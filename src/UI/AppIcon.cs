using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LiveDiag.UI
{
    internal static class AppIcon
    {
        private static Icon _icon;

        public static Icon Create()
        {
            if (_icon != null) return _icon;
            try
            {
                Bitmap bmp = new Bitmap(32, 32);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(47, 124, 224)))
                        g.FillEllipse(bg, 0, 0, 31, 31);
                    using (Pen p = new Pen(Color.White, 2.2f))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        g.DrawLine(p, 7, 16, 12, 16);
                        g.DrawLine(p, 12, 16, 15, 9);
                        g.DrawLine(p, 15, 9, 19, 23);
                        g.DrawLine(p, 19, 23, 22, 16);
                        g.DrawLine(p, 22, 16, 26, 16);
                    }
                }
                _icon = Icon.FromHandle(bmp.GetHicon());
            }
            catch
            {
                _icon = SystemIcons.Application;
            }
            return _icon;
        }
    }
}
