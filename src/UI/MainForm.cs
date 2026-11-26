using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LiveDiag.UI
{
    public class MainForm : Form
    {
        private readonly Engine _engine;
        private readonly Dictionary<string, MetricCard> _cards = new Dictionary<string, MetricCard>();
        private Pill _pill;
        private Label _line1;
        private Label _line2;
        private Label _badge;
        private ListView _events;
        private Button _btnSession;
        private Button _btnMark;
        private Button _btnUnmark;
        private Timer _uiTimer;
        private Color _lastAccent = Theme.Accent;

        public MainForm(Engine engine)
        {
            _engine = engine;
            BuildUi();
            _engine.OnEvent += OnEngineEvent;
            _uiTimer = new Timer();
            _uiTimer.Interval = 1500;
            _uiTimer.Tick += delegate { Refresh1(); };
            _uiTimer.Start();
            Refresh1();
        }

        private void BuildUi()
        {
            Text = Program.AppName + " " + Program.Version;
            Width = 1060;
            Height = 840;
            MinimumSize = new Size(920, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            Font = Theme.F(9f, FontStyle.Regular);
            Icon = AppIcon.Create();

            // ---------- 事件列表（Fill，先加） ----------
            Panel eventsPanel = new Panel();
            eventsPanel.Dock = DockStyle.Fill;
            eventsPanel.Padding = new Padding(16, 8, 16, 8);
            eventsPanel.BackColor = Theme.Bg;

            Label evTitle = new Label();
            evTitle.Text = "事件时间线（可溯源）";
            evTitle.Dock = DockStyle.Top;
            evTitle.Height = 24;
            evTitle.ForeColor = Theme.Sub;
            evTitle.Font = Theme.F(9f, FontStyle.Bold);

            _events = new ListView();
            _events.Dock = DockStyle.Fill;
            _events.View = View.Details;
            _events.FullRowSelect = true;
            _events.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _events.BorderStyle = BorderStyle.FixedSingle;
            _events.BackColor = Theme.Card;
            _events.Font = Theme.F(8.5f, FontStyle.Regular);
            _events.Columns.Add("时间", 70);
            _events.Columns.Add("级别", 50);
            _events.Columns.Add("事件", 300);
            _events.Columns.Add("详情", 560);

            eventsPanel.Controls.Add(_events);
            eventsPanel.Controls.Add(evTitle);

            // ---------- 底部按钮 ----------
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 58;
            bottom.BackColor = Theme.Bg;
            bottom.Padding = new Padding(16, 10, 16, 10);

            _btnSession = MakeButton("结束本次记录", 150);
            _btnSession.Click += delegate { ToggleSession(); };

            _btnMark = MakeButton("标记为直播", 110);
            _btnMark.Click += delegate { _engine.MarkCurrentAsLive(true); };

            _btnUnmark = MakeButton("标记为非直播", 120);
            _btnUnmark.Click += delegate { _engine.MarkCurrentAsLive(false); };

            Button btnList = MakeButton("历史会话", 100);
            btnList.Click += delegate { OpenSessions(); };

            Button btnSet = MakeButton("设置", 70);
            btnSet.Click += delegate { new SettingsForm(_engine).ShowDialog(this); };

            Button btnDir = MakeButton("打开数据目录", 120);
            btnDir.Click += delegate { Process.Start(Config.DataRoot); };

            Button btnHelp = MakeButton("使用说明", 90);
            btnHelp.Click += delegate { ShowHelp(); };

            int x = 16;
            foreach (Button b in new Button[] { _btnSession, _btnMark, _btnUnmark, btnList, btnSet, btnDir, btnHelp })
            {
                b.Left = x;
                b.Top = 12;
                bottom.Controls.Add(b);
                x += b.Width + 8;
            }

            // ---------- 指标卡片 ----------
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Top;
            grid.Height = 424;
            grid.Padding = new Padding(12, 6, 12, 6);
            grid.BackColor = Theme.Bg;
            grid.ColumnCount = 4;
            grid.RowCount = 4;
            for (int c = 0; c < 4; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            for (int r = 0; r < 4; r++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

            string[] titles = new string[] {
                "推流上行", "整机上行", "质量分", "网关 RTT",
                "接入跳 RTT", "接入跳丢包", "推流节点 RTT", "推流节点丢包",
                "网关丢包", "网关抖动", "推流节点抖动", "TCP 连接数",
                "CPU 占用", "内存占用", "磁盘队列", "无线信号"
            };
            for (int i = 0; i < titles.Length; i++)
            {
                MetricCard card = new MetricCard(titles[i]);
                card.Dock = DockStyle.Fill;
                card.Margin = new Padding(4);
                _cards[titles[i]] = card;
                grid.Controls.Add(card, i % 4, i / 4);
            }

            // ---------- 信息条 ----------
            Panel info = new Panel();
            info.Dock = DockStyle.Top;
            info.Height = 62;
            info.BackColor = Theme.Card;
            info.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(p, 0, info.Height - 1, info.Width, info.Height - 1);
                }
            };

            _line1 = new Label();
            _line1.AutoSize = false;
            _line1.SetBounds(18, 10, 1000, 22);
            _line1.ForeColor = Theme.Text;
            _line1.Font = Theme.F(9f, FontStyle.Regular);
            _line1.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

            _line2 = new Label();
            _line2.AutoSize = false;
            _line2.SetBounds(18, 33, 1000, 22);
            _line2.ForeColor = Theme.Sub;
            _line2.Font = Theme.F(8.5f, FontStyle.Regular);
            _line2.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

            info.Controls.Add(_line1);
            info.Controls.Add(_line2);

            // ---------- 顶栏 ----------
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 76;
            header.BackColor = Theme.Card;

            _pill = new Pill();
            _pill.SetBounds(18, 22, 104, 32);
            _pill.SetStyle("待机", Theme.Sub);

            Label title = new Label();
            title.Text = Program.AppName;
            title.Font = Theme.F(15f, FontStyle.Bold);
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.SetBounds(136, 16, 200, 26);

            Label sub = new Label();
            sub.Text = "网络与直播质量监控 · 自动识别推流 · 全程留痕可溯源";
            sub.Font = Theme.F(8.5f, FontStyle.Regular);
            sub.ForeColor = Theme.Sub;
            sub.AutoSize = true;
            sub.SetBounds(138, 44, 500, 20);

            _badge = new Label();
            _badge.AutoSize = false;
            _badge.TextAlign = ContentAlignment.MiddleRight;
            _badge.SetBounds(700, 26, 340, 24);
            _badge.ForeColor = Theme.Sub;
            _badge.Font = Theme.F(8.5f, FontStyle.Regular);
            _badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            header.Controls.Add(_pill);
            header.Controls.Add(title);
            header.Controls.Add(sub);
            header.Controls.Add(_badge);

            Controls.Add(eventsPanel);
            Controls.Add(bottom);
            Controls.Add(grid);
            Controls.Add(info);
            Controls.Add(header);
        }

        private Button MakeButton(string text, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 34;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.BackColor = Theme.Card;
            b.ForeColor = Theme.Text;
            b.Font = Theme.F(9f, FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void ToggleSession()
        {
            if (_engine.State == LiveState.Live || _engine.State == LiveState.Gap)
                _engine.StopManual();
            else
                _engine.StartManual();
        }

        private void ShowHelp()
        {
            Form f = new Form();
            f.Text = "使用说明";
            f.Width = 720;
            f.Height = 560;
            f.StartPosition = FormStartPosition.CenterParent;
            f.BackColor = Theme.Card;
            f.Font = Theme.F(9f, FontStyle.Regular);
            TextBox t = new TextBox();
            t.Multiline = true;
            t.ReadOnly = true;
            t.ScrollBars = ScrollBars.Vertical;
            t.Dock = DockStyle.Fill;
            t.BorderStyle = BorderStyle.None;
            t.BackColor = Theme.Card;
            t.Text = string.Join("\r\n", new string[] {
                "【这个工具在做什么】",
                "客户直播时打开本程序，它会持续记录网络质量与电脑性能；发现直播上行流量后自动开始计时，",
                "流量中断超过 5 分钟就判定本次直播结束，并生成一份可以直接发给客户或研发的诊断报告。",
                "",
                "【客户侧三步操作】",
                "1. 双击 LiveDiag.exe（无需安装、无需管理员）。窗口可以最小化到托盘。",
                "2. 正常开播。程序会自动识别推流进程与推流服务器，无需手动开始。",
                "3. 下播后打开“历史会话”，双击一行即可看到报告；需要原始数据就点“导出数据包”。",
                "",
                "【自动识别不准时】",
                "如果程序没识别到直播（界面“推流进程”显示未识别），点“手动开始记录”并按当前情况点",
                "“标记为直播”，下次同一进程就会被优先判定。若把非直播程序误判成直播，点“标记为非直播”。",
                "",
                "【报告怎么看】",
                "报告顶部的“定界结论”按置信度排序，最多 5 条；每条都带时间区间与具体数值证据。",
                "判断顺序是：本机性能 → 本地网关/WiFi → 公网链路 → 推流节点 → 平台侧。",
                "",
                "【口径说明】",
                "· 接入跳：自动用 TTL 逐跳探测，取“网关之后第一跳”（推流路径上第 2 跳，通常是 BRAS/汇聚）。",
                "  它代表接入网质量；本地网关正常而这一跳丢包/时延高，说明问题在光猫、线路或运营商接入网。",
                "· 会话定义：检测到直播上行流量开始计时；中断超过 5 分钟判定结束（可在设置里改）。",
                "· 采样间隔默认 5 秒一条，原始数据按天存放在 data\\samples 下，默认保留 30 天。",
                "· 普通权限下无法读取逐连接的精确字节数时会自动进入“估算模式”，推流上行按整机上行估算；",
                "  以管理员身份运行可获得逐连接精确统计（界面右上角会显示当前模式）。",
                "· 本工具只采集链路与主机指标，不采集画面、音频或聊天内容；观众侧 CDN 卡顿不在本机可观测范围。",
                "",
                "【常见结论对照】",
                "· 网关 RTT 飙升 / 丢包：路由器或 WiFi 问题 → 改有线、换 5G、减少同网设备。",
                "· 网关正常但接入跳（网关之后第一跳）RTT/丢包高：接入网或线路问题 → 查光猫与网线、光衰，",
                "  向运营商报障时说明“本地网关正常、出网关第一跳丢包”，并附本报告。",
                "· 网关正常但推流节点 RTT/丢包高：平台节点或该节点路由 → 换节点、联系平台。",
                "· 上行达到链路能力且延迟上升：上行带宽饱和 → 降码率、关下载/网盘。",
                "· 网络正常但 CPU/GPU 编码打满：本机编码瓶颈 → 换硬件编码、降分辨率。",
                "· 断流事件与运营商时段重合：上行链路问题 → 报障并附本报告。"
            });
            f.Controls.Add(t);
            f.ShowDialog(this);
        }

        private void OpenSessions()
        {
            using (SessionsForm f = new SessionsForm(_engine)) f.ShowDialog(this);
        }

        private int[] Series(Func<Sample, int> sel, int count)
        {
            Sample[] all = _engine.RecentSamples();
            int n = Math.Min(all.Length, count);
            int[] arr = new int[n];
            for (int i = 0; i < n; i++) arr[i] = sel(all[all.Length - n + i]);
            return arr;
        }

        private static int NonNeg(int v) { return v < 0 ? 0 : v; }

        private void Refresh1()
        {
            if (!Visible) return;   // 托盘隐藏时不刷新界面，几乎零开销
            LiveSnapshot s = _engine.Snap;
            if (s == null) return;
            long now = DateTime.UtcNow.Ticks / 10000000L - 62135596800L;

            string stateText;
            Color stateColor;
            switch (s.State)
            {
                case LiveState.Live: stateText = "直播中"; stateColor = Theme.Good; break;
                case LiveState.Probing: stateText = "确认中"; stateColor = Theme.Warn; break;
                case LiveState.Gap: stateText = "断流观察"; stateColor = Theme.Bad; break;
                default: stateText = "待机"; stateColor = Theme.Sub; break;
            }
            _pill.SetStyle(stateText, stateColor);

            _btnSession.Text = (s.State == LiveState.Live || s.State == LiveState.Gap) ? "结束本次记录" : "手动开始记录";

            string elapsed = s.SessionStartTs > 0 ? Fmt.Dur(now - s.SessionStartTs) : "--:--:--";
            if (!s.NetworkUp)
            {
                _line1.Text = "【网络已断开】网卡未连接或未获取到地址 —— 正在记录中，恢复后自动继续。";
                _line1.ForeColor = Theme.Bad;
            }
            else
            {
                _line1.ForeColor = Theme.Text;
            _line1.Text = string.Format("推流进程：{0}　推流服务器：{1}　本机 IP：{2}　网关：{3}　接入跳：{4}　DNS：{5}",
                s.StreamProc.Length > 0 ? s.StreamProc + "(" + s.StreamPid + ")" : "未识别",
                s.ServerIp.Length > 0 ? s.ServerIp + ":" + s.ServerPort : "未识别",
                s.LocalIp.Length > 0 ? s.LocalIp : "-",
                s.Gateway.Length > 0 ? s.Gateway : "-",
                s.HopIp.Length > 0 ? s.HopIp + "(第" + s.HopTtl + "跳)" : "定位中",
                s.Dns);
            }
            _line2.Text = string.Format("网卡：{0}（{1}）　本次记录时长：{2}　断流 {3} 次 / 累计 {4}　数据目录：{5}",
                s.AdapterName, s.Wired ? "有线" : "无线", elapsed, s.GapCount, Fmt.ShortDur(s.GapTotalMs), Config.DataRoot);

            _badge.Text = s.Degraded
                ? "估算模式：无逐连接统计权限（可用管理员运行提升）"
                : "精确模式：逐连接字节统计已启用";
            _badge.ForeColor = s.Degraded ? Theme.Warn : Theme.Good;

            int up = s.StreamUpBps;
            _cards["推流上行"].Set(Fmt.Bps(up), up > 0 ? Theme.Accent : Theme.Sub, Series(delegate(Sample x) { return NonNeg(x.StreamUpBps); }, 90), 1);
            _cards["整机上行"].Set(Fmt.Bps(s.UpBps), Theme.Accent, Series(delegate(Sample x) { return NonNeg(x.UpBps); }, 90), 1);
            _cards["质量分"].Set(s.Quality < 0 ? "-" : s.Quality.ToString(),
                s.Quality < 0 ? Theme.Sub : (s.Quality >= 85 ? Theme.Good : (s.Quality >= 65 ? Theme.Warn : Theme.Bad)),
                Series(delegate(Sample x) { return NonNeg(x.Quality); }, 90), 100);

            _cards["网关 RTT"].Set(Fmt.Ms(s.GwRtt), RttColor(s.GwRtt, 30, 100),
                Series(delegate(Sample x) { return NonNeg(x.GwRttMs); }, 90), 60);
            _cards["接入跳 RTT"].Set(Fmt.Ms(s.HopRtt), RttColor(s.HopRtt, 40, 120),
                Series(delegate(Sample x) { return NonNeg(x.HopRttMs); }, 90), 120);
            _cards["接入跳丢包"].Set(Fmt.Pm(s.HopLossPm), LossColor(s.HopLossPm),
                Series(delegate(Sample x) { return NonNeg(x.HopLossPm); }, 90), 20);
            _cards["推流节点 RTT"].Set(Fmt.Ms(s.SrvRtt), RttColor(s.SrvRtt, 80, 200),
                Series(delegate(Sample x) { return NonNeg(x.SrvRttMs); }, 90), 250);
            _cards["网关丢包"].Set(Fmt.Pm(s.GwLossPm), LossColor(s.GwLossPm),
                Series(delegate(Sample x) { return NonNeg(x.GwLossPm); }, 90), 20);
            _cards["网关抖动"].Set(Fmt.Ms(s.GwRtt < 0 ? -1 : s.GwJit), RttColor(s.GwRtt < 0 ? -1 : s.GwJit, 10, 30),
                Series(delegate(Sample x) { return NonNeg(x.GwJitMs); }, 90), 60);
            _cards["推流节点抖动"].Set(Fmt.Ms(s.SrvJit), RttColor(s.SrvJit, 20, 60),
                Series(delegate(Sample x) { return NonNeg(x.SrvJitMs); }, 90), 60);
            _cards["推流节点丢包"].Set(Fmt.Pm(s.SrvLossPm), LossColor(s.SrvLossPm),
                Series(delegate(Sample x) { return NonNeg(x.SrvLossPm); }, 90), 20);
            _cards["TCP 连接数"].Set(s.TcpConn < 0 ? "-" : s.TcpConn.ToString(), Theme.Text,
                Series(delegate(Sample x) { return NonNeg(x.TcpConn); }, 90), 200);

            _cards["CPU 占用"].Set(Fmt.Pct(s.CpuPm), CpuColor(s.CpuPm),
                Series(delegate(Sample x) { return NonNeg(x.CpuTotalPm); }, 90), 1000);
            _cards["内存占用"].Set(Fmt.Pct(s.MemPm), CpuColor(s.MemPm),
                Series(delegate(Sample x) { return NonNeg(x.MemPm); }, 90), 1000);
            _cards["磁盘队列"].Set(s.DiskQPm10 < 0 ? "-" : (s.DiskQPm10 / 10.0).ToString("0.0"), Theme.Text,
                Series(delegate(Sample x) { return NonNeg(x.DiskQPm10); }, 90), 50);
            _cards["无线信号"].Set(s.WifiRssi <= -1000 ? "有线" : s.WifiRssi + " dBm",
                s.WifiRssi <= -1000 ? Theme.Sub : (s.WifiRssi <= -75 ? Theme.Bad : (s.WifiRssi <= -65 ? Theme.Warn : Theme.Good)),
                Series(delegate(Sample x) { return x.WifiRssi <= -1000 ? 0 : x.WifiRssi + 100; }, 90), 60);
        }

        private static Color RttColor(int v, int good, int bad)
        {
            if (v < 0) return Theme.Sub;
            if (v <= good) return Theme.Good;
            if (v <= bad) return Theme.Warn;
            return Theme.Bad;
        }

        private static Color LossColor(int pm)
        {
            if (pm < 0) return Theme.Sub;
            if (pm == 0) return Theme.Good;
            if (pm < 10) return Theme.Warn;
            return Theme.Bad;
        }

        private static Color CpuColor(int pm)
        {
            if (pm < 0) return Theme.Sub;
            if (pm < 700) return Theme.Text;
            if (pm < 880) return Theme.Warn;
            return Theme.Bad;
        }

        private void OnEngineEvent(EventRecord e)
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke(new Action(delegate { AddEventRow(e); }));
            }
            catch { }
        }

        private void AddEventRow(EventRecord e)
        {
            ListViewItem it = new ListViewItem(e.TimeText);
            it.SubItems.Add(e.SeverityText);
            it.SubItems.Add(e.Title);
            it.SubItems.Add(e.Detail + (e.Evidence.Length > 0 ? " ｜ " + e.Evidence : ""));
            if (e.Severity >= 2) it.ForeColor = Theme.Bad;
            else if (e.Severity == 1) it.ForeColor = Theme.Warn;
            _events.Items.Insert(0, it);
            while (_events.Items.Count > 200) _events.Items.RemoveAt(_events.Items.Count - 1);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
