using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using LiveDiag.Interop;
using LiveDiag.UI;

namespace LiveDiag
{
    internal static class Program
    {
        public const string AppName = "直播诊断";
        public const string AppId = "LiveDiag";
        public const string Version = "0.1.0";

        [STAThread]
        private static void Main(string[] args)
        {
            Native.SetProcessDPIAware();

            // 开机自启时（注册表 Run 传入 --autostart）只驻留托盘，不弹主窗口
            bool silent = false;
            foreach (string a in args)
                if (a == "--autostart" || a == "--min" || a == "--silent") silent = true;

            if (args.Length > 0 && args[0] == "--selftest")
            {
                int secs = 8;
                if (args.Length > 1) int.TryParse(args[1], out secs);
                SelfTest(secs);
                return;
            }
            if (args.Length > 0 && args[0] == "--estats")
            {
                EstatsProbe.Run();
                return;
            }
            if (args.Length > 0 && args[0] == "--smoke")
            {
                int secs = 25;
                if (args.Length > 1) int.TryParse(args[1], out secs);
                RunSmoke(secs);
                return;
            }
            if (args.Length > 0 && args[0] == "--fakestream")
            {
                int secs = 20;
                int port = 1935;
                if (args.Length > 1) int.TryParse(args[1], out secs);
                if (args.Length > 2) int.TryParse(args[2], out port);
                RunFakeStream(secs, port);
                return;
            }
            if (args.Length > 0 && args[0] == "--report")
            {
                // 无界面模式下生成某会话报告：--report 会话ID
                if (args.Length > 1) GenerateReport(args[1]);
                return;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "Global\\LiveDiag_SingleInstance_v1", out created))
            {
                if (!created)
                {
                    MessageBox.Show("直播诊断已在运行，请在任务栏右下角的托盘图标中打开。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Engine.WriteLog("UI exception: " + e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Engine.WriteLog("fatal: " + e.ExceptionObject);
                };

                Config cfg = Config.Load();
                AutoStart.EnsureFromConfig(cfg);
                Engine engine = new Engine(cfg);
                MainForm form = new MainForm(engine);
                NotifyIcon tray = BuildTray(form, engine);

                engine.OnSessionStarted += delegate(SessionInfo info)
                {
                    Balloon(tray, "直播记录已开始", (info.StreamProc.Length > 0 ? info.StreamProc : "未识别进程") +
                        (info.ServerIp.Length > 0 ? " → " + info.ServerIp : ""));
                };
                engine.OnSessionEnded += delegate(SessionInfo info)
                {
                    if (form.IsHandleCreated && !form.IsDisposed)
                    {
                        try
                        {
                            form.BeginInvoke(new Action(delegate
                            {
                                Balloon(tray, "直播记录已结束",
                                    "时长 " + info.DurationText + "，质量分 " + (info.Score < 0 ? "-" : info.Score.ToString()) +
                                    (info.TopCause.Length > 0 ? "，首要原因：" + info.TopCause : ""));
                            }));
                        }
                        catch { }
                    }
                };

                engine.Start();
                Application.ApplicationExit += delegate
                {
                    try { tray.Visible = false; tray.Dispose(); } catch { }
                    try { engine.Dispose(); } catch { }
                };

                if (silent)
                {
                    Balloon(tray, "直播诊断已在后台运行", "开机自启已生效，双击托盘图标可打开主界面。");
                    Application.Run(new TrayContext());
                }
                else
                {
                    Application.Run(form);
                }
            }
        }

        /// <summary>静默模式（开机自启）用的消息循环宿主：没有窗口，只有托盘图标。</summary>
        private sealed class TrayContext : ApplicationContext
        {
        }

        private static NotifyIcon BuildTray(MainForm form, Engine engine)
        {
            NotifyIcon tray = new NotifyIcon();
            tray.Icon = AppIcon.Create();
            tray.Text = AppName + " " + Version;
            tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem miShow = new ToolStripMenuItem("显示主窗口");
            miShow.Click += delegate { ShowForm(form); };
            ToolStripMenuItem miToggle = new ToolStripMenuItem("手动开始记录");
            miToggle.Click += delegate
            {
                if (engine.State == LiveState.Live || engine.State == LiveState.Gap) engine.StopManual();
                else engine.StartManual();
            };
            ToolStripMenuItem miSessions = new ToolStripMenuItem("历史会话");
            miSessions.Click += delegate
            {
                ShowForm(form);
                using (SessionsForm f = new SessionsForm(engine)) f.ShowDialog(form);
            };
            ToolStripMenuItem miDir = new ToolStripMenuItem("打开数据目录");
            miDir.Click += delegate { Process.Start(Config.DataRoot); };
            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { Application.Exit(); };

            menu.Items.Add(miShow);
            menu.Items.Add(miToggle);
            menu.Items.Add(miSessions);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miDir);
            menu.Items.Add(miExit);
            menu.Opening += delegate
            {
                bool live = engine.State == LiveState.Live || engine.State == LiveState.Gap;
                miToggle.Text = live ? "结束本次记录" : "手动开始记录";
            };

            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowForm(form); };
            return tray;
        }

        private static void ShowForm(MainForm form)
        {
            try
            {
                if (!form.Visible) form.Show();
                if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
                form.Activate();
                form.BringToFront();
            }
            catch { }
        }

        private static void Balloon(NotifyIcon tray, string title, string text)
        {
            try
            {
                tray.BalloonTipTitle = title;
                tray.BalloonTipText = text;
                tray.BalloonTipIcon = ToolTipIcon.Info;
                tray.ShowBalloonTip(4000);
            }
            catch { }
        }

        private static void GenerateReport(string sessionId)
        {
            try
            {
                Store store = new Store();
                List<SessionInfo> list = store.LoadSessions();
                foreach (SessionInfo s in list)
                {
                    if (s.Id != sessionId) continue;
                    List<Sample> samples = store.LoadSamples(s.StartTs * 1000L, Math.Max(s.EndTs, s.StartTs) * 1000L);
                    Baseline bl = Analyzer.BuildBaseline(samples);
                    List<Finding> findings = Analyzer.Evaluate(samples, bl, false);
                    if (findings.Count > 0) s.TopCause = findings[0].Title;
                    string path = Report.Build(s, samples, findings, store, false);
                    store.UpsertSession(s);
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "report.txt"), path, new UTF8Encoding(true));
                    break;
                }
                store.Dispose();
            }
            catch (Exception ex) { Engine.WriteLog("--report failed: " + ex); }
        }

        // ==================== 自检 ====================
        private static void SelfTest(int seconds)
        {
            StringBuilder sb = new StringBuilder();
            Stopwatch sw = Stopwatch.StartNew();
            Action<string> log = delegate(string s) { sb.AppendLine(s); };

            log("== " + AppName + " 自检 " + Version + " ==");
            log("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            log("数据目录：" + Config.DataRoot + "  可写：" + CanWrite(Config.DataRoot));
            log("");

            NetCollector net = new NetCollector();
            AdapterInfo ad = net.RefreshAdapter();
            log("[网卡] " + ad.Name + " / " + ad.Description);
            log("       IP=" + ad.Ip + "  网关=" + ad.Gateway + "  DNS=" + ad.DnsText +
                "  链路=" + ad.LinkText + "  类型=" + (ad.Wired ? "有线" : "无线") + "  IfIndex=" + ad.IfIndex);

            net.ReadTraffic();
            Thread.Sleep(1000);
            TrafficInfo tr = net.ReadTraffic();
            log("[流量] 上行 " + Fmt.Bps((int)tr.UpBps) + "  下行 " + Fmt.Bps((int)tr.DownBps) +
                "  链路利用率 " + Fmt.Pm(tr.LinkUtilPm) + (tr.Valid ? "" : "  (无效)"));

            List<PingWorker> pings = new List<PingWorker>();
            if (ad.Gateway.Length > 0) pings.Add(new PingWorker(ad.Gateway));
            Config cfg = Config.Load();
            foreach (string t in cfg.ParseTargets()) pings.Add(new PingWorker(t));
            foreach (PingWorker pw in pings) pw.RunAsync();
            Thread.Sleep(1500);
            foreach (PingWorker pw in pings)
            {
                log("[PING] " + pw.Host + " rtt=" + Fmt.Ms(pw.Result.RttMs) + " 抖动=" + pw.Result.JitMs +
                    "ms 丢包=" + Fmt.Pm(pw.Result.LossPm) +
                    " (收 " + pw.Result.Recv + "/" + pw.Result.Sent + ")" +
                    (pw.Result.Error.Length > 0 ? " 错误:" + pw.Result.Error : ""));
            }

            TcpCollector tcp = new TcpCollector();
            tcp.Read();
            Thread.Sleep(1200);
            TcpSnapshot s2 = tcp.Read();
            log("[TCP] 连接 " + s2.Total + "（ESTAB " + s2.Estab + " / SYN_SENT " + s2.SynSent +
                " / TIME_WAIT " + s2.TimeWait + "）  新建 " + s2.NewPerSec.ToString("0.0") + "/s");
            log("[ESTATS] 精确模式=" + tcp.EstatsAvailable + "  跟踪连接=" + s2.Tracked +
                "  失败=" + s2.EstatsFail + "  错误码=" + tcp.EstatsErrorCode);

            // 接入网出口（网关之后第一跳）：逐跳探测 + 时延/丢包质检
            try
            {
                string[] targets = cfg.ParseTargets();
                string hopTarget = targets.Length > 0 ? targets[0] : "";
                if (hopTarget.Length > 0)
                {
                    using (HopProbe probe = new HopProbe())
                    {
                        HopPath path = probe.Trace(hopTarget, Math.Max(cfg.HopMaxTtl, cfg.HopTtl));
                        log("[接入跳] 探测目标 " + hopTarget + "  路径 " + path.PathText() +
                            (path.Error.Length > 0 ? "  错误:" + path.Error : ""));
                    }
                    using (HopPing hping = new HopPing(hopTarget, cfg.HopTtl))
                    {
                        hping.Run();
                        log("[接入跳] 第" + cfg.HopTtl + "跳 " + (hping.HopIp.Length > 0 ? hping.HopIp : "(无响应)") +
                            "  RTT " + Fmt.Ms(hping.Result.RttMs) + "  抖动 " + hping.Result.JitMs + "ms" +
                            "  丢包 " + Fmt.Pm(hping.Result.LossPm) +
                            " (收 " + hping.Result.Recv + "/" + hping.Result.Sent + ")");
                    }
                }
            }
            catch (Exception ex) { log("[接入跳] 探测失败：" + ex.Message); }

            ProcessCollector proc = new ProcessCollector();
            proc.Read();
            Thread.Sleep(1500);
            List<ProcStat> stats = proc.Read();
            stats.Sort(delegate(ProcStat a, ProcStat b) { return b.WriteBps.CompareTo(a.WriteBps); });
            for (int i = 0; i < stats.Count && i < 5; i++)
            {
                if (stats[i].WriteBps < 1000) break;
                log("[写出进程] " + stats[i].Name + "(" + stats[i].Pid + ") " + Fmt.Bps((int)stats[i].WriteBps) +
                    "  CPU " + Fmt.Pct((int)stats[i].CpuPm));
            }

            PerfCollector perf = new PerfCollector();
            PerfInfo pi = perf.Read(0);
            for (int i = 0; i < 4; i++) { Thread.Sleep(1200); pi = perf.Read(0); }
            log("[性能] CPU " + Fmt.Pct(pi.CpuPm) + "  内存 " + Fmt.Pct(pi.MemPm) +
                "  磁盘队列 " + (pi.DiskQPm10 < 0 ? "-" : (pi.DiskQPm10 / 10.0).ToString("0.0")) +
                "  电源 " + (pi.OnAc ? "市电" : "电池"));
            log("[CPU Top] " + string.Join(" | ", perf.TopCpuProcesses(5).ToArray()));
            log("[GPU] 编码引擎 " + (pi.GpuEncPm < 0 ? "-" : (pi.GpuEncPm / 10.0).ToString("0.0") + "%") +
                "  全部引擎 " + (pi.Gpu3dPm < 0 ? "-" : (pi.Gpu3dPm / 10.0).ToString("0.0") + "%") +
                "  diag=" + perf.GpuDiag);

            WlanCollector wlan = new WlanCollector();
            WlanInfo wi = wlan.Read();
            log("[WLAN] " + (wi.Valid
                ? ("SSID=" + wi.Ssid + " 信号=" + wi.Quality + "% 约" + wi.Rssi + "dBm 发=" + (wi.TxKbps / 1000) + "Mbps 收=" + (wi.RxKbps / 1000) + "Mbps " + wi.Phy)
                : ("(未取到) " + wi.Error)));

            log("");
            log("自检耗时 " + sw.ElapsedMilliseconds + " ms");

            string outFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "selftest.txt");
            try { File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(true)); }
            catch { }

            foreach (PingWorker pw in pings) pw.Dispose();
            wlan.Dispose();
        }

        // ==================== 状态机验证：模拟直播流量 ====================
        // 本模式在本进程内建立到 127.0.0.1:port 的持续上行，用于验证
        // 「检测到直播流量 → 开始计时 → 流量中断 → 超过阈值自动结束」整条链路。
        // port 默认 1935；用 443 可复现「抖音直播伴侣走 RTMPS」的识别路径。
        private static void RunFakeStream(int seconds, int port)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                Config cfg = Config.Load();
                cfg.SampleSeconds = 5;
                cfg.ProbeSeconds = 5;
                cfg.GapSeconds = 5;
                cfg.EndSeconds = 15;   // 测试用 15 秒，正式默认 300 秒
                sb.AppendLine("测试配置: sample=" + cfg.SampleSeconds + "s probe=" + cfg.ProbeSeconds +
                    "s gap=" + cfg.GapSeconds + "s end=" + cfg.EndSeconds + "s port=" + port + "s");

                Engine engine = new Engine(cfg);
                engine.TestMode = true;
                engine.Start();
                Thread.Sleep(2000);

                volatile_wrapper vw = new volatile_wrapper();
                Thread t = new Thread(delegate()
                {
                    try
                    {
                        System.Net.Sockets.TcpListener l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
                        l.Start();
                        System.Net.Sockets.TcpClient c = new System.Net.Sockets.TcpClient();
                        c.Connect(new IPEndPoint(IPAddress.Loopback, port));
                        System.Net.Sockets.TcpClient srv = l.AcceptTcpClient();
                        // 接收端必须持续读取，否则 TCP 窗口填满后发送侧会被阻塞
                        Thread reader = new Thread(delegate()
                        {
                            try
                            {
                                System.Net.Sockets.NetworkStream rs = srv.GetStream();
                                byte[] rb = new byte[64 * 1024];
                                while (!vw.Stop)
                                {
                                    if (rs.Read(rb, 0, rb.Length) <= 0) break;
                                }
                            }
                            catch { }
                        });
                        reader.IsBackground = true;
                        reader.Start();
                        System.Net.Sockets.NetworkStream ns = c.GetStream();
                        byte[] buf = new byte[32 * 1024];
                        while (!vw.Stop)
                        {
                            ns.Write(buf, 0, buf.Length);
                            vw.Sent += buf.Length;
                            Thread.Sleep(20);
                        }
                        c.Close();
                        srv.Close();
                        l.Stop();
                    }
                    catch (Exception ex) { Engine.WriteLog("fakestream: " + ex.Message); }
                });
                t.IsBackground = true;
                t.Start();

                Thread.Sleep(4000);
                // 诊断：确认数据真的发出去了、以及各采集器是否看到
                try
                {
                    ProcessCollector pc = new ProcessCollector();
                    pc.Read();
                    Thread.Sleep(1500);
                    List<ProcStat> ps = pc.Read();
                    foreach (ProcStat p in ps)
                    {
                        if (p.Pid != Process.GetCurrentProcess().Id) continue;
                        sb.AppendLine("自检: 已发送 " + (vw.Sent / 1024) + " KB, 本进程写速率 " + Fmt.Bps((int)p.WriteBps));
                    }
                    TcpCollector tc = new TcpCollector();
                    tc.Read();
                    Thread.Sleep(1200);
                    TcpSnapshot ts = tc.Read();
                    int nPort = 0;
                    foreach (ConnInfo ci in ts.Conns) if (ci.RemotePort == port || ci.LocalPort == port) nPort++;
                    sb.AppendLine("自检: 端口" + port + "连接数 " + nPort + "，Conns=" + ts.Conns.Count +
                        "，TCP 总数 " + ts.Total + "，UDP 端点 " + ts.Udp.Count + "，IPv6 连接 " + ts.Conns6.Count);
                    int shown = 0;
                    int me = Process.GetCurrentProcess().Id;
                    foreach (ConnInfo ci in ts.Conns)
                    {
                        if (ci.Pid != me || shown >= 6) continue;
                        sb.AppendLine("  conn pid=" + ci.Pid + " " + ci.LocalIp + ":" + ci.LocalPort + " -> " + ci.RemoteIp + ":" + ci.RemotePort + " state=" + ci.State);
                        shown++;
                    }
                }
                catch (Exception ex) { sb.AppendLine("自检异常: " + ex.Message); }

                sb.AppendLine("--- 阶段1：模拟约 12 Mbps 上行 ---");
                for (int i = 0; i < seconds; i++)
                {
                    Thread.Sleep(1000);
                    LiveSnapshot s = engine.Snap;
                    sb.AppendLine(string.Format("{0,3}s state={1,-7} proc={2,-12} server={3,-14} up={4}",
                        i, s.State, s.StreamProc, s.ServerIp + ":" + s.ServerPort, Fmt.Bps(s.UpBps)));
                }

                vw.Stop = true;
                sb.AppendLine("--- 阶段2：停止上行，观察断流与自动结束 ---");
                for (int i = 0; i < 30; i++)
                {
                    Thread.Sleep(1000);
                    LiveSnapshot s = engine.Snap;
                    sb.AppendLine(string.Format("{0,3}s state={1,-7} gap={2} up={3}",
                        i, s.State, s.GapCount, Fmt.Bps(s.UpBps)));
                    if (i > 8 && s.State == LiveState.Idle) break;
                }
                Thread.Sleep(3000);
                sb.AppendLine();
                if (File.Exists(Config.SessionIndexPath)) sb.AppendLine(File.ReadAllText(Config.SessionIndexPath, Encoding.UTF8));
                engine.Dispose();
            }
            catch (Exception ex) { sb.AppendLine("ERROR: " + ex); }
            try
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "fakestream.txt"), sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        private class volatile_wrapper
        {
            public volatile bool Stop;
            public long Sent;
        }

        // ==================== 端到端冒烟（无界面） ====================
        private static void RunSmoke(int seconds)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                Config cfg = Config.Load();
                sb.AppendLine("data root: " + Config.DataRoot);
                Engine engine = new Engine(cfg);
                engine.Start();
                Thread.Sleep(3000);
                engine.StartManual();
                for (int i = 0; i < seconds; i++)
                {
                    Thread.Sleep(1000);
                    LiveSnapshot s = engine.Snap;
                    if (s == null) continue;
                    sb.AppendLine(string.Format("{0,3}s state={1,-6} up={2,-10} stream={3,-10} gw={4,-6} srv={5,-6} cpu={6,-5} conn={7,-4} q={8}",
                        i, s.State, Fmt.Bps(s.UpBps), Fmt.Bps(s.StreamUpBps), Fmt.Ms(s.GwRtt), Fmt.Ms(s.SrvRtt),
                        Fmt.Pct(s.CpuPm), s.TcpConn, s.Quality));
                }
                engine.StopManual();
                Thread.Sleep(4000);
                sb.AppendLine();
                sb.AppendLine(string.Format("engine tick: avg={0:0.00}ms max={1:0.00}ms", engine.AvgTickMs, engine.MaxTickMs));
                sb.AppendLine("profile: " + engine.TickProfile);
                sb.AppendLine("sessions.tsv:");
                if (File.Exists(Config.SessionIndexPath)) sb.AppendLine(File.ReadAllText(Config.SessionIndexPath, Encoding.UTF8));
                sb.AppendLine("reports: " + (Directory.Exists(Config.ReportDir) ? string.Join(" | ", Directory.GetFiles(Config.ReportDir)) : "(none)"));
                sb.AppendLine("samples: " + (Directory.Exists(Config.SampleDir) ? string.Join(" | ", Directory.GetFiles(Config.SampleDir)) : "(none)"));
                sb.AppendLine("events:  " + (Directory.Exists(Config.EventDir) ? string.Join(" | ", Directory.GetFiles(Config.EventDir)) : "(none)"));
                engine.Dispose();
            }
            catch (Exception ex)
            {
                sb.AppendLine("SMOKE ERROR: " + ex);
            }
            try
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "smoke.txt"), sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                string f = Path.Combine(dir, ".probe");
                File.WriteAllText(f, "1");
                File.Delete(f);
                return true;
            }
            catch { return false; }
        }
    }
}
