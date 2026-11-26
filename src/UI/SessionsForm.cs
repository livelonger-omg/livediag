using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LiveDiag.UI
{
    public class SessionsForm : Form
    {
        private readonly Engine _engine;
        private readonly Store _store;
        private ListView _lv;
        private List<SessionInfo> _list = new List<SessionInfo>();
        private Label _hint;

        public SessionsForm(Engine engine)
        {
            _engine = engine;
            _store = new Store();
            BuildUi();
            Reload();
        }

        private void BuildUi()
        {
            Text = "历史会话";
            Width = 1020;
            Height = 560;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            Font = Theme.F(9f, FontStyle.Regular);
            Icon = AppIcon.Create();

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 56;
            bottom.BackColor = Theme.Bg;

            Button bOpen = Btn("打开报告", 100, 16);
            bOpen.Click += delegate { OpenReport(); };
            Button bBuild = Btn("重新生成报告", 120, 124);
            bBuild.Click += delegate { BuildReport(); };
            Button bZip = Btn("导出数据包", 110, 252);
            bZip.Click += delegate { ExportZip(); };
            Button bRef = Btn("刷新", 70, 370);
            bRef.Click += delegate { Reload(); };
            Button bClose = Btn("关闭", 70, 448);
            bClose.Click += delegate { Close(); };

            bottom.Controls.Add(bOpen);
            bottom.Controls.Add(bBuild);
            bottom.Controls.Add(bZip);
            bottom.Controls.Add(bRef);
            bottom.Controls.Add(bClose);

            _hint = new Label();
            _hint.Dock = DockStyle.Bottom;
            _hint.Height = 34;
            _hint.ForeColor = Theme.Sub;
            _hint.TextAlign = ContentAlignment.MiddleLeft;
            _hint.Padding = new Padding(18, 0, 0, 0);
            _hint.Text = "双击一行可直接打开报告。报告为离线 HTML，可直接发给客户或研发。";

            _lv = new ListView();
            _lv.Dock = DockStyle.Fill;
            _lv.View = View.Details;
            _lv.FullRowSelect = true;
            _lv.GridLines = false;
            _lv.BorderStyle = BorderStyle.None;
            _lv.Font = Theme.F(8.5f, FontStyle.Regular);
            _lv.BackColor = Theme.Card;
            _lv.Columns.Add("开始时间", 150);
            _lv.Columns.Add("时长", 80);
            _lv.Columns.Add("质量分", 60);
            _lv.Columns.Add("首要原因", 200);
            _lv.Columns.Add("结束原因", 170);
            _lv.Columns.Add("推流进程", 120);
            _lv.Columns.Add("推流服务器", 130);
            _lv.DoubleClick += delegate { OpenReport(); };

            Controls.Add(_lv);
            Controls.Add(_hint);
            Controls.Add(bottom);
        }

        private Button Btn(string text, int w, int x)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = w;
            b.Height = 32;
            b.Left = x;
            b.Top = 12;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.BackColor = Theme.Card;
            b.ForeColor = Theme.Text;
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void Reload()
        {
            _list = _store.LoadSessions();
            _lv.BeginUpdate();
            _lv.Items.Clear();
            foreach (SessionInfo s in _list)
            {
                ListViewItem it = new ListViewItem(s.StartText);
                it.SubItems.Add(s.DurationText);
                it.SubItems.Add(s.Score < 0 ? "-" : s.Score.ToString());
                it.SubItems.Add(s.TopCause);
                it.SubItems.Add(s.EndReason);
                it.SubItems.Add(s.StreamProc);
                it.SubItems.Add(s.ServerIp);
                it.Tag = s;
                if (s.Score >= 0 && s.Score < 65) it.ForeColor = Theme.Bad;
                else if (s.Score >= 65 && s.Score < 85) it.ForeColor = Theme.Warn;
                _lv.Items.Add(it);
            }
            _lv.EndUpdate();
            _hint.Text = "共 " + _list.Count + " 个会话。双击一行可直接打开报告；报告为离线 HTML，可直接发给客户或研发。";
        }

        private SessionInfo Selected()
        {
            if (_lv.SelectedItems.Count == 0) return null;
            return _lv.SelectedItems[0].Tag as SessionInfo;
        }

        private void OpenReport()
        {
            SessionInfo s = Selected();
            if (s == null) return;
            string path = s.ReportPath;
            if (path.Length == 0 || !File.Exists(path))
            {
                path = BuildReport();
                if (path.Length == 0) return;
            }
            try { Process.Start(path); }
            catch (Exception ex) { MessageBox.Show(this, "打开报告失败：" + ex.Message); }
        }

        private string BuildReport()
        {
            SessionInfo s = Selected();
            if (s == null) return "";
            List<Sample> samples = _store.LoadSamples(s.StartTs * 1000L, Math.Max(s.EndTs, s.StartTs) * 1000L);
            if (samples.Count == 0)
            {
                MessageBox.Show(this, "该会话没有找到采样数据（可能已被保留策略清理）。");
                return "";
            }
            Baseline bl = Analyzer.BuildBaseline(samples);
            List<Finding> findings = Analyzer.Evaluate(samples, bl, false);
            if (findings.Count > 0) s.TopCause = findings[0].Title;
            string path = Report.Build(s, samples, findings, _store, false);
            _store.UpsertSession(s);
            Reload();
            return path;
        }

        private void ExportZip()
        {
            SessionInfo s = Selected();
            if (s == null) return;
            List<Sample> samples = _store.LoadSamples(s.StartTs * 1000L, Math.Max(s.EndTs, s.StartTs) * 1000L);
            List<EventRecord> evs = _store.LoadEvents(s.StartTs * 1000L - 60000, Math.Max(s.EndTs, s.StartTs) * 1000L + 60000, s.Id);
            if (s.ReportPath.Length == 0 || !File.Exists(s.ReportPath))
            {
                Baseline bl = Analyzer.BuildBaseline(samples);
                List<Finding> f = Analyzer.Evaluate(samples, bl, false);
                s.ReportPath = Report.Build(s, samples, f, _store, false);
                _store.UpsertSession(s);
            }
            string zip = Report.ExportZip(s, samples, evs);
            if (zip.Length > 0)
            {
                try { Process.Start("explorer.exe", "/select,\"" + zip + "\""); } catch { }
            }
            else MessageBox.Show(this, "导出失败，请查看 data\\logs 下的日志。");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _store.Dispose();
            base.OnFormClosed(e);
        }
    }
}
