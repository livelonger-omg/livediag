using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiveDiag.UI
{
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(246, 247, 249);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(230, 233, 239);
        public static readonly Color Text = Color.FromArgb(29, 36, 48);
        public static readonly Color Sub = Color.FromArgb(123, 134, 152);
        public static readonly Color Accent = Color.FromArgb(47, 124, 224);
        public static readonly Color Good = Color.FromArgb(47, 174, 122);
        public static readonly Color Warn = Color.FromArgb(226, 163, 60);
        public static readonly Color Bad = Color.FromArgb(226, 96, 60);
        public static readonly Color Mono = Color.FromArgb(90, 101, 119);

        public static Font F(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style);
        }
    }

    /// <summary>迷你折线图，GDI+ 自绘，零依赖。</summary>
    public class Sparkline : Control
    {
        private int[] _data = new int[0];
        private Color _color = Theme.Accent;
        private int _max;

        public Sparkline()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        public void SetData(int[] data, Color color, int max)
        {
            _data = data;
            _color = color;
            _max = max;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_data == null || _data.Length < 2)
            {
                e.Graphics.Clear(BackColor);
                return;
            }
            int w = Width - 2;
            int h = Height - 2;
            if (w <= 2 || h <= 2) return;

            int max = _max;
            for (int i = 0; i < _data.Length; i++) if (_data[i] > max) max = _data[i];
            if (max <= 0) max = 1;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            using (Pen grid = new Pen(Color.FromArgb(238, 241, 246)))
            {
                e.Graphics.DrawLine(grid, 1, 1, 1, h);
                e.Graphics.DrawLine(grid, 1, h, w, h);
            }

            using (Pen pen = new Pen(_color, 1.4f))
            {
                PointF[] pts = new PointF[_data.Length];
                for (int i = 0; i < _data.Length; i++)
                {
                    float x = 1 + (float)i * (w - 1) / (_data.Length - 1);
                    int v = _data[i];
                    if (v < 0) v = 0;
                    float y = h - (float)v * h / max;
                    pts[i] = new PointF(x, y);
                }
                try { e.Graphics.DrawLines(pen, pts); } catch { }
            }
        }
    }

    /// <summary>指标卡片：标题 + 数值 + 迷你曲线。</summary>
    public class MetricCard : Panel
    {
        private readonly Label _title;
        private readonly Label _value;
        private readonly Sparkline _spark;

        public MetricCard(string title)
        {
            BackColor = Theme.Card;
            Padding = new Padding(12, 10, 12, 8);
            _title = new Label();
            _title.Text = title;
            _title.ForeColor = Theme.Sub;
            _title.Font = Theme.F(8.5f, FontStyle.Regular);
            _title.AutoSize = false;
            _title.Dock = DockStyle.Top;
            _title.Height = 18;

            _value = new Label();
            _value.Text = "-";
            _value.ForeColor = Theme.Text;
            _value.Font = Theme.F(15f, FontStyle.Bold);
            _value.AutoSize = false;
            _value.Dock = DockStyle.Top;
            _value.Height = 30;
            _value.TextAlign = ContentAlignment.MiddleLeft;

            _spark = new Sparkline();
            _spark.Dock = DockStyle.Fill;

            Controls.Add(_spark);
            Controls.Add(_value);
            Controls.Add(_title);
        }

        public void Set(string value, Color color, int[] series, int max)
        {
            _value.Text = value;
            _value.ForeColor = color;
            _spark.SetData(series, color, max);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>圆角状态标签。</summary>
    public class Pill : Label
    {
        public Pill()
        {
            AutoSize = false;
            TextAlign = ContentAlignment.MiddleCenter;
            ForeColor = Color.White;
            Font = Theme.F(9f, FontStyle.Bold);
            BackColor = Theme.Sub;
        }

        public void SetStyle(string text, Color back)
        {
            Text = text;
            BackColor = back;
            Invalidate();
        }
    }
}
