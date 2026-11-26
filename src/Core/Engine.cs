using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiveDiag
{
    /// <summary>采集调度、直播识别、会话状态机、分析归因的总控。</summary>
    public class Engine : IDisposable
    {
        public Config Cfg;
        public LiveState State = LiveState.Idle;
        public SessionInfo Current;
        public LiveSnapshot Snap = new LiveSnapshot();
        public bool Degraded { get { return !_tcp.EstatsAvailable; } }
        public string LastError = "";
        public double AvgTickMs;
        public double MaxTickMs;
        public string TickProfile = "";
        /// <summary>仅用于自测：跳过"整机必须有上行"的门槛，便于在单机验证状态机。</summary>
        public bool TestMode;
        private double _tPerf, _tProc, _tTcp, _tTraffic, _tSample;
        private int _profN;
        private double _tickMsSum;
        private int _tickMsCount;
        private readonly System.Diagnostics.Stopwatch _tickWatch = new System.Diagnostics.Stopwatch();

        public event Action<EventRecord> OnEvent;
        public event Action<SessionInfo> OnSessionStarted;
        public event Action<SessionInfo> OnSessionEnded;

        private readonly Store _store;
        private readonly NetCollector _net = new NetCollector();
        private readonly TcpCollector _tcp = new TcpCollector();
        private readonly PerfCollector _perf = new PerfCollector();
        private readonly ProcessCollector _proc = new ProcessCollector();
        private readonly WlanCollector _wlan = new WlanCollector();

        private PingWorker _gwPing;
        private readonly List<PingWorker> _pubPings = new List<PingWorker>();
        private PingWorker _srvPing;

        // 接入网：网关之后第一跳（接入网出口）的定位与质检
        private readonly HopProbe _hopProbe = new HopProbe();
        private readonly object _hopLock = new object();
        private readonly List<HopPing> _hopRetired = new List<HopPing>();
        private HopPing _hopPing;
        private string _hopIp = "";
        private string _hopTarget = "";
        private string _hopPathText = "";
        private string _hopError = "";
        private long _lastHopSec;
        private long _lastHopDiscoverMs;
        private int _hopDiscovering;
        private bool _hopPendingFound;
        private bool _hopPendingFail;
        private string _hopPendingIp = "";
        private int _hopPendingTtl;
        private string _hopPendingPath = "";

        private System.Threading.Timer _timer;
        private int _tickGuard;
        private int _tick;

        private TrafficInfo _traffic = new TrafficInfo();
        private TcpSnapshot _tcpSnap = new TcpSnapshot();
        private PerfInfo _perfInfo = new PerfInfo();
        private WlanInfo _wlanInfo = new WlanInfo();
        private List<ProcStat> _procStats = new List<ProcStat>();
        private AdapterInfo _adapter = new AdapterInfo();

        private long _lastSampleSec;
        private long _lastAdapterSec;
        private long _lastProcSec;
        private long _lastGwSec;
        private long _lastPubSec;
        private long _lastSrvSec;
        private long _lastWlanSec;
        private long _lastDnsSec;

        private int _candidatePid;
        private int _candidateMiss;
        private string _candidateProc = "";
        private string _serverIp = "";
        private int _serverPort;
        private int _candidateHits;

        private long _probeStartSec;
        private long _gapStartSec;
        private int _gapCount;
        private long _gapTotalMs;
        private long _lastHasSec;
        private bool _manualGap;
        private bool _hadTraffic;
        private bool _netDownLogged;

        private readonly List<Sample> _recent = new List<Sample>();
        private readonly List<Sample> _sessionSamples = new List<Sample>();
        private readonly object _recentLock = new object();
        private readonly Dictionary<string, long> _lastEventSec = new Dictionary<string, long>();
        private Dictionary<string, int> _learning;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _ptrCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        private int _ptrBusy;
        private int _dnsMs = -1;
        private bool _dnsBusy;
        private long _sessionScoreSum;
        private int _sessionScoreCount;
        private long _sessionPeakUp;
        private long _sessionUpSum;
        private int _sessionUpCount;
        private long _lastDiagSec;

        public Engine(Config cfg)
        {
            Cfg = cfg;
            Config.EnsureDirs();
            _store = new Store();
            _learning = _store.LoadLearning();
            _store.Cleanup(Cfg);
            RepairOpenSessions();
            InitPings();
        }

        /// <summary>上次程序被强制结束时，收尾未闭合的会话，避免"永远进行中"。</summary>
        private void RepairOpenSessions()
        {
            try
            {
                List<SessionInfo> list = _store.LoadSessions();
                bool changed = false;
                long nowSec = Mono.UnixMs / 1000;
                foreach (SessionInfo s in list)
                {
                    if (s.EndTs != 0) continue;
                    List<Sample> sm = _store.LoadSamples(s.StartTs * 1000L, nowSec * 1000L);
                    long lastTs = sm.Count > 0 ? sm[sm.Count - 1].Ts : s.StartTs;
                    s.EndTs = lastTs;
                    s.EndReason = "程序异常退出，记录被截断";
                    s.SampleCount = sm.Count;
                    if (sm.Count > 0)
                    {
                        int sum = 0, cnt = 0;
                        long peak = 0, upSum = 0;
                        int upCnt = 0;
                        foreach (Sample x in sm)
                        {
                            if (x.Quality >= 0) { sum += x.Quality; cnt++; }
                            if (x.StreamUpBps > peak) peak = x.StreamUpBps;
                            if (x.StreamUpBps > 0) { upSum += x.StreamUpBps; upCnt++; }
                        }
                        s.Score = cnt > 0 ? sum / cnt : -1;
                        s.PeakUpBps = peak;
                        s.AvgUpBps = upCnt > 0 ? upSum / upCnt : 0;
                        Baseline bl = Analyzer.BuildBaseline(sm);
                        List<Finding> f = Analyzer.Evaluate(sm, bl, Degraded);
                        if (f.Count > 0) s.TopCause = f[0].Title;
                        s.ReportPath = Report.Build(s, sm, f, _store, Degraded);
                    }
                    changed = true;
                }
                if (changed) _store.SaveSessions(list);
            }
            catch (Exception ex) { WriteLog("repair sessions failed: " + ex); }
        }

        private void InitPings()
        {
            try
            {
                if (_gwPing != null) _gwPing.Dispose();
                _gwPing = null;
                if (_adapter.Gateway.Length > 0) _gwPing = new PingWorker(_adapter.Gateway);

                foreach (PingWorker p in _pubPings) p.Dispose();
                _pubPings.Clear();
                foreach (string t in Cfg.ParseTargets()) _pubPings.Add(new PingWorker(t));
            }
            catch (Exception ex) { LastError = ex.Message; }
        }

        public void Start()
        {
            if (_timer != null) return;
            _timer = new System.Threading.Timer(Tick, null, 300, 1000);
        }

        // ==================== 接入网第一跳（网关之后）====================

        /// <summary>周期性调度：没定位到就重试定位（异步逐跳扫描），定位到了就做一轮 TTL 受限探测。</summary>
        private void EnsureHopProbe()
        {
            string target = _serverIp;
            if (target.Length == 0)
            {
                string[] targets = Cfg.ParseTargets();
                if (targets.Length > 0) target = targets[0];
            }
            if (target.Length == 0) return;

            long nowMs = Mono.UnixMs;
            lock (_hopLock)
            {
                bool targetChanged = target != _hopTarget;
                bool needDiscover = targetChanged || _hopPing == null || _hopIp.Length == 0;
                bool canRetry = nowMs - _lastHopDiscoverMs >= 120000;
                if (needDiscover && canRetry && Interlocked.CompareExchange(ref _hopDiscovering, 1, 0) == 0)
                {
                    _hopTarget = target;
                    _lastHopDiscoverMs = nowMs;
                    int wantTtl = Cfg.HopTtl;
                    int maxTtl = Math.Max(Cfg.HopMaxTtl, wantTtl);
                    Task.Factory.StartNew(delegate { DiscoverHop(target, wantTtl, maxTtl); });
                    return;
                }
                if (_hopPing != null) _hopPing.RunAsync();
            }
        }

        /// <summary>逐跳扫描定位接入网出口；该跳不回 ICMP 时顺延到后面第一个有响应的跳。</summary>
        private void DiscoverHop(string target, int wantTtl, int maxTtl)
        {
            try
            {
                HopPath path = _hopProbe.Trace(target, maxTtl);
                string hop = path.HopAt(wantTtl);
                int used = wantTtl;
                if (hop.Length == 0)
                {
                    for (int ttl = wantTtl + 1; ttl <= path.MaxTtl; ttl++)
                    {
                        string h = path.HopAt(ttl);
                        if (h.Length > 0) { hop = h; used = ttl; break; }
                    }
                }

                lock (_hopLock)
                {
                    _hopPathText = path.PathText();
                    _hopError = path.Error;
                    if (hop.Length == 0)
                    {
                        _hopIp = "";
                        if (_hopPing != null) { _hopRetired.Add(_hopPing); _hopPing = null; }
                        if (!_hopPendingFail)
                        {
                            _hopPendingFail = true;
                            _hopPendingPath = _hopPathText;
                        }
                    }
                    else
                    {
                        if (hop != _hopIp || _hopPing == null)
                        {
                            _hopIp = hop;
                            if (_hopPing != null) _hopRetired.Add(_hopPing);
                            _hopPing = new HopPing(target, used);
                            _hopPendingFound = true;
                            _hopPendingIp = hop;
                            _hopPendingTtl = used;
                            _hopPendingPath = _hopPathText;
                        }
                        else
                        {
                            _hopPing.SetTtl(used);
                        }
                        _hopPendingFail = false;
                    }
                }
            }
            catch (Exception ex) { _hopError = ex.Message; }
            finally { Interlocked.Exchange(ref _hopDiscovering, 0); }
        }

        /// <summary>事件统一回到 tick 线程再写，避免与采样线程同时写事件文件。</summary>
        private void FlushHopEvents()
        {
            bool found = false, fail = false;
            string ip = "", path = "";
            int ttl = 0;
            lock (_hopLock)
            {
                if (_hopPendingFound) { found = true; ip = _hopPendingIp; ttl = _hopPendingTtl; path = _hopPendingPath; _hopPendingFound = false; }
                else if (_hopPendingFail) { fail = true; path = _hopPendingPath; _hopPendingFail = false; }
                // 回收旧句柄
                foreach (HopPing p in _hopRetired) { try { p.Dispose(); } catch { } }
                _hopRetired.Clear();
            }
            if (found)
                LogEvent("E19", 0, "已定位接入网出口（网关后第 " + Math.Max(1, ttl - 1) + " 跳）",
                    "接入网第一跳 " + ip + "，后续将持续测量它到本机的时延与丢包；探测目标 " + _hopTarget + "。",
                    "路径：" + path);
            else if (fail)
                LogEvent("E19", 1, "接入网第一跳未响应 ICMP",
                    "逐跳探测时目标跳没有回 ICMP 报文，接入网质量暂时无法评估（部分 BRAS 会过滤 ICMP）。",
                    "路径：" + path);
        }

        /// <summary>网络地址变化后重新定位接入网出口。</summary>
        private void ResetHop()
        {
            lock (_hopLock)
            {
                _hopTarget = "";
                _hopIp = "";
                _hopPathText = "";
                _lastHopDiscoverMs = 0;
                _lastHopSec = 0;
                if (_hopPing != null) { _hopRetired.Add(_hopPing); _hopPing = null; }
            }
        }

        private HopPing CurrentHopPing()
        {
            lock (_hopLock) return _hopPing;
        }

        public void Dispose()
        {
            if (_timer != null) { _timer.Dispose(); _timer = null; }
            if (_gwPing != null) _gwPing.Dispose();
            if (_srvPing != null) _srvPing.Dispose();
            foreach (PingWorker p in _pubPings) p.Dispose();
            lock (_hopLock)
            {
                if (_hopPing != null) { _hopPing.Dispose(); _hopPing = null; }
                foreach (HopPing p in _hopRetired) { try { p.Dispose(); } catch { } }
                _hopRetired.Clear();
            }
            _hopProbe.Dispose();
            _wlan.Dispose();
            _store.Dispose();
        }

        // ==================== 主循环 ====================
        private void Tick(object state)
        {
            if (Interlocked.CompareExchange(ref _tickGuard, 1, 0) != 0) return;
            long __t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                _tick++;
                long now = Mono.UnixMs / 1000;

                if (_tick == 1 || now - _lastAdapterSec >= 10)
                {
                    _lastAdapterSec = now;
                    string oldIp = _adapter.Ip;
                    string oldGw = _adapter.Gateway;
                    _adapter = _net.RefreshAdapter();
                    if (!_adapter.Up)
                    {
                        if (!_netDownLogged)
                        {
                            _netDownLogged = true;
                            LogEvent("E06", 2, "网络连接断开", "网卡不可用或未获取到地址：" + _net.LastError, "", true);
                        }
                    }
                    else if (_netDownLogged)
                    {
                        _netDownLogged = false;
                        LogEvent("E06", 1, "网络连接恢复", "IP " + _adapter.Ip + "，网关 " + _adapter.Gateway, "", true);
                    }
                    if (_adapter.Ip.Length > 0 && oldIp.Length > 0 && oldIp != _adapter.Ip)
                    {
                        LogEvent("E07", 1, "本机 IP 发生变化", "IP " + oldIp + " → " + _adapter.Ip, "");
                        ResetHop();   // 换了网络，接入网出口要重新定位
                    }
                    if (_adapter.Gateway.Length > 0 && oldGw.Length > 0 && oldGw != _adapter.Gateway)
                        LogEvent("E07", 1, "默认网关发生变化", "网关 " + oldGw + " → " + _adapter.Gateway, "");
                    if (_gwPing == null || _gwPing.Host != _adapter.Gateway) InitPings();
                }

                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                _traffic = _net.ReadTraffic();
                long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                _tTraffic += (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

                if (_tick % Math.Max(1, Cfg.PerfSeconds) == 0)
                {
                    _perfInfo = _perf.Read(_candidatePid);
                    long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                    _tPerf += (t2 - t1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                }

                if (now - _lastProcSec >= 10)
                {
                    _lastProcSec = now;
                    _procStats = _proc.Read();
                    long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
                    _tProc += (t3 - t1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                }

                if (now - _lastGwSec >= Cfg.PingGwSeconds && _gwPing != null)
                {
                    _lastGwSec = now;
                    _gwPing.RunAsync();
                }
                if (now - _lastPubSec >= Cfg.PingPubSeconds)
                {
                    _lastPubSec = now;
                    foreach (PingWorker p in _pubPings) p.RunAsync();
                }
                if (_srvPing != null && now - _lastSrvSec >= Cfg.PingSrvSeconds)
                {
                    _lastSrvSec = now;
                    _srvPing.RunAsync();
                }
                if (now - _lastHopSec >= Cfg.HopSeconds)
                {
                    _lastHopSec = now;
                    EnsureHopProbe();
                }
                FlushHopEvents();

                if (now - _lastWlanSec >= Cfg.WlanSeconds)
                {
                    _lastWlanSec = now;
                    _wlanInfo = _wlan.Read();
                }

                if (now - _lastDnsSec >= Cfg.DnsSeconds)
                {
                    _lastDnsSec = now;
                    ProbeDns();
                }

                if (now - _lastSampleSec >= Cfg.SampleSeconds)
                {
                    _lastSampleSec = now;
                    UpdateTcp();
                    BuildSample(now);
                }
                else if (now - _lastSampleSec >= 1 && _tick % Math.Max(1, Cfg.TcpSeconds) == 0)
                {
                    UpdateTcp();
                }

                UpdateSnapshot();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                WriteLog("tick error: " + ex);
            }
            finally
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - __t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                _tickMsSum += ms;
                _tickMsCount++;
                if (ms > MaxTickMs) MaxTickMs = ms;
                if (_tickMsCount >= 10)
                {
                    AvgTickMs = _tickMsSum / _tickMsCount;
                    _tickMsSum = 0;
                    _tickMsCount = 0;
                }
                Interlocked.Exchange(ref _tickGuard, 0);
            }
        }

        private void ProbeDns()
        {
            if (_dnsBusy) return;
            _dnsBusy = true;
            string host = Cfg.DnsProbeHost;
            Task.Factory.StartNew(delegate
            {
                try
                {
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    IPAddress[] addrs = Dns.GetHostAddresses(host);
                    sw.Stop();
                    _dnsMs = (int)sw.ElapsedMilliseconds;
                    if (addrs == null || addrs.Length == 0) _dnsMs = 999;
                }
                catch { _dnsMs = 999; }
                finally { _dnsBusy = false; }
            });
        }

        private void UpdateTcp()
        {
            try
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                _tcpSnap = _tcp.Read();
                DetectCandidate();
                _tTcp += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
            catch (Exception ex) { LastError = ex.Message; }
        }

        // ==================== 直播识别 ====================
        private void DetectCandidate()
        {
            string[] keywords = Cfg.ParseKeywords();
            int[] ports = Cfg.ParseStreamPorts();
            HashSet<int> portSet = new HashSet<int>();
            foreach (int p in ports) portSet.Add(p);

            // 候选来源：TCP(IPv4/IPv6) + UDP。抖音直播伴侣走 RTMPS(443)，视频号/OBS 走 1935；
            // 若推流走 SRT/QUIC 则只有 UDP 端点，TCP 表里根本看不到。
            HashSet<int> pids = new HashSet<int>();
            foreach (ConnInfo c in _tcpSnap.Conns) pids.Add(c.Pid);
            foreach (ConnInfo c in _tcpSnap.Conns6) pids.Add(c.Pid);
            foreach (UdpEp u in _tcpSnap.Udp) pids.Add(u.Pid);
            if (_candidatePid > 0) pids.Add(_candidatePid);

            double threshold = Cfg.CandidateKbps * 1000.0;
            int bestPid = 0;
            double bestScore = -1;
            double bestBps = 0;
            bool machineBusy = TestMode || _traffic.UpBps >= threshold;

            foreach (int pid in pids)
            {
                if (pid <= 4) continue;
                double connBps = _tcpSnap.UpOf(pid);
                // 无 ESTATS 权限时，进程 IO 写出量（含 socket 写出）是最可靠的"谁在上行"代理指标
                double procWriteBps = WriteBpsOf(pid);
                double bps = connBps > 0 ? connBps : 0;
                string name = _proc.NameOf(pid).ToLowerInvariant();
                bool kwHit = MatchesKeyword(name, keywords);
                bool portHit = _tcpSnap.HasRemotePort(pid, portSet);
                bool rtmpHit = _tcpSnap.HasRemotePort(pid, 1935) || _tcpSnap.HasRemotePort(pid, 1936);
                bool udpHit = _tcpSnap.HasUdpPort(pid, portSet);
                string host = ResolvePtr(pid, portSet);
                bool dnsHit = host.Length > 0 && MatchesKeyword(host.ToLowerInvariant(), PushDomainKeys);

                bool featureHit = rtmpHit || portHit || kwHit || dnsHit || udpHit;
                if (!featureHit) continue;

                // 证据强度决定门槛：1935/反解到推流域名属硬证据；
                // 只有"进程名 + 443"这种弱证据时，要求整机上行达到 3 倍阈值（≥600kbps），
                // 否则微信/浏览器这类只连着 443 的进程会被误判成直播（今天真的发生过）。
                bool strong = rtmpHit || dnsHit;
                double gate = strong ? threshold * 0.5 : threshold * 3;

                // 无逐连接统计权限时：整机确实在上行 + 该进程具备推流特征 → 视为候选，
                // 速率按整机上行估算（界面与报告中都会标注为估算模式）。
                if (bps < gate && machineBusy) bps = _traffic.UpBps;
                if (TestMode) bps = Math.Max(bps, threshold * 4);
                if (bps < gate) continue;

                // 端口分级：1935/1936 是 RTMP 专用端口，属硬证据；443 遍地都是，需要进程名配合
                double score = 0;
                if (rtmpHit) score += 5;
                else if (portHit || udpHit) score += 1;
                if (kwHit) score += 3;
                if (dnsHit) score += 4;
                // 关键补丁：抖音直播伴侣等用 RTMPS(443)/UDP 推流时，"进程名 + 推流端口"就是全部证据，
                // 旧规则下只有 4 分（< 5）永远识别不出来。
                if (kwHit && (portHit || udpHit || rtmpHit)) score += 2;
                // 该进程自身确实在大流量写出，可压过浏览器/微信这类同样连着 443 的进程
                if (procWriteBps >= threshold) score += 2;
                if (procWriteBps >= threshold * 5) score += 1;
                if (connBps >= threshold) score += 2;
                if (pid == _candidatePid) score += 1;
                int learned;
                if (_learning.TryGetValue(name, out learned)) score += learned == 1 ? 6 : -6;

                if (score > bestScore) { bestScore = score; bestPid = pid; bestBps = bps; }
            }

            if (bestPid > 0 && bestScore >= 5)
            {
                if (bestPid != _candidatePid)
                {
                    string newName = _proc.NameOf(bestPid);
                    if (Current != null && _candidatePid > 0)
                        LogEvent("E14", 1, "推流进程发生变化", _candidateProc + " → " + newName, "");
                    _candidatePid = bestPid;
                    _candidateProc = newName;
                    _candidateHits = 0;
                    _tcp.ResetBaseline();
                }
                _candidateHits++;
                _candidateMiss = 0;
                _lastHasSec = Mono.UnixMs / 1000;
                if (Current != null) _hadTraffic = true;
                UpdateServerIp(bestPid, portSet);
            }
            else
            {
                // 连续两次未命中才清除，避免采样抖动导致状态反复
                _candidateHits = 0;
                _candidateMiss++;
                if (_candidateMiss >= 2)
                {
                    _candidatePid = 0;
                    _candidateProc = "";
                }
                // 只有明显上行（≥1 Mbps）才提示，避免网盘/云同步这类小流量刷屏
                if (machineBusy && _traffic.UpBps >= Math.Max(threshold * 3, 1000000.0)) DiagnoseDetectMiss();
            }
        }

        private double WriteBpsOf(int pid)
        {
            foreach (ProcStat st in _procStats)
                if (st.Pid == pid) return st.WriteBps;
            return 0;
        }

        /// <summary>整机在明显上行、但没识别出推流进程时，把现场证据写进事件与 diag 目录。</summary>
        private void DiagnoseDetectMiss()
        {
            try
            {
                long now = Mono.UnixMs / 1000;
                if (now - _lastDiagSec < 60) return;
                _lastDiagSec = now;

                int[] ports = Cfg.ParseStreamPorts();
                HashSet<int> portSet = new HashSet<int>();
                foreach (int p in ports) portSet.Add(p);

                StringBuilder ev = new StringBuilder();
                int n = 0;
                foreach (ConnInfo c in _tcpSnap.Conns)
                {
                    if (!portSet.Contains(c.RemotePort)) continue;
                    if (n++ >= 6) break;
                    if (ev.Length > 0) ev.Append("；");
                    ev.Append(_proc.NameOf(c.Pid)).Append("→").Append(c.RemoteIp).Append(":").Append(c.RemotePort);
                }
                if (n == 0) ev.Append("TCP 表中没有命中任何推流端口");

                LogEvent("E18", Current != null ? 2 : 1, "整机在上行但识别不到推流进程",
                    "上行 " + Fmt.Bps((int)_traffic.UpBps) + "，但没有任何进程同时满足「推流端口/协议 + 进程名」条件，" +
                    "本次会话将按整机流量估算。连接明细已存到 data\\diag 目录，可据此确认直播软件的实际进程名与远端端口。",
                    ev.ToString());

                DumpConnDiag();
            }
            catch { }
        }

        /// <summary>把当前 TCP(IPv4/IPv6) 与 UDP 连接明细落盘，便于事后分析为什么没命中。</summary>
        private void DumpConnDiag()
        {
            try
            {
                Directory.CreateDirectory(Config.DiagDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  整机上行 " + Fmt.Bps((int)_traffic.UpBps));
                sb.AppendLine("# 判定条件: 候选阈值 " + Cfg.CandidateKbps + " kbps，端口 " + Cfg.StreamPorts);
                sb.AppendLine("# 关键词 " + Cfg.StreamProcessKeywords);
                sb.AppendLine();
                sb.AppendLine("[进程 IO 写出 Top10]");
                List<ProcStat> stats = new List<ProcStat>(_procStats);
                stats.Sort(delegate(ProcStat a, ProcStat b) { return b.WriteBps.CompareTo(a.WriteBps); });
                for (int i = 0; i < stats.Count && i < 10; i++)
                    sb.AppendLine(stats[i].Name + "(" + stats[i].Pid + ") 写出 " + Fmt.Bps((int)stats[i].WriteBps) +
                        "  CPU " + Fmt.Pct((int)stats[i].CpuPm));
                sb.AppendLine();
                sb.AppendLine("[TCP IPv4]");
                foreach (ConnInfo c in _tcpSnap.Conns)
                    sb.AppendLine(_proc.NameOf(c.Pid) + "(" + c.Pid + ") " + c.LocalIp + ":" + c.LocalPort +
                        " -> " + c.RemoteIp + ":" + c.RemotePort + " state=" + c.State);
                sb.AppendLine();
                sb.AppendLine("[TCP IPv6]");
                foreach (ConnInfo c in _tcpSnap.Conns6)
                    sb.AppendLine(_proc.NameOf(c.Pid) + "(" + c.Pid + ") [" + c.LocalIp + "]:" + c.LocalPort +
                        " -> [" + c.RemoteIp + "]:" + c.RemotePort + " state=" + c.State);
                sb.AppendLine();
                sb.AppendLine("[UDP]");
                foreach (UdpEp u in _tcpSnap.Udp)
                    sb.AppendLine(_proc.NameOf(u.Pid) + "(" + u.Pid + ") " + u.LocalIp + ":" + u.LocalPort);

                string file = Path.Combine(Config.DiagDir, "conn-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                TrimDiagFiles();
            }
            catch { }
        }

        private static void TrimDiagFiles()
        {
            try
            {
                string[] files = Directory.GetFiles(Config.DiagDir, "conn-*.txt");
                if (files.Length <= 20) return;
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length - 20; i++) File.Delete(files[i]);
            }
            catch { }
        }

        private static readonly string[] PushDomainKeys = new string[] {
            "push", "rtmp", "live", "stream", "ingest", "cdn", "uplink", "push-", "publish"
        };

        /// <summary>对候选进程的推流端口对端做反向解析（带缓存、限并发），用于识别推流 CDN 域名。</summary>
        private string ResolvePtr(int pid, HashSet<int> portSet)
        {
            foreach (ConnInfo c in _tcpSnap.Conns)
            {
                if (c.Pid != pid) continue;
                if (!portSet.Contains(c.RemotePort)) continue;
                if (IsPrivate(c.RemoteIp)) continue;
                string host;
                if (!_ptrCache.TryGetValue(c.RemoteIp, out host))
                {
                    _ptrCache[c.RemoteIp] = "";      // 占位，避免重复发起
                    RequestPtr(c.RemoteIp);
                    continue;
                }
                if (host.Length > 0) return host;
            }
            return "";
        }

        private void RequestPtr(string ip)
        {
            if (System.Threading.Interlocked.CompareExchange(ref _ptrBusy, 1, 0) != 0) return;
            Task.Factory.StartNew(delegate
            {
                try
                {
                    IPHostEntry entry = Dns.GetHostEntry(ip);
                    if (entry != null && entry.HostName != null) _ptrCache[ip] = entry.HostName;
                }
                catch { }
                finally { System.Threading.Interlocked.Exchange(ref _ptrBusy, 0); }
            });
        }

        private static bool MatchesKeyword(string name, string[] keywords)
        {
            foreach (string k in keywords)
                if (k.Length > 0 && name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void UpdateServerIp(int pid, HashSet<int> portSet)
        {
            string srv = "";
            int srvPort = 0;
            int bestRank = 99;
            foreach (ConnInfo c in _tcpSnap.Conns)
            {
                if (c.Pid != pid) continue;
                int rank = 99;
                if (c.RemotePort == 1935 || c.RemotePort == 1936) rank = 0;
                else if (c.RemotePort == 443) rank = 1;
                else if (portSet.Contains(c.RemotePort)) rank = 2;
                else continue;
                if (IsPrivate(c.RemoteIp)) rank += 3;
                if (rank < bestRank) { bestRank = rank; srv = c.RemoteIp; srvPort = c.RemotePort; }
            }
            // IPv6 推流：IPv4 表里没有，单独再扫一遍（同端口优先级）
            if (bestRank > 1)
            {
                foreach (ConnInfo c in _tcpSnap.Conns6)
                {
                    if (c.Pid != pid) continue;
                    int rank = 99;
                    if (c.RemotePort == 1935 || c.RemotePort == 1936) rank = 0;
                    else if (c.RemotePort == 443) rank = 1;
                    else if (portSet.Contains(c.RemotePort)) rank = 2;
                    else continue;
                    if (c.RemoteIp.StartsWith("fe80") || c.RemoteIp == "::1") rank += 3;
                    if (rank < bestRank) { bestRank = rank; srv = c.RemoteIp; srvPort = c.RemotePort; }
                }
            }
            if (srv.Length == 0 || (srv == _serverIp && srvPort == _serverPort)) return;

            string old = _serverIp;
            _serverIp = srv;
            _serverPort = srvPort;
            if (_srvPing != null) { _srvPing.Dispose(); _srvPing = null; }
            IPAddress parsed;
            if (IPAddress.TryParse(srv, out parsed) && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                _srvPing = new PingWorker(srv);
            _lastSrvSec = 0;
            if (old.Length > 0)
                LogEvent("E14", 1, "推流服务器变化", old + " → " + srv + ":" + srvPort, "");
        }

        private static bool IsPrivate(string ip)
        {
            if (ip.StartsWith("10.") || ip.StartsWith("127.") || ip.StartsWith("192.168.") || ip.StartsWith("169.254.")) return true;
            if (ip.StartsWith("172."))
            {
                string[] p = ip.Split('.');
                int b;
                if (p.Length == 4 && int.TryParse(p[1], out b) && b >= 16 && b <= 31) return true;
            }
            return false;
        }

        // ==================== 采样与会话 ====================
        private void BuildSample(long nowSec)
        {
            long tStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                BuildSampleCore(nowSec);
            }
            finally
            {
                _tSample += (System.Diagnostics.Stopwatch.GetTimestamp() - tStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                _profN++;
                if (_profN >= 8)
                {
                    TickProfile = string.Format("perf={0:0.00}ms proc={1:0.00}ms tcp={2:0.00}ms traffic={3:0.00}ms samplePath={4:0.00}ms (每 tick 均摊)",
                        _tPerf / _profN, _tProc / _profN, _tTcp / _profN, _tTraffic / _profN, _tSample / _profN);
                    _tPerf = _tProc = _tTcp = _tTraffic = _tSample = 0;
                    _profN = 0;
                }
            }
        }

        private void BuildSampleCore(long nowSec)
        {
            Sample s = new Sample();
            s.Ts = nowSec;
            s.UpBps = (int)_traffic.UpBps;
            s.DownBps = (int)_traffic.DownBps;
            s.LinkUtilPm = _traffic.LinkUtilPm;

            bool hasCandidate = _candidatePid > 0;
            bool precise = _tcp.EstatsAvailable;
            double streamBps = 0;
            if (hasCandidate)
            {
                if (precise) streamBps = _tcpSnap.UpOf(_candidatePid);
                if (streamBps <= 0) streamBps = _traffic.UpBps;
            }
            else if (Current != null)
            {
                // 手动会话或未识别进程时，用整机上行作为推流上行的估算值
                streamBps = _traffic.UpBps;
            }
            s.StreamUpBps = (int)streamBps;

            if (s.UpBps > 0 && hasCandidate)
            {
                int other = (int)((1.0 - Math.Min(1.0, streamBps / s.UpBps)) * 1000);
                s.OtherUpPm = Math.Max(0, Math.Min(1000, other));
            }
            else s.OtherUpPm = -1;

            if (_gwPing != null && _gwPing.Result.HasValue && !_gwPing.Result.NoResponse)
            {
                s.GwRttMs = _gwPing.Result.RttMs;
                s.GwJitMs = _gwPing.Result.JitMs;
                s.GwLossPm = _gwPing.Result.LossPm;
            }
            if (_pubPings.Count > 0 && _pubPings[0].Result.HasValue && !_pubPings[0].Result.NoResponse)
            {
                s.PubRttMs = _pubPings[0].Result.RttMs;
                s.PubLossPm = _pubPings[0].Result.LossPm;
            }
            if (_srvPing != null && _srvPing.Result.HasValue && !_srvPing.Result.NoResponse)
            {
                s.SrvRttMs = _srvPing.Result.RttMs;
                s.SrvJitMs = _srvPing.Result.JitMs;
                s.SrvLossPm = _srvPing.Result.LossPm;
            }
            // 接入网第一跳（网关之后）：这一段才是"接入网"，故障概率远高于核心网
            HopPing hp = CurrentHopPing();
            if (hp != null && hp.Result.HasValue && !hp.Result.NoResponse)
            {
                s.HopRttMs = hp.Result.RttMs;
                s.HopJitMs = hp.Result.JitMs;
                s.HopLossPm = hp.Result.LossPm;
                s.HopIp = hp.HopIp;
                s.HopTtl = hp.Ttl;
            }
            s.DnsMs = _dnsMs;
            s.RetransPm = -1;
            s.CwndKb = -1;
            s.RtoCount = -1;
            s.SynRetrans = _tcpSnap.SynSent;
            s.TcpConn = _tcpSnap.Total;
            s.NewConnPer10 = (int)Math.Min(999, Math.Round(_tcpSnap.NewPerSec * 10));
            s.CpuTotalPm = _perfInfo.CpuPm;
            s.CpuStreamPm = _perfInfo.CpuStreamPm;
            s.GpuEncPm = _perfInfo.GpuEncPm;
            s.Gpu3dPm = _perfInfo.Gpu3dPm;
            s.MemPm = _perfInfo.MemPm;
            s.DiskQPm10 = _perfInfo.DiskQPm10;
            if (_wlanInfo.Valid)
            {
                s.WifiRssi = _wlanInfo.Rssi;
                int txMbps = _wlanInfo.TxKbps / 1000;
                if (txMbps < 0) txMbps = 0;
                if (txMbps > 254) txMbps = 254;
                s.WifiTx10 = txMbps * 10;
            }
            else s.WifiRssi = -1000;

            UpdateStateMachine(s);

            SampleFlags flags = SampleFlags.None;
            if (Current != null) flags |= SampleFlags.SessionRunning;
            if (State == LiveState.Probing) flags |= SampleFlags.Candidate;
            if (State == LiveState.Gap || (State == LiveState.Live && _manualGap)) flags |= SampleFlags.InGap;
            if (_adapter.Wired) flags |= SampleFlags.Wired;
            if (!precise) flags |= SampleFlags.Degraded;
            if (Current != null && Current.Manual) flags |= SampleFlags.Manual;
            if (_wlanInfo.Valid) flags |= SampleFlags.WifiValid;
            s.Flags = flags;

            Baseline bl = BuildBaseline();
            s.Quality = Analyzer.Score(s, bl);

            _store.Append(s);
            lock (_recentLock)
            {
                _recent.Add(s);
                if (_recent.Count > 720) _recent.RemoveAt(0);
            }
            if (Current != null)
            {
                _sessionSamples.Add(s);
                _sessionScoreSum += s.Quality;
                _sessionScoreCount++;
                if (s.StreamUpBps > _sessionPeakUp) _sessionPeakUp = s.StreamUpBps;
                if (s.StreamUpBps > 0) { _sessionUpSum += s.StreamUpBps; _sessionUpCount++; }
            }

            if (Current != null && _sessionSamples.Count % 24 == 0) PersistSession();

            RunAnalysis(bl);
        }

        private Baseline BuildBaseline()
        {
            List<Sample> src = Current != null ? _sessionSamples : _recent;
            int take = Math.Min(src.Count, Current != null ? 60 : 60);
            List<Sample> window = new List<Sample>();
            for (int i = src.Count - take; i < src.Count; i++)
                if (i >= 0) window.Add(src[i]);
            Baseline b = Analyzer.BuildBaseline(window);
            b.Degraded = Degraded;
            return b;
        }

        private void UpdateStateMachine(Sample s)
        {
            long now = s.Ts;
            bool has = _candidatePid > 0;

            switch (State)
            {
                case LiveState.Idle:
                    if (has)
                    {
                        State = LiveState.Probing;
                        _probeStartSec = now;
                        LogEvent("E01", 0, "检测到疑似直播流量",
                            _candidateProc + " 上行约 " + Fmt.Bps(s.StreamUpBps) + "，确认中……", "");
                    }
                    break;

                case LiveState.Probing:
                    if (!has)
                    {
                        State = LiveState.Idle;
                        LogEvent("E17", 0, "疑似流量已消失", "未持续达到判定条件，视为误触发。", "");
                    }
                    else if (now - _probeStartSec >= Cfg.ProbeSeconds)
                    {
                        StartSession(Math.Max(_probeStartSec, now - Cfg.ProbeSeconds), false);
                    }
                    break;

                case LiveState.Live:
                    if (!has)
                    {
                        if (Current != null && Current.Manual)
                        {
                            // 手动会话不因无流量而结束，但仍标记断流事实
                            if (_hadTraffic && !_manualGap && now - _lastHasSec >= Cfg.GapSeconds)
                            {
                                _manualGap = true;
                                _gapStartSec = now;
                                _gapCount++;
                                LogEvent("E03", 2, "推流流量中断",
                                    "已持续 " + Cfg.GapSeconds + " 秒无直播流量（手动记录继续）。", "");
                            }
                            if (_manualGap && now - _lastHasSec >= 1800)
                                EndSession("30 分钟无直播流量，自动结束", now);
                        }
                        else
                        {
                            State = LiveState.Gap;
                            _gapStartSec = now;
                            _gapCount++;
                            LogEvent("E03", 2, "推流流量中断",
                                "已持续 " + Cfg.GapSeconds + " 秒无直播流量，开始观察（连续中断超过 " + EndRuleText() + " 判定结束）。", "");
                        }
                    }
                    break;

                case LiveState.Gap:
                    if (has)
                    {
                        long dur = now - _gapStartSec;
                        _gapTotalMs += dur * 1000;
                        State = LiveState.Live;
                        _manualGap = false;
                        LogEvent("E04", 1, "推流流量恢复",
                            "本次中断 " + Fmt.ShortDur(dur * 1000) + "，累计中断 " + Fmt.ShortDur(_gapTotalMs) + "。", "");
                    }
                    else if (!(Current != null && Current.Manual) && now - _gapStartSec >= Cfg.EndSeconds)
                    {
                        EndSession("流量中断超过 " + EndRuleText(), now);
                    }
                    break;
            }
        }

        private string EndRuleText()
        {
            return Cfg.EndSeconds >= 60 ? (Cfg.EndSeconds / 60) + " 分钟" : Cfg.EndSeconds + " 秒";
        }

        private void StartSession(long startSec, bool manual)
        {
            Current = new SessionInfo();
            Current.Id = "S" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            Current.StartTs = startSec;
            Current.StreamProc = _candidateProc;
            Current.ServerIp = _serverIp;
            Current.LocalIp = _adapter.Ip;
            Current.AdapterName = _adapter.Name;
            Current.Manual = manual;
            State = LiveState.Live;
            _gapCount = 0;
            _gapTotalMs = 0;
            _manualGap = false;
            _hadTraffic = _candidatePid > 0;
            _lastHasSec = manual ? startSec : _lastHasSec;
            _sessionSamples.Clear();
            _sessionScoreSum = 0;
            _sessionScoreCount = 0;
            _sessionPeakUp = 0;
            _sessionUpSum = 0;
            _sessionUpCount = 0;
            PersistSession();
            LogEvent("E02", 0, manual ? "手动开始记录直播" : "直播会话开始",
                "推流进程：" + (Current.StreamProc.Length > 0 ? Current.StreamProc : "未识别") +
                "，推流服务器：" + (Current.ServerIp.Length > 0 ? Current.ServerIp + ":" + _serverPort : "未识别"), "", true);
            if (OnSessionStarted != null) OnSessionStarted(Current);
        }

        public void StartManual()
        {
            if (State == LiveState.Live || State == LiveState.Gap) return;
            StartSession(Mono.UnixMs / 1000, true);
        }

        public void StopManual()
        {
            if (Current == null) return;
            EndSession("手动结束", Mono.UnixMs / 1000);
        }

        private void EndSession(string reason, long endSec)
        {
            if (Current == null) return;
            Current.EndTs = endSec;
            Current.EndReason = reason;
            Current.SampleCount = _sessionSamples.Count;
            Current.GapCount = 0;
            Current.GapTotalMs = 0;
            Current.PeakUpBps = _sessionPeakUp;
            Current.AvgUpBps = _sessionUpCount > 0 ? _sessionUpSum / _sessionUpCount : 0;
            Current.Score = _sessionScoreCount > 0 ? (int)(_sessionScoreSum / _sessionScoreCount) : -1;

            // 统计中断
            int gaps = 0;
            long gapMs = 0;
            bool inGap = false;
            long gapStart = 0;
            foreach (Sample s in _sessionSamples)
            {
                bool g = (s.Flags & SampleFlags.InGap) != 0;
                if (g && !inGap) { inGap = true; gapStart = s.Ts; gaps++; }
                else if (!g && inGap) { inGap = false; gapMs += (s.Ts - gapStart) * 1000; }
            }
            if (inGap && _sessionSamples.Count > 0)
                gapMs += (_sessionSamples[_sessionSamples.Count - 1].Ts - gapStart) * 1000;
            Current.GapCount = gaps;
            Current.GapTotalMs = gapMs;

            State = LiveState.Idle;
            SessionInfo finished = Current;
            LogEvent("E05", 0, "直播会话结束", "结束原因：" + reason + "，时长 " + finished.DurationText, "", true);
            Current = null;
            _candidateHits = 0;
            _candidatePid = 0;
            _candidateProc = "";

            List<Sample> samples = new List<Sample>(_sessionSamples);
            SessionInfo captured = finished;
            Task.Factory.StartNew(delegate
            {
                try
                {
                    Baseline bl = Analyzer.BuildBaseline(samples);
                    List<Finding> findings = Analyzer.Evaluate(samples, bl, Degraded);
                    if (findings.Count > 0) captured.TopCause = findings[0].Title;
                    Store st = new Store();
                    captured.ReportPath = Report.Build(captured, samples, findings, st, Degraded);
                    st.UpsertSession(captured);
                    st.Dispose();
                    if (OnSessionEnded != null) OnSessionEnded(captured);
                }
                catch (Exception ex) { WriteLog("report error: " + ex); }
            });

            _sessionSamples.Clear();
        }

        private void PersistSession()
        {
            if (Current == null) return;
            try
            {
                SessionInfo tmp = Current;
                tmp.SampleCount = _sessionSamples.Count;
                tmp.PeakUpBps = _sessionPeakUp;
                tmp.AvgUpBps = _sessionUpCount > 0 ? _sessionUpSum / _sessionUpCount : 0;
                tmp.Score = _sessionScoreCount > 0 ? (int)(_sessionScoreSum / _sessionScoreCount) : -1;
                _store.UpsertSession(tmp);
            }
            catch { }
        }

        private void RunAnalysis(Baseline bl)
        {
            List<Sample> window = new List<Sample>();
            lock (_recentLock)
            {
                int take = Math.Min(_recent.Count, 12);
                for (int i = _recent.Count - take; i < _recent.Count; i++) if (i >= 0) window.Add(_recent[i]);
            }
            if (window.Count < 3) return;
            List<Finding> findings = Analyzer.Evaluate(window, bl, Degraded);
            foreach (Finding f in findings)
            {
                if (f.Severity <= 0) continue;
                LogEvent("R_" + f.Code, f.Severity, f.Title, f.Detail, f.Evidence + "｜" + Analyzer.TimeRange(f.FromTs, f.ToTs));
            }
        }

        // ==================== 事件与快照 ====================
        private void LogEvent(string code, int severity, string title, string detail, string evidence)
        {
            LogEvent(code, severity, title, detail, evidence, false);
        }

        private void LogEvent(string code, int severity, string title, string detail, string evidence, bool force)
        {
            try
            {
                long now = Mono.UnixMs / 1000;
                long last;
                if (!force && _lastEventSec.TryGetValue(code, out last) && now - last < 120) return;
                _lastEventSec[code] = now;

                EventRecord e = new EventRecord();
                e.Ts = now;
                e.SessionId = Current != null ? Current.Id : "";
                e.Code = code;
                e.Severity = severity;
                e.Title = title;
                e.Detail = detail;
                e.Evidence = evidence;
                _store.AppendEvent(e);
                if (OnEvent != null) OnEvent(e);
            }
            catch { }
        }

        public void MarkCurrentAsLive(bool isLive)
        {
            string name = _candidateProc.Length > 0 ? _candidateProc : (_candidatePid > 0 ? _proc.NameOf(_candidatePid) : "");
            if (name.Length == 0)
            {
                LogEvent("E15", 1, "无法标记", "当前没有识别到任何上行进程，请先启动直播软件。", "", true);
                return;
            }
            _learning[name.ToLowerInvariant()] = isLive ? 1 : 0;
            _store.SaveLearning(_learning);
            LogEvent("E15", 0, isLive ? "已标记为直播程序" : "已标记为非直播程序",
                name + "，后续会话将" + (isLive ? "优先" : "不再") + "按此判定。", "", true);
            if (isLive && (State == LiveState.Idle || State == LiveState.Probing))
            {
                StartSession(Mono.UnixMs / 1000, true);
            }
        }

        public Sample[] RecentSamples()
        {
            lock (_recentLock)
            {
                return _recent.ToArray();
            }
        }

        public void UpdateSnapshot()
        {
            LiveSnapshot s = new LiveSnapshot();
            s.Ts = Mono.UnixMs / 1000;
            s.State = State;
            s.SessionId = Current != null ? Current.Id : "";
            s.SessionStartTs = Current != null ? Current.StartTs : 0;
            s.UpBps = (int)_traffic.UpBps;
            s.DownBps = (int)_traffic.DownBps;
            s.LinkUtilPm = _traffic.LinkUtilPm;
            if (_gwPing != null && _gwPing.Result.HasValue && !_gwPing.Result.NoResponse)
            {
                s.GwRtt = _gwPing.Result.RttMs;
                s.GwLossPm = _gwPing.Result.LossPm;
                s.GwJit = _gwPing.Result.JitMs;
            }
            if (_pubPings.Count > 0 && _pubPings[0].Result.HasValue && !_pubPings[0].Result.NoResponse)
            {
                s.PubRtt = _pubPings[0].Result.RttMs;
                s.PubLossPm = _pubPings[0].Result.LossPm;
            }
            if (_srvPing != null && _srvPing.Result.HasValue && !_srvPing.Result.NoResponse)
            {
                s.SrvRtt = _srvPing.Result.RttMs;
                s.SrvLossPm = _srvPing.Result.LossPm;
                s.SrvJit = _srvPing.Result.JitMs;
            }
            HopPing hopPing = CurrentHopPing();
            s.HopTarget = _hopTarget;
            if (hopPing != null)
            {
                s.HopIp = hopPing.HopIp;
                s.HopTtl = hopPing.Ttl;
                s.HopFound = hopPing.HopIp.Length > 0;
                if (hopPing.Result.HasValue && !hopPing.Result.NoResponse)
                {
                    s.HopRtt = hopPing.Result.RttMs;
                    s.HopLossPm = hopPing.Result.LossPm;
                    s.HopJit = hopPing.Result.JitMs;
                }
            }
            s.DnsMs = _dnsMs;
            s.TcpConn = _tcpSnap.Total;
            s.SynSent = _tcpSnap.SynSent;
            s.NewConnPer10 = (int)Math.Round(_tcpSnap.NewPerSec * 10);
            s.CpuPm = _perfInfo.CpuPm;
            s.MemPm = _perfInfo.MemPm;
            s.DiskQPm10 = _perfInfo.DiskQPm10;
            s.GpuEncPm = _perfInfo.GpuEncPm;
            s.WifiRssi = _wlanInfo.Valid ? _wlanInfo.Rssi : -1000;
            s.WifiQuality = _wlanInfo.Valid ? _wlanInfo.Quality : -1;
            s.WifiTxKbps = _wlanInfo.Valid ? _wlanInfo.TxKbps : -1;
            s.AdapterName = _adapter.Name;
            s.LocalIp = _adapter.Ip;
            s.Gateway = _adapter.Gateway;
            s.Dns = _adapter.DnsText;
            s.Wired = _adapter.Wired;
            s.StreamProc = _candidateProc;
            s.StreamPid = _candidatePid;
            s.ServerIp = _serverIp;
            s.ServerPort = _serverPort;
            s.StreamUpBps = (int)_traffic.UpBps;
            s.Degraded = Degraded;
            s.NetworkUp = _adapter.Up;
            s.DegradedReason = Degraded ? "无逐连接统计权限，上行按整机估算" : "";
            s.GapCount = _gapCount;
            s.GapTotalMs = _gapTotalMs;
            if (Current != null)
            {
                s.Quality = _sessionScoreCount > 0 ? (int)(_sessionScoreSum / _sessionScoreCount) : -1;
                if (_candidatePid > 0 && _tcp.EstatsAvailable) s.StreamUpBps = (int)_tcpSnap.UpOf(_candidatePid);
            }
            if (_tcpSnap != null && _candidatePid > 0)
            {
                if (_tcpSnap.PidUp.ContainsKey(_candidatePid) && _tcp.EstatsAvailable)
                    s.StreamUpBps = (int)_tcpSnap.UpOf(_candidatePid);
            }
            // 找上行最大的其他进程
            double bestOther = 0; int bestPid = 0;
            foreach (ProcStat st in _procStats)
            {
                if (st.Pid == _candidatePid) continue;
                if (st.WriteBps > bestOther) { bestOther = st.WriteBps; bestPid = st.Pid; }
            }
            if (bestPid > 0 && bestOther > 200000)
            {
                s.OtherUpProc = _proc.NameOf(bestPid);
                if (s.UpBps > 0) s.OtherUpPm = (int)Math.Min(1000, bestOther / s.UpBps * 1000);
            }
            Snap = s;
        }

        public void ReloadConfig(Config cfg)
        {
            Cfg = cfg;
            InitPings();
        }

        public static void WriteLog(string msg)
        {
            try
            {
                Directory.CreateDirectory(Config.LogDir);
                string f = Path.Combine(Config.LogDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
