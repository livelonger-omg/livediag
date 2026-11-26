using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LiveDiag
{
    /// <summary>Unix 时间戳换算（秒/毫秒 → 本地时间）。集中一处，避免各处重复换算出错。</summary>
    public static class Ux
    {
        public static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static DateTime ToLocal(long unixSeconds)
        {
            if (unixSeconds <= 0) return DateTime.MinValue;
            return Epoch.AddSeconds(unixSeconds).ToLocalTime();
        }

        public static DateTime ToLocalMs(long unixMs)
        {
            if (unixMs <= 0) return DateTime.MinValue;
            return Epoch.AddMilliseconds(unixMs).ToLocalTime();
        }

        public static long Now()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalSeconds;
        }
    }

    public enum LiveState
    {
        Idle = 0,
        Probing = 1,
        Live = 2,
        Gap = 3
    }

    [Flags]
    public enum SampleFlags
    {
        None = 0,
        SessionRunning = 1,
        InGap = 2,
        Wired = 4,
        Candidate = 8,
        Degraded = 16,
        Manual = 32,
        WifiValid = 64
    }

    /// <summary>一次 5 秒采样的全部数值。落盘为 60 字节定长记录。</summary>
    public class Sample
    {
        public long Ts;
        public SampleFlags Flags;
        public int UpBps;
        public int DownBps;
        public int StreamUpBps;
        public int GwRttMs = -1;
        public int GwJitMs;
        public int GwLossPm = -1;
        public int PubRttMs = -1;
        public int PubLossPm = -1;
        public int SrvRttMs = -1;
        public int SrvJitMs;
        public int SrvLossPm = -1;
        // 接入网：网关之后第一跳（推流路径上 TTL 限制探测到的汇聚设备）
        public int HopRttMs = -1;
        public int HopJitMs;
        public int HopLossPm = -1;
        public string HopIp = "";
        public int HopTtl;
        public int DnsMs = -1;
        public int RetransPm = -1;
        public int CwndKb = -1;
        public int RtoCount = -1;
        public int SynRetrans = -1;
        public int TcpConn = -1;
        public int NewConnPer10 = -1;
        public int CpuTotalPm;
        public int CpuStreamPm = -1;
        public int GpuEncPm = -1;
        public int Gpu3dPm = -1;
        public int MemPm;
        public int DiskQPm10 = -1;
        public int WifiRssi = -1000;   // -1000 表示无效
        public int WifiTx10 = -1;
        public int LinkUtilPm = -1;
        public int OtherUpPm = -1;
        public int Quality = -1;

        public DateTime Time { get { return Ux.ToLocal(Ts); } }
    }

    public class SessionInfo
    {
        public string Id;
        public long StartTs;
        public long EndTs;
        public string EndReason;
        public int Score = -1;
        public string TopCause = "";
        public string StreamProc = "";
        public string ServerIp = "";
        public string LocalIp = "";
        public string AdapterName = "";
        public bool Manual;
        public int SampleCount;
        public int GapCount;
        public long GapTotalMs;
        public long PeakUpBps;
        public long AvgUpBps;
        public string ReportPath = "";
        public string Note = "";

        public long DurationMs { get { return EndTs > StartTs ? (EndTs - StartTs) * 1000L : 0; } }

        public string DurationText
        {
            get
            {
                long s = DurationMs / 1000L;
                if (s <= 0) return "进行中";
                return string.Format("{0:00}:{1:00}:{2:00}", s / 3600, s % 3600 / 60, s % 60);
            }
        }

        public string StartText
        {
            get { return Ux.ToLocal(StartTs).ToString("yyyy-MM-dd HH:mm:ss"); }
        }
    }

    public class EventRecord
    {
        public long Ts;
        public string SessionId = "";
        public string Code = "";
        public int Severity = 1;   // 0 信息 1 注意 2 严重
        public string Title = "";
        public string Detail = "";
        public string Evidence = "";

        public string TimeText { get { return Ux.ToLocal(Ts).ToString("HH:mm:ss"); } }
        public string SeverityText
        {
            get
            {
                if (Severity >= 2) return "严重";
                if (Severity == 1) return "注意";
                return "信息";
            }
        }
    }

    /// <summary>给 UI 用的实时快照。</summary>
    public class LiveSnapshot
    {
        public long Ts;
        public LiveState State = LiveState.Idle;
        public string SessionId = "";
        public long SessionStartTs;
        public int UpBps, DownBps, StreamUpBps;
        public int GwRtt = -1, GwLossPm = -1, GwJit, PubRtt = -1, PubLossPm = -1, SrvRtt = -1, SrvLossPm = -1, SrvJit;
        // 接入网（网关之后第一跳）
        public int HopRtt = -1, HopLossPm = -1, HopJit, HopTtl;
        public string HopIp = "";
        public string HopTarget = "";
        public bool HopFound;
        public int DnsMs = -1, RetransPm = -1, TcpConn = -1, SynSent, NewConnPer10 = -1;
        public int CpuPm, MemPm, DiskQPm10 = -1, GpuEncPm = -1;
        public int WifiQuality = -1, WifiTxKbps = -1, WifiRssi = -1000;
        public int LinkUtilPm = -1;
        public string AdapterName = "", LocalIp = "", Gateway = "", Dns = "";
        public bool Wired;
        public string StreamProc = "";
        public int StreamPid;
        public string ServerIp = "";
        public int ServerPort;
        public string OtherUpProc = "";
        public int OtherUpPm = -1;
        public int Quality = -1;
        public bool Degraded;
        public bool NetworkUp = true;
        public string DegradedReason = "";
        public int GapCount;
        public long GapTotalMs;
        public string EndReason = "";
    }

    /// <summary>INI 风格配置，便于客户现场手改。</summary>
    public class Config
    {
        // 采样节奏
        public int SampleSeconds = 5;
        public int PingGwSeconds = 5;
        public int PingPubSeconds = 5;
        public int PingSrvSeconds = 2;
        public int TcpSeconds = 2;
        public int WlanSeconds = 10;
        public int DnsSeconds = 30;
        public int PerfSeconds = 1;

        // 接入网第一跳（网关之后的汇聚设备）探测
        public int HopTtl = 2;          // 2 = 网关之后第一跳；1 = 网关本身
        public int HopSeconds = 5;      // 探测周期
        public int HopMaxTtl = 8;       // 自动定位时最多扫描到第几跳

        // 直播识别与会话
        public int CandidateKbps = 200;
        public int ProbeSeconds = 10;
        public int GapSeconds = 15;
        public int EndSeconds = 300;

        // 探测目标
        public string PublicTargets = "223.5.5.5,119.29.29.29";
        public string DnsProbeHost = "www.baidu.com";
        public string StreamProcessKeywords = "obs,welive,wechat,wxchannel,live,stream,直播,obs64,视频号,抖音,快手,bili,kuaishou,douyin";
        public string StreamPorts = "1935,1936,443,80,8000,9000";

        // 存储
        public int RawRetentionDays = 30;
        public int ReportRetentionDays = 180;

        public bool AutoStart = false;
        public string Note = "";

        private static string _root;

        public static string DataRoot
        {
            get
            {
                if (_root != null) return _root;
                string exeDir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                string candidate = Path.Combine(exeDir, "data");
                try
                {
                    if (!Directory.Exists(candidate)) Directory.CreateDirectory(candidate);
                    string probe = Path.Combine(candidate, ".w");
                    File.WriteAllText(probe, "1");
                    File.Delete(probe);
                    _root = candidate;
                }
                catch
                {
                    _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveDiag");
                    Directory.CreateDirectory(_root);
                }
                return _root;
            }
        }

        public static string ConfigPath { get { return Path.Combine(DataRoot, "config.ini"); } }
        public static string SampleDir { get { return Path.Combine(DataRoot, "samples"); } }
        public static string EventDir { get { return Path.Combine(DataRoot, "events"); } }
        public static string ReportDir { get { return Path.Combine(DataRoot, "reports"); } }
        public static string LogDir { get { return Path.Combine(DataRoot, "logs"); } }
        /// <summary>未识别到推流进程时落盘的连接明细，用于事后定位。</summary>
        public static string DiagDir { get { return Path.Combine(DataRoot, "diag"); } }
        public static string SessionIndexPath { get { return Path.Combine(DataRoot, "sessions.tsv"); } }
        public static string LearningPath { get { return Path.Combine(DataRoot, "learning.tsv"); } }

        public static void EnsureDirs()
        {
            Directory.CreateDirectory(SampleDir);
            Directory.CreateDirectory(EventDir);
            Directory.CreateDirectory(ReportDir);
            Directory.CreateDirectory(LogDir);
            Directory.CreateDirectory(DiagDir);
        }

        public static Config Load()
        {
            Config c = new Config();
            try
            {
                EnsureDirs();
                if (!File.Exists(ConfigPath)) { c.Save(); return c; }
                foreach (string raw in File.ReadAllLines(ConfigPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    c.Set(k, v);
                }
            }
            catch { }
            return c;
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# 直播诊断 配置文件（改动后重启程序生效）");
            sb.AppendLine("# 时间单位均为秒；KBPS 为千比特/秒");
            sb.AppendLine();
            sb.AppendLine("[采样节奏]");
            sb.AppendLine("SampleSeconds=" + SampleSeconds);
            sb.AppendLine("PingGwSeconds=" + PingGwSeconds);
            sb.AppendLine("PingPubSeconds=" + PingPubSeconds);
            sb.AppendLine("PingSrvSeconds=" + PingSrvSeconds);
            sb.AppendLine("TcpSeconds=" + TcpSeconds);
            sb.AppendLine("WlanSeconds=" + WlanSeconds);
            sb.AppendLine("DnsSeconds=" + DnsSeconds);
            sb.AppendLine();
            sb.AppendLine("[直播识别]");
            sb.AppendLine("CandidateKbps=" + CandidateKbps);
            sb.AppendLine("ProbeSeconds=" + ProbeSeconds);
            sb.AppendLine("GapSeconds=" + GapSeconds);
            sb.AppendLine("EndSeconds=" + EndSeconds);
            sb.AppendLine("StreamProcessKeywords=" + StreamProcessKeywords);
            sb.AppendLine("StreamPorts=" + StreamPorts);
            sb.AppendLine();
            sb.AppendLine("[探测目标]");
            sb.AppendLine("PublicTargets=" + PublicTargets);
            sb.AppendLine("DnsProbeHost=" + DnsProbeHost);
            sb.AppendLine();
            sb.AppendLine("[接入网探测]");
            sb.AppendLine("# HopTtl=1 探测网关本身；2 探测网关之后的第一跳（接入网出口，默认）");
            sb.AppendLine("HopTtl=" + HopTtl);
            sb.AppendLine("HopSeconds=" + HopSeconds);
            sb.AppendLine("HopMaxTtl=" + HopMaxTtl);
            sb.AppendLine();
            sb.AppendLine("[存储]");
            sb.AppendLine("RawRetentionDays=" + RawRetentionDays);
            sb.AppendLine("ReportRetentionDays=" + ReportRetentionDays);
            sb.AppendLine("AutoStart=" + (AutoStart ? "1" : "0"));
            sb.AppendLine("Note=" + Note);
            File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
        }

        private void Set(string k, string v)
        {
            int n;
            switch (k.ToLowerInvariant())
            {
                case "sampleseconds": if (int.TryParse(v, out n) && n >= 1 && n <= 60) SampleSeconds = n; break;
                case "pinggwseconds": if (int.TryParse(v, out n) && n >= 1) PingGwSeconds = n; break;
                case "pingpubseconds": if (int.TryParse(v, out n) && n >= 1) PingPubSeconds = n; break;
                case "pingsrvseconds": if (int.TryParse(v, out n) && n >= 1) PingSrvSeconds = n; break;
                case "tcpseconds": if (int.TryParse(v, out n) && n >= 1) TcpSeconds = n; break;
                case "wlanseconds": if (int.TryParse(v, out n) && n >= 1) WlanSeconds = n; break;
                case "dnsseconds": if (int.TryParse(v, out n) && n >= 1) DnsSeconds = n; break;
                case "candidatekbps": if (int.TryParse(v, out n) && n >= 20) CandidateKbps = n; break;
                case "probeseconds": if (int.TryParse(v, out n) && n >= 3) ProbeSeconds = n; break;
                case "gapseconds": if (int.TryParse(v, out n) && n >= 3) GapSeconds = n; break;
                case "endseconds": if (int.TryParse(v, out n) && n >= 30) EndSeconds = n; break;
                case "streamprocesskeywords": if (v.Length > 0) StreamProcessKeywords = v; break;
                case "streamports": if (v.Length > 0) StreamPorts = v; break;
                case "publictargets": if (v.Length > 0) PublicTargets = v; break;
                case "dnsprobehost": if (v.Length > 0) DnsProbeHost = v; break;
                case "hopttl": if (int.TryParse(v, out n) && n >= 1 && n <= 20) HopTtl = n; break;
                case "hopseconds": if (int.TryParse(v, out n) && n >= 1) HopSeconds = n; break;
                case "hopmaxttl": if (int.TryParse(v, out n) && n >= 2 && n <= 30) HopMaxTtl = n; break;
                case "rawretentiondays": if (int.TryParse(v, out n) && n >= 1) RawRetentionDays = n; break;
                case "reportretentiondays": if (int.TryParse(v, out n) && n >= 1) ReportRetentionDays = n; break;
                case "autostart": AutoStart = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "note": Note = v; break;
            }
        }

        public int[] ParseStreamPorts()
        {
            List<int> list = new List<int>();
            foreach (string s in StreamPorts.Split(','))
            {
                int p;
                if (int.TryParse(s.Trim(), out p) && p > 0) list.Add(p);
            }
            return list.ToArray();
        }

        public string[] ParseKeywords()
        {
            List<string> list = new List<string>();
            foreach (string s in StreamProcessKeywords.Split(','))
            {
                string t = s.Trim().ToLowerInvariant();
                if (t.Length > 0) list.Add(t);
            }
            return list.ToArray();
        }

        public string[] ParseTargets()
        {
            List<string> list = new List<string>();
            foreach (string s in PublicTargets.Split(','))
            {
                string t = s.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list.ToArray();
        }
    }

    public static class Fmt
    {
        public static string Bps(int bps)
        {
            if (bps < 0) return "-";
            double v = bps;
            if (v < 1000) return v.ToString("0") + " bps";
            if (v < 1000 * 1000) return (v / 1000).ToString("0.#") + " Kbps";
            if (v < 1000d * 1000 * 1000) return (v / 1000d / 1000).ToString("0.##") + " Mbps";
            return (v / 1000d / 1000 / 1000).ToString("0.##") + " Gbps";
        }

        public static string Pm(int pm)
        {
            if (pm < 0) return "-";
            return (pm / 10.0).ToString("0.0") + "%";
        }

        public static string Ms(int ms)
        {
            if (ms < 0) return "-";
            return ms + " ms";
        }

        public static string Pct(int pm)
        {
            if (pm < 0) return "-";
            return (pm / 10.0).ToString("0") + "%";
        }

        public static string Dur(long seconds)
        {
            if (seconds < 0) seconds = 0;
            return string.Format("{0:00}:{1:00}:{2:00}", seconds / 3600, seconds % 3600 / 60, seconds % 60);
        }

        public static string ShortDur(long ms)
        {
            if (ms < 1000) return ms + "ms";
            if (ms < 60000) return (ms / 1000.0).ToString("0.#") + "s";
            return (ms / 60000.0).ToString("0.#") + "min";
        }
    }
}
