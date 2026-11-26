using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiveDiag.UI
{
    public class SettingsForm : Form
    {
        private readonly Engine _engine;
        private readonly Config _cfg;
        private TextBox _tSample, _tCandidate, _tProbe, _tGap, _tEnd, _tPublic, _tDns, _tKeywords, _tRetention;
        private CheckBox _cAuto;

        public SettingsForm(Engine engine)
        {
            _engine = engine;
            _cfg = engine.Cfg;
            BuildUi();
            LoadValues();
        }

        private void BuildUi()
        {
            Text = "设置";
            Width = 640;
            Height = 560;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            Font = Theme.F(9f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            int y = 18;
            _tSample = AddRow("采样间隔（秒）", ref y);
            _tCandidate = AddRow("判定为直播的上行阈值（Kbps）", ref y);
            _tProbe = AddRow("确认持续时间（秒）", ref y);
            _tGap = AddRow("中断判定阈值（秒）", ref y);
            _tEnd = AddRow("结束判定阈值（秒，默认 300 = 5 分钟）", ref y);
            _tPublic = AddRow("公网参考目标（逗号分隔 IP）", ref y);
            _tDns = AddRow("DNS 探测域名", ref y);
            _tKeywords = AddRow("直播进程关键字（逗号分隔）", ref y);
            _tRetention = AddRow("原始数据保留天数", ref y);

            _cAuto = new CheckBox();
            _cAuto.Text = "开机自动启动（写入当前用户启动项）";
            _cAuto.SetBounds(20, y + 4, 400, 24);
            _cAuto.ForeColor = Theme.Text;
            Controls.Add(_cAuto);

            Label path = new Label();
            path.Text = "数据目录：" + Config.DataRoot;
            path.SetBounds(20, y + 36, 580, 22);
            path.ForeColor = Theme.Sub;
            Controls.Add(path);

            Label tip = new Label();
            tip.Text = "说明：会话从检测到直播上行流量开始计时；流量中断超过“结束判定阈值”即判定本次直播结束。\r\n" +
                       "修改采样间隔会立即生效；其余阈值在下次会话开始时生效。";
            tip.SetBounds(20, y + 66, 590, 46);
            tip.ForeColor = Theme.Sub;
            Controls.Add(tip);

            Button ok = new Button();
            ok.Text = "保存并应用";
            ok.SetBounds(400, y + 120, 110, 34);
            ok.FlatStyle = FlatStyle.Flat;
            ok.FlatAppearance.BorderColor = Theme.Border;
            ok.BackColor = Theme.Card;
            ok.Click += delegate { Save(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(518, y + 120, 80, 34);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderColor = Theme.Border;
            cancel.BackColor = Theme.Card;
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
        }

        private TextBox AddRow(string caption, ref int y)
        {
            Label l = new Label();
            l.Text = caption;
            l.SetBounds(20, y + 3, 260, 22);
            l.ForeColor = Theme.Text;
            Controls.Add(l);

            TextBox t = new TextBox();
            t.SetBounds(290, y, 300, 24);
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Theme.Card;
            Controls.Add(t);

            y += 34;
            return t;
        }

        private void LoadValues()
        {
            _tSample.Text = _cfg.SampleSeconds.ToString();
            _tCandidate.Text = _cfg.CandidateKbps.ToString();
            _tProbe.Text = _cfg.ProbeSeconds.ToString();
            _tGap.Text = _cfg.GapSeconds.ToString();
            _tEnd.Text = _cfg.EndSeconds.ToString();
            _tPublic.Text = _cfg.PublicTargets;
            _tDns.Text = _cfg.DnsProbeHost;
            _tKeywords.Text = _cfg.StreamProcessKeywords;
            _tRetention.Text = _cfg.RawRetentionDays.ToString();
            _cAuto.Checked = _cfg.AutoStart;
        }

        private void Save()
        {
            int n;
            if (int.TryParse(_tSample.Text, out n) && n >= 1 && n <= 60) _cfg.SampleSeconds = n;
            if (int.TryParse(_tCandidate.Text, out n) && n >= 20) _cfg.CandidateKbps = n;
            if (int.TryParse(_tProbe.Text, out n) && n >= 3) _cfg.ProbeSeconds = n;
            if (int.TryParse(_tGap.Text, out n) && n >= 3) _cfg.GapSeconds = n;
            if (int.TryParse(_tEnd.Text, out n) && n >= 30) _cfg.EndSeconds = n;
            if (int.TryParse(_tRetention.Text, out n) && n >= 1) _cfg.RawRetentionDays = n;
            if (_tPublic.Text.Trim().Length > 0) _cfg.PublicTargets = _tPublic.Text.Trim();
            if (_tDns.Text.Trim().Length > 0) _cfg.DnsProbeHost = _tDns.Text.Trim();
            if (_tKeywords.Text.Trim().Length > 0) _cfg.StreamProcessKeywords = _tKeywords.Text.Trim();
            if (_cfg.AutoStart != _cAuto.Checked)
            {
                string err;
                if (!AutoStart.Apply(_cAuto.Checked, out err))
                {
                    MessageBox.Show(this, "设置开机启动失败：" + err + "\n配置未改动。");
                    return;
                }
                _cfg.AutoStart = _cAuto.Checked;
            }
            _cfg.Save();
            _engine.ReloadConfig(_cfg);
            MessageBox.Show(this, "已保存。");
            Close();
        }

    }
}
