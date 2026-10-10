using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CoffeeCup
{
    /// <summary>
    /// Draws the coffee-mug tray icon. Geometry is on a 32x32 design grid shared with
    /// tools/make_icon.py, scaled to whatever size the tray asks for.
    /// </summary>
    internal sealed class MugRenderer : IDisposable
    {
        public const int Levels = 24;     // coffee level is quantised to this many steps
        public const int SteamFrames = 6; // frames in one steam cycle

        private const float BodyL = 4f, BodyR = 22f, BodyT = 12f, BodyB = 29f, BodyRadius = 4.5f;
        private const float HandleCx = 22f, HandleCy = 19.5f, HandleR = 4.6f;
        private const float Stroke = 2.6f;
        private const float FullTop = 14.6f;

        private static readonly Color Coffee = Color.FromArgb(124, 74, 38);
        private static readonly Color Crema = Color.FromArgb(201, 154, 98);

        private readonly Dictionary<(int level, int steam), Icon> _cache = new Dictionary<(int, int), Icon>();
        private int _size;
        private bool _lightTaskbar;

        public MugRenderer(int size, bool lightTaskbar)
        {
            Reset(size, lightTaskbar);
        }

        /// <summary>Call when the icon size or taskbar theme changes.</summary>
        public void Reset(int size, bool lightTaskbar)
        {
            _size = Math.Max(16, size);
            _lightTaskbar = lightTaskbar;
            ClearCache();
        }

        /// <param name="level">0 (empty) to <see cref="Levels"/> (full).</param>
        /// <param name="steamFrame">0 to <see cref="SteamFrames"/> - 1.</param>
        public Icon Get(int level, int steamFrame)
        {
            level = Math.Max(0, Math.Min(Levels, level));
            steamFrame = level == 0 ? 0 : ((steamFrame % SteamFrames) + SteamFrames) % SteamFrames;
            var key = (level, steamFrame);
            if (!_cache.TryGetValue(key, out var icon))
            {
                icon = Render(level / (float)Levels, steamFrame);
                _cache[key] = icon;
            }
            return icon;
        }

        private Icon Render(float level, int steamFrame)
        {
            Color outline = _lightTaskbar ? Color.FromArgb(32, 32, 32) : Color.FromArgb(240, 240, 240);

            using (var bmp = new Bitmap(_size, _size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var pen = new Pen(outline, Stroke) { StartCap = LineCap.Flat, EndCap = LineCap.Flat })
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    g.ScaleTransform(_size / 32f, _size / 32f);

                    // Handle: a ring whose left half is hidden behind the body.
                    g.SetClip(RectangleF.FromLTRB(BodyL, 0, BodyR, 32), CombineMode.Exclude);
                    g.DrawEllipse(pen, HandleCx - HandleR, HandleCy - HandleR, HandleR * 2, HandleR * 2);
                    g.ResetClip();

                    // Coffee, clipped to the inside of the mug.
                    if (level > 0)
                    {
                        float inL = BodyL + Stroke / 2, inR = BodyR - Stroke / 2, inB = BodyB - Stroke / 2;
                        using (var inside = RoundedBottom(inL, BodyT - 2, inR, inB, BodyRadius - Stroke / 2))
                        {
                            g.SetClip(inside);
                            float top = inB - (inB - FullTop) * level;
                            using (var b = new SolidBrush(Coffee)) g.FillRectangle(b, inL - 1, top, inR - inL + 2, inB - top + 1);
                            using (var b = new SolidBrush(Crema)) g.FillRectangle(b, inL - 1, top, inR - inL + 2, 1.4f);
                            g.ResetClip();
                        }
                    }

                    // Body outline (open at the top) and the rim.
                    using (var body = RoundedBottom(BodyL, BodyT, BodyR, BodyB, BodyRadius, closed: false))
                        g.DrawPath(pen, body);
                    g.DrawLine(pen, BodyL - Stroke / 2, BodyT, BodyR + Stroke / 2, BodyT);

                    if (level > 0.05f)
                        DrawSteam(g, outline, level, steamFrame);
                }
                return ToIcon(bmp);
            }
        }

        private static void DrawSteam(Graphics g, Color outline, float level, int frame)
        {
            int alpha = (int)(190 * Math.Min(1f, level * 1.4f));
            double phase = frame * 2 * Math.PI / SteamFrames;
            using (var pen = new Pen(Color.FromArgb(alpha, outline), Stroke * 0.75f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            })
            {
                float[] xs = { 8.5f, 13f, 17.5f };
                for (int i = 0; i < xs.Length; i++)
                {
                    var pts = new PointF[17];
                    for (int j = 0; j < pts.Length; j++)
                    {
                        double t = j / (double)(pts.Length - 1);
                        pts[j] = new PointF(
                            xs[i] + (float)Math.Sin(t * 2 * Math.PI + phase + i * 0.8),
                            9.2f - (float)t * 8f);
                    }
                    g.DrawLines(pen, pts);
                }
            }
        }

        private static GraphicsPath RoundedBottom(float l, float t, float r, float b, float radius, bool closed = true)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddLine(l, t, l, b - radius);
            p.AddArc(l, b - d, d, d, 180, -90);
            p.AddLine(l + radius, b, r - radius, b);
            p.AddArc(r - d, b - d, d, d, 90, -90);
            p.AddLine(r, b - radius, r, t);
            if (closed) p.CloseFigure();
            return p;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static Icon ToIcon(Bitmap bmp)
        {
            IntPtr h = bmp.GetHicon();
            try
            {
                using (var tmp = Icon.FromHandle(h))
                    return (Icon)tmp.Clone(); // the clone owns its own handle
            }
            finally
            {
                DestroyIcon(h);
            }
        }

        private void ClearCache()
        {
            foreach (var icon in _cache.Values) icon.Dispose();
            _cache.Clear();
        }

        public void Dispose() => ClearCache();
    }
}
