using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WigiPanel.Layout;

namespace WigiPanel.Rendering
{
    /// <summary>
    /// Draws a <see cref="PanelLayout"/> with GDI+. Independent of WigiDash so the same code
    /// renders on the device and in the offline preview tool.
    /// </summary>
    public sealed class PanelRenderer : IDisposable
    {
        private const string FallbackFont = "Segoe UI";

        private readonly PanelLayout layout;
        private readonly string baseDir;
        // One collection per font file: GDI+ mixes up faces (e.g. returns Bold for Regular)
        // when several faces of one family share a collection.
        private readonly List<PrivateFontCollection> privateFonts = new List<PrivateFontCollection>();
        // Native bold/italic of each face (GDI+ reports every style as available, so read the file).
        private readonly Dictionary<FontFamily, FontStyle> faceStyles = new Dictionary<FontFamily, FontStyle>();
        private readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();
        private readonly Dictionary<string, Image> imageCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Element, Queue<double>> history = new Dictionary<Element, Queue<double>>();
        private readonly StringFormat baseFormat;

        public PanelRenderer(PanelLayout layout, string baseDir)
        {
            this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
            this.baseDir = baseDir ?? AppDomain.CurrentDomain.BaseDirectory;

            baseFormat = (StringFormat)StringFormat.GenericTypographic.Clone();
            baseFormat.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            baseFormat.Trimming = StringTrimming.None;

            foreach (var f in layout.Fonts ?? Enumerable.Empty<string>())
            {
                var path = ResolvePath(f);
                // GDI+ only loads TrueType-outline fonts; CFF-based .otf files throw and are skipped.
                if (!File.Exists(path)) continue;
                var pfc = new PrivateFontCollection();
                try
                {
                    pfc.AddFontFile(path);
                    privateFonts.Add(pfc);
                    var native = ReadNativeStyle(path);
                    foreach (var fam in pfc.Families) faceStyles[fam] = native;
                }
                catch { pfc.Dispose(); }
            }
        }

        public PanelLayout Layout => layout;

        public int RefreshMs => Math.Max(100, layout.RefreshMs ?? 1000);

        /// <summary>Renders the layout to a new bitmap of the requested size.</summary>
        public Bitmap Render(int width, int height, IValueSource values)
        {
            var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) Render(g, width, height, values);
            return bmp;
        }

        public void Render(Graphics g, int width, int height, IValueSource values)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;

            values = new LayoutValueSource(layout, values);
            var canvas = layout.Canvas;
            g.Clear(ParseColor(canvas.Background, Color.Black));
            ApplyCanvasTransform(g, canvas.Width, canvas.Height, width, height);

            foreach (var e in layout.Elements ?? Enumerable.Empty<Element>())
            {
                if (e.Visible == false) continue;
                if (!string.IsNullOrEmpty(e.ShowIf))
                {
                    var probe = ExpandTemplate(e.ShowIf, values).Trim();
                    if (probe.Length == 0 || probe == "--") continue;
                }
                try
                {
                    switch ((e.Type ?? "").ToLowerInvariant())
                    {
                        case "rect": DrawRect(g, e); break;
                        case "text": DrawText(g, e, values); break;
                        case "bar": DrawBar(g, e, values); break;
                        case "gauge": DrawGauge(g, e, values); break;
                        case "imagegauge": DrawImageGauge(g, e, values); break;
                        case "graph": DrawGraph(g, e, values); break;
                        case "image": DrawImage(g, e); break;
                    }
                }
                catch
                {
                    // One bad element must never take down the whole panel.
                }
            }
            g.ResetTransform();
        }

        /// <summary>
        /// A canvas within a few pixels of the target (e.g. a 1024x600 AIDA-style design on the
        /// 1016x592 full-page widget) is drawn 1:1 from the top-left corner, cropping the right and
        /// bottom edges, exactly as AIDA64 itself feeds the WigiDash. Anything else is scaled to fit.
        /// </summary>
        private static void ApplyCanvasTransform(Graphics g, int cw, int ch, int tw, int th)
        {
            if (cw <= 0 || ch <= 0) return;
            if (Math.Abs(cw - tw) <= 16 && Math.Abs(ch - th) <= 16) return;
            float s = Math.Min((float)tw / cw, (float)th / ch);
            g.TranslateTransform((tw - cw * s) / 2f, (th - ch * s) / 2f);
            g.ScaleTransform(s, s);
        }

        // ---------------------------------------------------------------- elements

        private static void DrawRect(Graphics g, Element e)
        {
            var r = new RectangleF(e.X, e.Y, e.W ?? 0, e.H ?? 0);
            using (var b = new SolidBrush(ParseColor(e.Color, Color.White)))
                FillRounded(g, b, r, e.Radius ?? 0);
        }

        private void DrawText(Graphics g, Element e, IValueSource values)
        {
            var text = ExpandTemplate(e.Text ?? "", values);
            if (text.Length == 0) return;

            var font = GetFont(e.Font, e.Size ?? 12, e.Style);
            var color = ResolveColor(e, values, sensorValue: e.Sensor != null ? Scaled(e.Sensor, values) : null);
            using (var brush = new SolidBrush(color))
            using (var fmt = (StringFormat)baseFormat.Clone())
            {
                var align = (e.Align ?? "left").ToLowerInvariant();
                fmt.Alignment = align == "right" ? StringAlignment.Far
                              : align == "center" ? StringAlignment.Center
                              : StringAlignment.Near;

                // Vertical placement within y..y+h uses the font's line spacing.
                float y = e.Y;
                var valign = (e.VAlign ?? "top").ToLowerInvariant();
                if (e.H.HasValue && valign != "top")
                {
                    float lineH = font.GetHeight(g);
                    y += valign == "bottom" ? e.H.Value - lineH : (e.H.Value - lineH) / 2f;
                }

                RectangleF rect;
                const float big = 4000f;
                if (e.W.HasValue) rect = new RectangleF(e.X, y, e.W.Value, big);
                else if (align == "right") rect = new RectangleF(e.X - big, y, big, big);
                else if (align == "center") rect = new RectangleF(e.X - big / 2, y, big, big);
                else rect = new RectangleF(e.X, y, big, big);

                g.DrawString(text, font, brush, rect, fmt);
            }
        }

        private void DrawBar(Graphics g, Element e, IValueSource values)
        {
            var r = new RectangleF(e.X, e.Y, e.W ?? 100, e.H ?? 8);
            float radius = e.Radius ?? 0;
            var v = Scaled(e.Sensor, values);
            double frac = Fraction(v, e.Min ?? 0, e.Max ?? 100);

            using (var bg = new SolidBrush(ResolveBg(e, v, Color.FromArgb(0x36, 0x36, 0x36))))
                FillRounded(g, bg, r, radius);

            if (frac > 0)
            {
                float fw = (float)(r.Width * frac), fh = (float)(r.Height * frac);
                bool rev = e.Reverse == true;
                RectangleF fill = e.Vertical == true
                    ? new RectangleF(r.X, rev ? r.Y : r.Bottom - fh, r.Width, fh)
                    : new RectangleF(rev ? r.Right - fw : r.X, r.Y, fw, r.Height);

                using (var fg = new SolidBrush(ResolveColor(e, values, v)))
                using (var clip = RoundedPath(r, radius))
                {
                    var state = g.Save();
                    g.SetClip(clip, CombineMode.Intersect);
                    g.FillRectangle(fg, fill);
                    g.Restore(state);
                }
            }

            if (!string.IsNullOrEmpty(e.FrameColor))
                using (var pen = new Pen(ParseColor(e.FrameColor, Color.Gray), 1f))
                    g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        /// <summary>AIDA-style gauge: one pre-drawn image per state, picked by the value.</summary>
        private void DrawImageGauge(Graphics g, Element e, IValueSource values)
        {
            if (e.Frames == null || e.Frames.Count == 0) return;
            var v = Scaled(e.Sensor, values);
            int n = e.Frames.Count;
            int idx = v.HasValue ? (int)Math.Round(Fraction(v, e.Min ?? 0, e.Max ?? 100) * (n - 1)) : 0;
            var img = GetImage(e.Frames[Math.Max(0, Math.Min(n - 1, idx))]);
            if (img == null) return;
            g.DrawImage(img, new RectangleF(e.X, e.Y, e.W ?? img.Width, e.H ?? img.Height));
        }

        private void DrawGauge(Graphics g, Element e, IValueSource values)
        {
            float d = e.W ?? 170;
            float t = e.Thickness ?? 12;
            float start = e.Start ?? 135;
            float sweep = e.Sweep ?? 270;
            var v = Scaled(e.Sensor, values);
            double frac = Fraction(v, e.Min ?? 0, e.Max ?? 100);
            int steps = e.Steps ?? 0;
            if (steps > 1) frac = Math.Round(frac * (steps - 1)) / (steps - 1);

            var arcRect = new RectangleF(e.X + t / 2, e.Y + t / 2, d - t, d - t);
            bool round = e.RoundCaps == true;

            using (var bgPen = new Pen(ParseColor(e.Bg, Color.FromArgb(0x36, 0x36, 0x36)), t))
            {
                if (round && sweep < 360) bgPen.StartCap = bgPen.EndCap = LineCap.Round;
                if (sweep >= 360) g.DrawEllipse(bgPen, arcRect);
                else g.DrawArc(bgPen, arcRect, start, sweep);
            }
            if (frac <= 0) return;
            using (var fgPen = new Pen(ResolveColor(e, values, v), t))
            {
                if (round) fgPen.StartCap = fgPen.EndCap = LineCap.Round;
                float fs = (float)(sweep * frac);
                if (fs >= 360) g.DrawEllipse(fgPen, arcRect);
                else g.DrawArc(fgPen, arcRect, start, fs);
            }
        }

        private void DrawGraph(Graphics g, Element e, IValueSource values)
        {
            var r = new RectangleF(e.X, e.Y, e.W ?? 200, e.H ?? 50);
            float step = e.Step ?? 0;
            int max = Math.Max(2, e.Points ?? (step > 0 ? (int)(r.Width / step) + 2 : 60));
            if (step <= 0) step = r.Width / (max - 1);
            if (!history.TryGetValue(e, out var q)) history[e] = q = new Queue<double>();
            var v = Scaled(e.Sensor, values);
            if (v.HasValue) q.Enqueue(v.Value);
            while (q.Count > max) q.Dequeue();

            if (!string.IsNullOrEmpty(e.Bg))
                using (var bg = new SolidBrush(ParseColor(e.Bg, Color.Transparent)))
                    FillRounded(g, bg, r, e.Radius ?? 0);

            var state = g.Save();
            g.SetClip(r, CombineMode.Intersect);

            if (!string.IsNullOrEmpty(e.GridColor) && (e.GridSize ?? 0) >= 2)
                using (var gp = new Pen(ParseColor(e.GridColor, Color.Green), 1f))
                {
                    for (float gx = r.Right - 1; gx >= r.X; gx -= e.GridSize.Value) g.DrawLine(gp, gx, r.Y, gx, r.Bottom);
                    for (float gy = r.Bottom - 1; gy >= r.Y; gy -= e.GridSize.Value) g.DrawLine(gp, r.X, gy, r.Right, gy);
                }

            if (q.Count >= 1)
            {
                double lo = e.Min ?? 0, hi = e.Max ?? 100;
                if (e.Autoscale == true) hi = Math.Max(lo + 1e-9, q.Max() * 1.1);
                var pts = q.Select((val, i) => new PointF(
                    r.Right - (q.Count - 1 - i) * step,
                    r.Bottom - (float)(r.Height * Fraction(val, lo, hi)))).ToArray();

                var color = ResolveColor(e, values, v);
                var kind = (e.GraphType ?? (e.Fill == false ? "line" : "area")).ToLowerInvariant();
                if (kind == "bars")
                {
                    using (var b = new SolidBrush(color))
                        foreach (var p in pts) g.FillRectangle(b, p.X - Math.Max(1, step) + 1, p.Y, Math.Max(1, step), r.Bottom - p.Y);
                }
                else if (pts.Length >= 2)
                {
                    if (kind == "area")
                    {
                        var poly = new List<PointF>(pts) { new PointF(pts[pts.Length - 1].X, r.Bottom), new PointF(pts[0].X, r.Bottom) };
                        // AIDA area graphs are solid; layouts with a separate line get a translucent fill.
                        int alpha = e.GraphType == "area" ? 255 : 60;
                        using (var fb = new SolidBrush(Color.FromArgb(alpha, color))) g.FillPolygon(fb, poly.ToArray());
                    }
                    if (kind == "line" || e.GraphType == null)
                        using (var pen = new Pen(color, e.LineWidth ?? 2f) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
                }
            }
            g.Restore(state);

            if (!string.IsNullOrEmpty(e.FrameColor))
                using (var pen = new Pen(ParseColor(e.FrameColor, Color.Gray), 1f))
                    g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        private void DrawImage(Graphics g, Element e)
        {
            if (string.IsNullOrEmpty(e.Path)) return;
            var img = GetImage(e.Path);
            if (img == null) return;
            g.DrawImage(img, new RectangleF(e.X, e.Y, e.W ?? img.Width, e.H ?? img.Height));
        }

        /// <summary>Embedded skin image by name first, then a file path relative to the layout.</summary>
        private Image GetImage(string nameOrPath)
        {
            if (imageCache.TryGetValue(nameOrPath, out var img)) return img;
            try
            {
                if (layout.Images != null && layout.Images.TryGetValue(Path.GetFileName(nameOrPath), out var bytes))
                    using (var ms = new MemoryStream(bytes))
                    using (var tmp = Image.FromStream(ms))
                        img = new Bitmap(tmp);
                else
                {
                    var path = ResolvePath(nameOrPath);
                    img = File.Exists(path) ? LoadImageUnlocked(path) : null;
                }
            }
            catch
            {
                img = null;
            }
            imageCache[nameOrPath] = img;
            return img;
        }

        // ---------------------------------------------------------------- templates

        /// <summary>
        /// Expands {KEY} and {KEY:format}. KEY is a layout var, a built-in (CPUMODEL, GPUMODEL,
        /// HOSTNAME, time, date) or a sensor binding name. "{{" and "}}" are literal braces.
        /// </summary>
        public string ExpandTemplate(string template, IValueSource values)
        {
            if (template.IndexOf('{') < 0) return template.Replace("}}", "}");
            var sb = new StringBuilder(template.Length + 16);
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                if (c == '{' && i + 1 < template.Length && template[i + 1] == '{') { sb.Append('{'); i++; continue; }
                if (c == '}' && i + 1 < template.Length && template[i + 1] == '}') { sb.Append('}'); i++; continue; }
                if (c != '{') { sb.Append(c); continue; }

                int end = template.IndexOf('}', i + 1);
                if (end < 0) { sb.Append(template, i, template.Length - i); break; }
                var token = template.Substring(i + 1, end - i - 1);
                int colon = token.IndexOf(':');
                var key = colon < 0 ? token : token.Substring(0, colon);
                var fmt = colon < 0 ? null : token.Substring(colon + 1);
                sb.Append(ResolveToken(key.Trim(), fmt, values));
                i = end;
            }
            return sb.ToString();
        }

        private string ResolveToken(string key, string fmt, IValueSource values)
        {
            if (layout.Vars != null && layout.Vars.TryGetValue(key, out var custom)) return custom ?? "";
            switch (key.ToUpperInvariant())
            {
                case "CPUMODEL": return SystemInfo.CpuModel;
                case "GPUMODEL": return SystemInfo.GpuModel;
                case "HOSTNAME": return SystemInfo.HostName;
                case "MOBOMODEL": return SystemInfo.MotherboardModel;
                case "BIOSVER": return SystemInfo.BiosVersion;
                case "MEMTYPE": return SystemInfo.MemoryType;
                case "DESKRES": return SystemInfo.DesktopResolution;
                case "UPTIME": return SystemInfo.Uptime;
                case "LOCALIP": return SystemInfo.LocalIp;
                case "LOCALIP6": return SystemInfo.LocalIp6;
                case "TIME": return DateTime.Now.ToString(fmt ?? "HH:mm", CultureInfo.CurrentCulture);
                case "DATE": return DateTime.Now.ToString(fmt ?? "d", CultureInfo.CurrentCulture);
            }
            var v = Scaled(key, values);
            if (!v.HasValue) return layout.Sensors != null && layout.Sensors.ContainsKey(key) ? "--" : "{" + key + "}";
            return v.Value.ToString(fmt ?? "0", CultureInfo.CurrentCulture);
        }

        // ---------------------------------------------------------------- helpers

        private double? Scaled(string key, IValueSource values)
        {
            if (string.IsNullOrEmpty(key) || values == null) return null;
            var raw = values.Get(key);
            if (!raw.HasValue || double.IsNaN(raw.Value)) return null;
            if (layout.Sensors != null && layout.Sensors.TryGetValue(key, out var b) && b.Scale.HasValue)
                return raw.Value * b.Scale.Value;
            return raw;
        }

        private static double Fraction(double? v, double min, double max)
        {
            if (!v.HasValue || max <= min) return 0;
            return Math.Max(0, Math.Min(1, (v.Value - min) / (max - min)));
        }

        private static Color ResolveColor(Element e, IValueSource values, double? sensorValue)
        {
            var color = ParseColor(e.Color, Color.White);
            if (sensorValue.HasValue && e.Thresholds != null)
                foreach (var t in e.Thresholds.OrderBy(t => t.At))
                    if (sensorValue.Value >= t.At) color = ParseColor(t.Color, color);
            return color;
        }

        private static Color ResolveBg(Element e, double? sensorValue, Color fallback)
        {
            var color = ParseColor(e.Bg, fallback);
            if (sensorValue.HasValue && e.Thresholds != null)
                foreach (var t in e.Thresholds.OrderBy(t => t.At))
                    if (sensorValue.Value >= t.At && !string.IsNullOrEmpty(t.Bg)) color = ParseColor(t.Bg, color);
            return color;
        }

        private Font GetFont(string family, float size, string style)
        {
            var fs = ParseStyle(style);
            var key = (family ?? "") + "|" + size.ToString(CultureInfo.InvariantCulture) + "|" + fs;
            if (fontCache.TryGetValue(key, out var f)) return f;

            // Prefer the private face that natively has this style (bold/italic flags only; GDI+
            // simulates underline/strikeout), then any face of the family.
            var core = fs & (FontStyle.Bold | FontStyle.Italic);
            var faces = privateFonts.SelectMany(c => c.Families)
                .Where(p => string.Equals(p.Name, family, StringComparison.OrdinalIgnoreCase)).ToList();
            FontStyle Native(FontFamily p) => faceStyles.TryGetValue(p, out var s) ? s : FontStyle.Regular;
            FontFamily ff = faces.FirstOrDefault(p => Native(p) == core)
                            ?? faces.FirstOrDefault(p => Native(p) == FontStyle.Regular)
                            ?? faces.FirstOrDefault();
            if (ff == null)
            {
                try { ff = new FontFamily(string.IsNullOrEmpty(family) ? FallbackFont : family); }
                catch (ArgumentException) { ff = new FontFamily(FallbackFont); }
            }
            if (!ff.IsStyleAvailable(fs)) fs = ff.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : core;
            f = new Font(ff, size, fs, GraphicsUnit.Point);
            fontCache[key] = f;
            return f;
        }

        /// <summary>Bold/italic bits from the font's 'head' table (macStyle), for TrueType and OpenType files.</summary>
        private static FontStyle ReadNativeStyle(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var br = new BinaryReader(fs))
                {
                    int U16() { var b = br.ReadBytes(2); return (b[0] << 8) | b[1]; }
                    long U32() { var b = br.ReadBytes(4); return ((long)b[0] << 24) | ((long)b[1] << 16) | ((long)b[2] << 8) | b[3]; }
                    fs.Position = 4;
                    int numTables = U16();
                    fs.Position = 12;
                    for (int i = 0; i < numTables; i++)
                    {
                        var tag = new string(br.ReadChars(4));
                        U32(); // checksum
                        long offset = U32();
                        U32(); // length
                        if (tag != "head") continue;
                        fs.Position = offset + 44;
                        int mac = U16();
                        var style = FontStyle.Regular;
                        if ((mac & 1) != 0) style |= FontStyle.Bold;
                        if ((mac & 2) != 0) style |= FontStyle.Italic;
                        return style;
                    }
                }
            }
            catch { }
            return FontStyle.Regular;
        }

        private static FontStyle ParseStyle(string s)
        {
            var fs = FontStyle.Regular;
            foreach (var token in (s ?? "").ToLowerInvariant().Split(new[] { ' ', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (token)
                {
                    case "bold": fs |= FontStyle.Bold; break;
                    case "italic": fs |= FontStyle.Italic; break;
                    case "bolditalic": fs |= FontStyle.Bold | FontStyle.Italic; break;
                    case "underline": fs |= FontStyle.Underline; break;
                    case "strikeout": fs |= FontStyle.Strikeout; break;
                }
            }
            return fs;
        }

        public static Color ParseColor(string s, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim();
            if (s[0] == '#')
            {
                var hex = s.Substring(1);
                if (hex.Length == 3) hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
                if (hex.Length == 6) hex = "FF" + hex;
                if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
                    return Color.FromArgb(unchecked((int)argb));
                return fallback;
            }
            var named = Color.FromName(s);
            return named.IsKnownColor ? named : fallback;
        }

        private static GraphicsPath RoundedPath(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0.5f) { p.AddRectangle(r); return p; }
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static void FillRounded(Graphics g, Brush b, RectangleF r, float radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            if (radius <= 0.5f) { g.FillRectangle(b, r); return; }
            using (var p = RoundedPath(r, radius)) g.FillPath(b, p);
        }

        private string ResolvePath(string p) => Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p);

        /// <summary>Loads an image without keeping the file locked, so layouts can be edited live.</summary>
        private static Image LoadImageUnlocked(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var tmp = Image.FromStream(fs))
                return new Bitmap(tmp);
        }

        public void Dispose()
        {
            foreach (var f in fontCache.Values) f.Dispose();
            foreach (var i in imageCache.Values) i?.Dispose();
            fontCache.Clear();
            imageCache.Clear();
            foreach (var pfc in privateFonts) pfc.Dispose();
            privateFonts.Clear();
            baseFormat.Dispose();
        }
    }
}
