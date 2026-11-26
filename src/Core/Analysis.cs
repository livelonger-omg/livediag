using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LiveDiag
{
    public class Finding
    {
        public string Code = "";
        public string Title = "";
        public string Detail = "";
        public string Advice = "";
        public string Evidence = "";
        public int Confidence;
        public int Severity = 1;
        public long FromTs;
        public long ToTs;
    }

    public class Baseline
    {
        public int GwRtt;
        public int SrvRtt;
        public int HopRtt;      // 接入网第一跳（网关之后）基线 RTT
        public int TargetUpBps;
        public bool Degraded;
    }

    /// <summary>规则引擎：把指标窗口翻译成"卡顿原因"，每条结论都附时间区间与数值证据。</summary>
    public static class Analyzer
    {
        /// <summary>单次采样质量分 0~100。</summary>
        public static int Score(Sample s, Baseline b)
        {
            int score = 100;

            if ((s.Flags & SampleFlags.InGap) != 0) score -= 45;

            if (s.SrvLossPm >= 0)
            {
                if (s.SrvLossPm >= 20) score -= 20;
                else if (s.SrvLossPm >= 5) score -= 10;
            }
            if (s.GwLossPm >= 0)
            {
                if (s.GwLossPm >= 20) score -= 15;
                else if (s.GwLossPm >= 5) score -= 8;
            }
            if (s.PubLossPm >= 0)
            {
                if (s.PubLossPm >= 50) score -= 8;
                else if (s.PubLossPm >= 10) score -= 4;
            }

            if (s.SrvRttMs >= 400) score -= 15;
            else if (s.SrvRttMs >= 200) score -= 8;
            else if (b.SrvRtt > 0 && s.SrvRttMs > 0 && s.SrvRttMs > b.SrvRtt * 3 && s.SrvRttMs > 80) score -= 5;

            if (s.GwRttMs >= 200) score -= 10;
            else if (b.GwRtt > 0 && s.GwRttMs > 0 && s.GwRttMs > Math.Max(30, b.GwRtt * 4)) score -= 6;

            // 接入网（网关之后第一跳）：这一段出问题会同时拉高时延与丢包，但本地网关完全正常
            if (s.HopLossPm >= 0)
            {
                if (s.HopLossPm >= 20) score -= 12;
                else if (s.HopLossPm >= 10) score -= 6;
                else if (s.HopLossPm >= 5) score -= 3;
            }
            if (s.HopRttMs > 0 && b.HopRtt > 0 && s.HopRttMs > Math.Max(40, b.HopRtt * 3)) score -= 5;

            if (s.SrvJitMs >= 100) score -= 6;
            else if (s.SrvJitMs >= 40) score -= 3;

            if (s.RetransPm >= 20) score -= 12;
            else if (s.RetransPm >= 8) score -= 6;

            if (b.TargetUpBps > 0 && s.StreamUpBps > 0)
            {
                double r = (double)s.StreamUpBps / b.TargetUpBps;
                if (r < 0.4) score -= 18;
                else if (r < 0.7) score -= 10;
                else if (r < 0.85) score -= 4;
            }

            if (s.CpuTotalPm >= 950) score -= 12;
            else if (s.CpuTotalPm >= 850) score -= 6;

            if (s.GpuEncPm >= 950) score -= 8;

            if (s.DiskQPm10 >= 30) score -= 5;

            if (s.MemPm >= 950) score -= 6;

            if (s.WifiRssi > -1000)
            {
                if (s.WifiRssi <= -78) score -= 12;
                else if (s.WifiRssi <= -70) score -= 7;
            }

            if (score < 0) score = 0;
            if (score > 100) score = 100;
            return score;
        }

        public static Baseline BuildBaseline(List<Sample> samples)
        {
            Baseline b = new Baseline();
            List<int> gw = new List<int>();
            List<int> srv = new List<int>();
            List<int> hop = new List<int>();
            List<int> up = new List<int>();
            foreach (Sample s in samples)
            {
                if (s.GwRttMs > 0) gw.Add(s.GwRttMs);
                if (s.SrvRttMs > 0) srv.Add(s.SrvRttMs);
                if (s.HopRttMs > 0) hop.Add(s.HopRttMs);
                if (s.StreamUpBps > 0) up.Add(s.StreamUpBps);
            }
            b.GwRtt = Percentile(gw, 0.5);
            b.SrvRtt = Percentile(srv, 0.5);
            b.HopRtt = Percentile(hop, 0.5);
            b.TargetUpBps = Percentile(up, 0.9);
            return b;
        }

        public static int Percentile(List<int> values, double p)
        {
            if (values == null || values.Count == 0) return 0;
            List<int> copy = new List<int>(values);
            copy.Sort();
            int idx = (int)Math.Round((copy.Count - 1) * p);
            if (idx < 0) idx = 0;
            if (idx >= copy.Count) idx = copy.Count - 1;
            return copy[idx];
        }

        /// <summary>对整个窗口做一次归因。</summary>
        public static List<Finding> Evaluate(List<Sample> window, Baseline baseLine, bool degraded)
        {
            List<Finding> result = new List<Finding>();
            if (window == null || window.Count < 3) return result;

            int n = window.Count;
            long from = window[0].Ts;
            long to = window[n - 1].Ts;

            int gwBad = 0, gwLossBad = 0, srvBad = 0, srvLossBad = 0, pubBad = 0, pubLossBad = 0;
            int wifiBad = 0, cpuBad = 0, gpuBad = 0, diskBad = 0, memBad = 0, gap = 0, dnsBad = 0;
            int upLow = 0, upLowSevere = 0, otherUp = 0, rtoBad = 0;
            int gwValid = 0, srvValid = 0, pubValid = 0;
            int hopValid = 0, hopLossBad = 0, hopRttBad = 0, hopJitBad = 0;

            int maxGw = 0, maxSrv = 0, maxSrvJit = 0, maxLossSrv = 0, maxLossGw = 0, maxCpu = 0, maxGpu = 0;
            int minRssi = 0, maxDns = 0, minUp = int.MaxValue, maxUp = 0;
            int maxHop = 0, maxHopJit = 0, maxLossHop = 0;
            string hopIp = "";

            foreach (Sample s in window)
            {
                if (s.GwRttMs > 0)
                {
                    gwValid++;
                    if (s.GwRttMs > maxGw) maxGw = s.GwRttMs;
                    if (s.GwRttMs > Math.Max(50, baseLine.GwRtt * 4)) gwBad++;
                }
                if (s.GwLossPm >= 0)
                {
                    if (s.GwLossPm > maxLossGw) maxLossGw = s.GwLossPm;
                    if (s.GwLossPm >= 10) gwLossBad++;
                }
                if (s.SrvRttMs > 0)
                {
                    srvValid++;
                    if (s.SrvRttMs > maxSrv) maxSrv = s.SrvRttMs;
                    if (s.SrvJitMs > maxSrvJit) maxSrvJit = s.SrvJitMs;
                    if (s.SrvRttMs > Math.Max(150, baseLine.SrvRtt * 3)) srvBad++;
                }
                if (s.SrvLossPm >= 0)
                {
                    if (s.SrvLossPm > maxLossSrv) maxLossSrv = s.SrvLossPm;
                    if (s.SrvLossPm >= 10) srvLossBad++;
                }
                if (s.PubRttMs > 0)
                {
                    pubValid++;
                    if (s.PubRttMs > 120) pubBad++;
                }
                if (s.PubLossPm >= 0 && s.PubLossPm >= 20) pubLossBad++;

                // 接入网第一跳
                if (s.HopIp.Length > 0) hopIp = s.HopIp;
                if (s.HopRttMs > 0)
                {
                    hopValid++;
                    if (s.HopRttMs > maxHop) maxHop = s.HopRttMs;
                    if (s.HopJitMs > maxHopJit) maxHopJit = s.HopJitMs;
                    if (s.HopRttMs > Math.Max(40, baseLine.HopRtt * 3)) hopRttBad++;
                    if (s.HopJitMs >= 40) hopJitBad++;
                }
                if (s.HopLossPm >= 0)
                {
                    if (s.HopLossPm > maxLossHop) maxLossHop = s.HopLossPm;
                    if (s.HopLossPm >= 10) hopLossBad++;
                }

                if (s.WifiRssi > -1000)
                {
                    if (s.WifiRssi < minRssi || minRssi == 0) minRssi = s.WifiRssi;
                    if (s.WifiRssi <= -70) wifiBad++;
                }
                if (s.CpuTotalPm > maxCpu) maxCpu = s.CpuTotalPm;
                if (s.CpuTotalPm >= 850) cpuBad++;
                if (s.GpuEncPm >= 0) { if (s.GpuEncPm > maxGpu) maxGpu = s.GpuEncPm; if (s.GpuEncPm >= 900) gpuBad++; }
                if (s.DiskQPm10 >= 20) diskBad++;
                if (s.MemPm >= 920) memBad++;
                if ((s.Flags & SampleFlags.InGap) != 0) gap++;
                if (s.DnsMs > maxDns) maxDns = s.DnsMs;
                if (s.DnsMs >= 200) dnsBad++;
                if (s.RtoCount > 0) rtoBad++;

                if (s.StreamUpBps > 0)
                {
                    if (s.StreamUpBps < minUp) minUp = s.StreamUpBps;
                    if (s.StreamUpBps > maxUp) maxUp = s.StreamUpBps;
                    if (baseLine.TargetUpBps > 0)
                    {
                        double r = (double)s.StreamUpBps / baseLine.TargetUpBps;
                        if (r < 0.7) upLow++;
                        if (r < 0.4) upLowSevere++;
                    }
                }
                if (s.OtherUpPm >= 300) otherUp++;
            }

            if (minUp == int.MaxValue) minUp = 0;

            // ---- 断流 ----
            if (gap > 0)
            {
                Finding f = New("GAP", "推流中断", 2, from, to,
                    string.Format("本时段有 {0} 次采样处于断流状态（共 {1} 条），推流上行几乎为 0。", gap, n),
                    "检查直播软件是否报错、网卡是否掉线；若自动恢复，多为网络抖动或平台重连。");
                f.Confidence = Ratio(gap, n);
                f.Evidence = string.Format("断流占比 {0:0}%", gap * 100.0 / n);
                result.Add(f);
            }

            // ---- 推流节点/平台侧 ----
            if (srvValid >= 2 && (srvBad >= n * 0.2 || srvLossBad >= n * 0.15))
            {
                bool pubOk = pubBad <= n * 0.2 && pubLossBad <= n * 0.1;
                Finding f = New("SRV", pubOk ? "推流节点/平台侧异常" : "公网链路质量差", pubOk ? 2 : 1, from, to,
                    string.Format("推流服务器 {0} 的 RTT 最高 {1} ms（基线 {2} ms）、丢包最高 {3}、抖动最高 {4} ms。",
                        "server", maxSrv, baseLine.SrvRtt, Fmt.Pm(maxLossSrv), maxSrvJit),
                    pubOk
                        ? "公网其他目标正常而推流节点异常，问题更可能在平台侧或该节点路由：换节点/联系平台，并保留本报告。"
                        : "公网多个目标同时劣化，检查上行链路与运营商时段性拥塞。");
                f.Confidence = Math.Max(Ratio(srvBad, n), Ratio(srvLossBad, n));
                f.Evidence = string.Format("推流节点 RTT 峰值 {0}ms，丢包峰值 {1}；同时段公网参考点异常占比 {2:0}%",
                    maxSrv, Fmt.Pm(maxLossSrv), pubBad * 100.0 / n);
                result.Add(f);
            }

            // ---- 本地无线/局域网 ----
            if (gwValid >= 2 && (gwBad >= n * 0.2 || gwLossBad >= n * 0.15 || wifiBad >= n * 0.3))
            {
                bool wireless = minRssi != 0;
                Finding f = New("LAN", wireless ? "本地无线链路质量差" : "本地网关/路由器异常", 2, from, to,
                    string.Format("网关 RTT 峰值 {0} ms（基线 {1} ms），网关丢包峰值 {2}{3}。",
                        maxGw, baseLine.GwRtt, Fmt.Pm(maxLossGw),
                        wireless ? string.Format("，无线信号最弱 {0} dBm", minRssi) : ""),
                    wireless
                        ? "优先改有线；无法改线时切 5GHz、靠近路由器、避开 2.4G 拥塞与中继/电力猫。"
                        : "重启路由器、检查是否有设备占满上行、检查路由器 NAT 会话数与 QoS 设置。");
                f.Confidence = Math.Max(Math.Max(Ratio(gwBad, n), Ratio(gwLossBad, n)), Ratio(wifiBad, n));
                f.Evidence = string.Format("网关丢包峰值 {0}，网关 RTT 峰值 {1}ms，基线 {2}ms", Fmt.Pm(maxLossGw), maxGw, baseLine.GwRtt);
                result.Add(f);
            }

            // ---- 接入网：网关之后第一跳 ----
            // 这一段（BRAS/汇聚交换机）属于接入网，故障概率高于核心网；
            // 典型特征是"本地网关正常，但出了网关就开始丢包/涨时延"。
            bool gwHealthy = gwLossBad <= n * 0.1 && gwBad <= n * 0.2;
            if (hopValid >= 4 && (hopLossBad >= Math.Max(2, n * 0.1) || hopRttBad >= n * 0.2 || hopJitBad >= n * 0.2))
            {
                bool lossProblem = hopLossBad >= Math.Max(2, n * 0.1);
                string title = lossProblem ? "接入网（网关后第一跳）丢包" : "接入网（网关后第一跳）时延抖动异常";
                Finding f = New("ACC", title, gwHealthy ? 2 : 1, from, to,
                    string.Format("接入网第一跳 {0}：丢包峰值 {1}、RTT 最高 {2} ms（基线 {3} ms）、抖动最高 {4} ms；{5}",
                        hopIp.Length > 0 ? hopIp : "（地址未知）", Fmt.Pm(maxLossHop), maxHop, baseLine.HopRtt, maxHopJit,
                        gwHealthy
                            ? string.Format("同期本地网关正常（丢包峰值 {0}、RTT 峰值 {1} ms），问题出在网关之外。", Fmt.Pm(maxLossGw), maxGw)
                            : string.Format("同期本地网关也不正常（丢包峰值 {0}、RTT 峰值 {1} ms），需先排查路由器与 WiFi。", Fmt.Pm(maxLossGw), maxGw)),
                    gwHealthy
                        ? "依次检查：光猫与路由器之间的网线/水晶头、光猫温度与重启、同宽带下是否有设备占满上行（网盘/监控/下载）、光衰与线路质量；仍旧异常请向运营商报障，说明“本地网关正常、出网关第一跳丢包”，并附本报告。"
                        : "先按本地链路排查（改有线、换 5GHz、重启路由器），排除家庭侧设备后再向运营商报障。");
                f.Confidence = Math.Max(Math.Max(Ratio(hopLossBad, n), Ratio(hopRttBad, n)), Ratio(hopJitBad, n));
                f.Evidence = string.Format("接入跳 {0} 丢包峰值 {1}，RTT {2}ms（基线 {3}ms），网关丢包峰值 {4}",
                    hopIp.Length > 0 ? hopIp : "-", Fmt.Pm(maxLossHop), maxHop, baseLine.HopRtt, Fmt.Pm(maxLossGw));
                result.Add(f);
            }

            // ---- 上行带宽饱和（缓冲膨胀）----
            if (gwValid >= 2 && baseLine.GwRtt > 0 && maxGw >= Math.Max(40, baseLine.GwRtt * 5) && maxUp > 0)
            {
                Finding f = New("BW_SAT", "上行带宽饱和（排队延迟上升）", 2, from, to,
                    string.Format("网关 RTT 从基线 {0} ms 涨到 {1} ms，同时推流上行约 {2}。这是典型的上行被占满后的排队延迟。",
                        baseLine.GwRtt, maxGw, Fmt.Bps(maxUp)),
                    "降低推流码率或分辨率；关闭同网络下的下载/网盘/监控回传；必要时升级上行带宽。");
                f.Confidence = 70;
                f.Evidence = string.Format("网关 RTT {0}ms→{1}ms，推流上行峰值 {2}", baseLine.GwRtt, maxGw, Fmt.Bps(maxUp));
                result.Add(f);
            }

            // ---- 推流码率不稳 ----
            if (baseLine.TargetUpBps > 0 && (upLow >= n * 0.2 || upLowSevere >= n * 0.1))
            {
                Finding f = New("UP_UNSTABLE", "推流码率明显低于常态", 2, from, to,
                    string.Format("推流上行从常态 {0} 掉到最低 {1}（仅为 {2:0}%），说明编码端在降码率或发送受阻。",
                        Fmt.Bps(baseLine.TargetUpBps), Fmt.Bps(minUp), minUp * 100.0 / baseLine.TargetUpBps),
                    "结合同段网络指标判断：网络正常则查编码器/软件限速；网络异常则查上行带宽与丢包。");
                f.Confidence = Math.Max(Ratio(upLow, n), Ratio(upLowSevere, n));
                f.Evidence = string.Format("常态 {0} → 最低 {1}", Fmt.Bps(baseLine.TargetUpBps), Fmt.Bps(minUp));
                result.Add(f);
            }

            // ---- 本机资源 ----
            if (cpuBad >= n * 0.3 || gpuBad >= n * 0.3 || diskBad >= n * 0.3 || memBad >= n * 0.3)
            {
                StringBuilder sb = new StringBuilder();
                StringBuilder adv = new StringBuilder();
                if (cpuBad >= n * 0.3) { sb.Append("CPU 峰值 " + maxCpu / 10 + "%；"); adv.Append("降低分辨率/帧率或改用硬件编码；"); }
                if (gpuBad >= n * 0.3) { sb.Append("GPU 编码峰值 " + maxGpu / 10 + "%；"); adv.Append("检查显卡编码器占用，避免同时录制/多路推流；"); }
                if (diskBad >= n * 0.3) { sb.Append("磁盘队列长期偏高；"); adv.Append("不要把录制写到系统盘；"); }
                if (memBad >= n * 0.3) { sb.Append("内存占用过高；"); adv.Append("关闭多余浏览器标签与后台程序；"); }
                Finding f = New("LOCAL_PERF", "本机性能瓶颈", 2, from, to,
                    sb.ToString(), adv.ToString());
                f.Confidence = Math.Max(Math.Max(Ratio(cpuBad, n), Ratio(gpuBad, n)), Math.Max(Ratio(diskBad, n), Ratio(memBad, n)));
                f.Evidence = string.Format("CPU 峰值 {0}%，GPU编码峰值 {1}，内存峰值见曲线",
                    maxCpu / 10, maxGpu < 0 ? "-" : (maxGpu / 10).ToString() + "%");
                result.Add(f);
            }

            // ---- DNS ----
            if (dnsBad >= n * 0.2 && maxDns > 0)
            {
                Finding f = New("DNS", "DNS 解析异常缓慢", 1, from, to,
                    string.Format("域名解析耗时最高 {0} ms，可能导致连接建立变慢或解析到远端节点。", maxDns),
                    "改用 223.5.5.5 / 119.29.29.29 等公共 DNS，或联系网络管理员；必要时在直播软件中固定推流地址。");
                f.Confidence = Ratio(dnsBad, n);
                f.Evidence = string.Format("解析耗时峰值 {0}ms", maxDns);
                result.Add(f);
            }

            // ---- 其他程序占用上行 ----
            if (otherUp >= n * 0.2)
            {
                Finding f = New("CONTENTION", "其他程序占用上行带宽", 1, from, to,
                    string.Format("整机上行中约 {0:0.#}% 不属于推流进程，存在上行被抢占。", otherUpPct(window)),
                    "关闭网盘同步、系统更新、远程桌面、监控回传等占用上行的程序。");
                f.Confidence = Ratio(otherUp, n);
                f.Evidence = string.Format("非推流上行占比均值 {0:0.#}%", otherUpPct(window));
                result.Add(f);
            }

            // ---- 降级说明（不算故障）----
            if (degraded && result.Count == 0)
            {
                Finding f = New("DEGRADED", "当前权限下部分指标不可用", 0, from, to,
                    "逐连接字节数统计需管理员权限；已用进程连接特征与整机上行估算替代。",
                    "如需精确到每条连接的重传与速率，可用“以管理员身份运行”启动本程序。");
                f.Confidence = 100;
                result.Add(f);
            }

            result.Sort(delegate(Finding a, Finding b) { return b.Confidence.CompareTo(a.Confidence); });
            return result;
        }

        private static double otherUpPct(List<Sample> window)
        {
            double sum = 0;
            int n = 0;
            foreach (Sample s in window)
            {
                if (s.OtherUpPm >= 0) { sum += s.OtherUpPm; n++; }
            }
            return n == 0 ? 0 : sum / n / 10.0;
        }

        private static Finding New(string code, string title, int severity, long from, long to, string detail, string advice)
        {
            Finding f = new Finding();
            f.Code = code;
            f.Title = title;
            f.Severity = severity;
            f.FromTs = from;
            f.ToTs = to;
            f.Detail = detail;
            f.Advice = advice;
            f.Confidence = 50;
            return f;
        }

        private static int Ratio(int bad, int total)
        {
            if (total <= 0) return 0;
            int r = (int)Math.Round(bad * 100.0 / total);
            if (r > 100) r = 100;
            if (r < 1) r = 1;
            return r;
        }

        public static string TimeRange(long fromTs, long toTs)
        {
            DateTime a = Ux.ToLocal(fromTs);
            DateTime b = Ux.ToLocal(toTs);
            return a.ToString("HH:mm:ss") + "–" + b.ToString("HH:mm:ss");
        }
    }
}
