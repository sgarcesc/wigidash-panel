using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WigiPanel.Layout.Aida
{
    /// <summary>
    /// Imports AIDA64 skins, both the G.Skill WigiDash LCD export (.gslcd) and SensorPanel export
    /// (.sensorpanel), into a <see cref="PanelLayout"/>. Both are line-based tag soups:
    /// one item per line (&lt;ID&gt;..&lt;ITMX&gt;..), images embedded as hex (IMGDAT), and custom gauge
    /// frames stored once per file as GAUSTAFNM/GAUSTADAT pairs.
    /// </summary>
    public static class AidaSkinImporter
    {
        public static readonly string[] Extensions = { ".gslcd", ".sensorpanel" };

        public static bool IsAidaSkin(string path) =>
            Extensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

        private static readonly Regex TagRx = new Regex(@"<([A-Z0-9]+)>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex GaugeFrameRx = new Regex(@"<GAUSTAFNM>(.*?)</GAUSTAFNM>\s*<GAUSTADAT>([0-9A-Fa-f]*)</GAUSTADAT>", RegexOptions.Singleline | RegexOptions.Compiled);

        public static PanelLayout Import(string path, AidaSensorMap map)
        {
            var text = ReadText(path);
            var dir = Path.GetDirectoryName(path) ?? "";
            var layout = new PanelLayout
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Canvas = new CanvasSpec(),
                Sensors = new Dictionary<string, SensorBinding>(),
                Elements = new List<Element>(),
                Images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase),
                Warnings = new List<string>(),
                Fonts = FindFonts(dir),
                RefreshMs = 1000,
            };

            // Header
            var header = Tags(text.Length > 4000 ? text.Substring(0, 4000) : text);
            if (header.TryGetValue("SPWIDTH", out var sw) && int.TryParse(sw, out var w) && w > 0) layout.Canvas.Width = w;
            if (header.TryGetValue("SPHEIGHT", out var sh) && int.TryParse(sh, out var h) && h > 0) layout.Canvas.Height = h;
            var bg = header.TryGetValue("LCDBGCOLOR", out var lbg) ? lbg : header.TryGetValue("SPBGCOLOR", out var sbg) ? sbg : null;
            layout.Canvas.Background = AidaColor(bg, "#000000");

            if (map?.Snapshot != null && map.SourcePath != null)
                layout.Snapshot = Path.Combine(Path.GetDirectoryName(map.SourcePath) ?? "", map.Snapshot);

            // Gauge frame images stored once per file
            foreach (Match m in GaugeFrameRx.Matches(text))
                AddImage(layout, m.Groups[1].Value, m.Groups[2].Value);

            // Only the first LCD page (SensorPanel files have no pages)
            var body = text;
            var p1 = text.IndexOf("<LCDPAGE1>", StringComparison.Ordinal);
            if (p1 >= 0)
            {
                var e1 = text.IndexOf("</LCDPAGE1>", p1, StringComparison.Ordinal);
                body = e1 > p1 ? text.Substring(p1, e1 - p1) : text.Substring(p1);
            }

            // Same fonts the renderer will use (skin + shared), so row heights match what is drawn.
            var fontFiles = layout.Fonts.Concat(LayoutLoader.SharedFontDirs.Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.ttf").Concat(Directory.GetFiles(d, "*.otf"))));
            using (var metrics = new FontMetrics(fontFiles))
            {
                var ctx = new Ctx(layout, map, dir, metrics);
                foreach (var raw in body.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.IndexOf("<ITMX>", StringComparison.Ordinal) < 0) continue;
                    var t = Tags(line);
                    if (!t.TryGetValue("ID", out var id) || id.StartsWith("-", StringComparison.Ordinal)) continue;
                    try { ctx.Convert(id, t); }
                    catch (Exception ex) { layout.Warnings.Add($"Item {id}: {ex.Message}"); }
                }
                if (ctx.Unmapped.Count > 0)
                    layout.Warnings.Add("No mapping for AIDA64 sensors: " + string.Join(", ", ctx.Unmapped.OrderBy(s => s)));
            }
            return layout;
        }

        /// <summary>Line heights (in px) for fonts as the renderer will resolve them.</summary>
        internal sealed class FontMetrics : IDisposable
        {
            private readonly System.Drawing.Text.PrivateFontCollection pfc = new System.Drawing.Text.PrivateFontCollection();
            private readonly Dictionary<string, float> cache = new Dictionary<string, float>();

            public FontMetrics(IEnumerable<string> files)
            {
                foreach (var f in files.Distinct(StringComparer.OrdinalIgnoreCase))
                    try { pfc.AddFontFile(f); } catch { }
            }

            public float LineHeight(string family, float sizePt)
            {
                var key = family + "|" + sizePt.ToString(CultureInfo.InvariantCulture);
                if (cache.TryGetValue(key, out var h)) return h;
                System.Drawing.FontFamily ff = pfc.Families.FirstOrDefault(p => string.Equals(p.Name, family, StringComparison.OrdinalIgnoreCase));
                bool owned = false;
                if (ff == null)
                {
                    try { ff = new System.Drawing.FontFamily(family); }
                    catch (ArgumentException) { ff = new System.Drawing.FontFamily("Segoe UI"); }
                    owned = true;
                }
                var style = ff.IsStyleAvailable(System.Drawing.FontStyle.Regular) ? System.Drawing.FontStyle.Regular : System.Drawing.FontStyle.Bold;
                float emPx = sizePt * 96f / 72f;
                h = emPx * ff.GetLineSpacing(style) / ff.GetEmHeight(style);
                if (owned) ff.Dispose();
                cache[key] = h;
                return h;
            }

            public void Dispose() => pfc.Dispose();
        }

        // ---------------------------------------------------------------- conversion

        private sealed class Ctx
        {
            private readonly PanelLayout layout;
            private readonly AidaSensorMap map;
            private readonly string dir;
            private readonly FontMetrics metrics;
            public readonly HashSet<string> Unmapped = new HashSet<string>();

            public Ctx(PanelLayout layout, AidaSensorMap map, string dir, FontMetrics metrics)
            {
                this.layout = layout;
                this.map = map;
                this.dir = dir;
                this.metrics = metrics;
            }

            public void Convert(string id, Dictionary<string, string> t)
            {
                float x = F(t, "ITMX"), y = F(t, "ITMY");
                if (id == "IMG") { Image(t, x, y); return; }
                if (id == "LBL") { Label(t, x, y); return; }

                var m = Regex.Match(id, @"^\[(\w+)\](.+)$");
                if (m.Success)
                {
                    var kind = m.Groups[1].Value.ToUpperInvariant();
                    var sid = m.Groups[2].Value;
                    switch (kind)
                    {
                        case "SIMPLE": Simple(sid, t, x, y); return;
                        case "GAUGE": Gauge(sid, t, x, y); return;
                        case "GRAPH": Graph(sid, t, x, y); return;
                        default: layout.Warnings.Add("Unsupported item type " + kind); return;
                    }
                }
                Sensor(id, t, x, y);
            }

            private void Image(Dictionary<string, string> t, float x, float y)
            {
                var file = S(t, "IMGFIL");
                if (string.IsNullOrEmpty(file)) return;
                var key = Path.GetFileName(file);
                if (t.TryGetValue("IMGDAT", out var hex) && hex.Length > 0) AddImage(layout, key, hex);
                else key = Path.IsPathRooted(file) ? file : Path.Combine(dir, file);
                layout.Elements.Add(new Element { Type = "image", X = x, Y = y, Path = key });
            }

            private void Label(Dictionary<string, string> t, float x, float y)
            {
                layout.Elements.Add(new Element
                {
                    Type = "text", X = x, Y = y,
                    Text = Vars(S(t, "LBL")),
                    Font = Font(t), Size = Size(t),
                    Color = AidaColor(S(t, "LBLCOL"), "#000000"),
                    Style = Style(S(t, "LBLBIS")),
                });
            }

            private void Simple(string sid, Dictionary<string, string> t, float x, float y)
            {
                var flags = S(t, "TXTBIR") ?? "000";
                var sb = new StringBuilder();
                if (S(t, "SHWLBL") == "1")
                {
                    // AIDA separates the label from the value with a space.
                    var lbl = S(t, "LBL") ?? "";
                    sb.Append(Escape(lbl));
                    if (lbl.Length > 0 && !char.IsWhiteSpace(lbl[lbl.Length - 1])) sb.Append(' ');
                }
                bool unit = S(t, "SHWUNT") == "1";
                sb.Append(ValueTemplate(sid, unit ? S(t, "UNT") : null));
                if (unit) sb.Append(Escape(S(t, "UNT")));
                layout.Elements.Add(new Element
                {
                    Type = "text", X = x, Y = y,
                    Text = sb.ToString(),
                    Font = Font(t), Size = Size(t),
                    Color = AidaColor(S(t, "TXTCOL"), "#FFFFFF"),
                    Style = Style(flags.Length >= 2 ? flags.Substring(0, 2) : flags),
                    // Third flag = right-aligned, with ITMX as the right edge.
                    Align = flags.Length >= 3 && flags[2] == '1' ? "right" : null,
                });
            }

            /// <summary>
            /// Classic sensor item, laid out the way AIDA64 draws it (calibrated against AIDA's own
            /// output): label left at x; value right-aligned so it ends at x+WID, minus UNTWID when
            /// a unit is shown, with the unit right after it. The row is the font's line height or,
            /// for embedded bars (BARPLC=EMB), at least BARHEI; text and bar are centred in it.
            /// A separate bar on an item without text sits at its top-left (x+BARIND, y).
            /// </summary>
            private void Sensor(string sid, Dictionary<string, string> t, float x, float y)
            {
                bool showLabel = S(t, "SHWLBL") == "1";
                bool showValue = S(t, "SHWVAL") == "1";
                bool showUnit = S(t, "SHWUNT") == "1";
                bool showBar = S(t, "SHWBAR") == "1";
                bool anyText = showLabel || showValue || showUnit;
                bool embedded = string.Equals(S(t, "BARPLC"), "EMB", StringComparison.OrdinalIgnoreCase);
                float wid = F(t, "WID", 100), unitWid = F(t, "UNTWID", 40);
                float bw = F(t, "BARWID", 100), bh = F(t, "BARHEI", 15), ind = F(t, "BARIND");
                float size = Size(t);
                var font = Font(t);
                float lineH = metrics.LineHeight(font, size);
                float rowH = embedded ? Math.Max(lineH, bh) : lineH;
                float valueRight = x + wid - (showUnit ? unitWid : 0);

                Element bar = null;
                if (showBar)
                {
                    EnsureSensor(sid);
                    float bx, by;
                    if (embedded) { bx = x + ind; by = y + (rowH - bh) / 2; }
                    else if (!anyText) { bx = x + ind; by = y; }
                    else { bx = x + wid + ind; by = y + (rowH - bh) / 2; }
                    var fs = S(t, "BARFS") ?? "0000";

                    bar = new Element
                    {
                        Type = "bar", X = bx, Y = by, W = bw, H = bh,
                        Sensor = sid,
                        Min = D(t, "BARMIN") ?? 0, Max = D(t, "BARMAX") ?? 100,
                        Color = AidaColor(S(t, "BARMINFGC"), "#00DF00"),
                        Bg = AidaColor(S(t, "BARMINBGC"), "#333333"),
                        Vertical = bh > bw,
                        Reverse = fs.Length >= 4 && fs[3] == '1',
                        FrameColor = fs.Length >= 1 && fs[0] == '1' ? AidaColor(S(t, "BARFRMCOL"), "#666666") : null,
                        Thresholds = BarThresholds(t),
                    };
                    if (embedded) layout.Elements.Add(bar);
                }

                if (showLabel)
                    layout.Elements.Add(new Element
                    {
                        Type = "text", X = x, Y = y, H = rowH, VAlign = "middle", Text = Escape(S(t, "LBL")),
                        Font = font, Size = size, Color = AidaColor(S(t, "LBLCOL"), "#FFFFFF"), Style = Style(S(t, "LBLBIS")),
                    });
                if (showValue)
                    layout.Elements.Add(new Element
                    {
                        Type = "text", X = valueRight, Y = y, H = rowH, VAlign = "middle", Align = "right", Text = ValueTemplate(sid, showUnit ? S(t, "UNT") : null),
                        Font = font, Size = size, Color = AidaColor(S(t, "VALCOL"), "#FFFFFF"), Style = Style(S(t, "VALBIS")),
                    });
                if (showUnit)
                    layout.Elements.Add(new Element
                    {
                        Type = "text", X = valueRight, Y = y, H = rowH, VAlign = "middle", Text = Escape(S(t, "UNT")),
                        Font = font, Size = size, Color = AidaColor(S(t, "UNTCOL"), "#FFFFFF"), Style = Style(S(t, "UNTBIS")),
                    });

                if (bar != null && !layout.Elements.Contains(bar)) layout.Elements.Add(bar);
            }

            private void Gauge(string sid, Dictionary<string, string> t, float x, float y)
            {
                EnsureSensor(sid);
                double min = D(t, "MINVAL") ?? 0, max = D(t, "MAXVAL") ?? 100;
                var frames = (S(t, "STAFLS") ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(f => FrameKey(f.Trim())).ToList();

                float gw, gh;
                if (string.Equals(S(t, "TYP"), "Custom", StringComparison.OrdinalIgnoreCase) && frames.Count > 0)
                {
                    var size = ImageSize(frames[0]);
                    gw = size.Width; gh = size.Height;
                    layout.Elements.Add(new Element { Type = "imagegauge", X = x, Y = y, W = gw, H = gh, Frames = frames, Sensor = sid, Min = min, Max = max });
                }
                else
                {
                    // AIDA's built-in gauge artwork isn't available; draw a ring of the nominal size.
                    var siz = (S(t, "SIZ") ?? "M").ToUpperInvariant();
                    gw = gh = siz == "S" ? 80 : siz == "L" ? 200 : 140;
                    layout.Elements.Add(new Element { Type = "gauge", X = x, Y = y, W = gw, Thickness = gw / 10, Start = 135, Sweep = 270, Sensor = sid, Min = min, Max = max, Color = "#FFFFFF", Bg = "#363636" });
                }

                if (S(t, "SHWVAL") == "1")
                {
                    float size = Size(t);
                    var bi = S(t, "VALBI") ?? "00";
                    layout.Elements.Add(new Element
                    {
                        Type = "text", X = x, Y = y, W = gw, H = gh, VAlign = "middle", Align = "center", Text = ValueTemplate(sid),
                        Font = Font(t), Size = size, Color = AidaColor(S(t, "VALCOL"), "#FFFFFF"), Style = Style(bi),
                    });
                }
            }

            private void Graph(string sid, Dictionary<string, string> t, float x, float y)
            {
                EnsureSensor(sid);
                float gw = F(t, "WID", 100), gh = F(t, "HEI", 50), step = Math.Max(1, F(t, "GPHSTP", 1));
                var flags = S(t, "GPHBFG") ?? "111";
                var typ = (S(t, "TYP") ?? "LG").ToUpperInvariant();
                layout.Elements.Add(new Element
                {
                    Type = "graph", X = x, Y = y, W = gw, H = gh,
                    Sensor = sid, Min = D(t, "MINVAL") ?? 0, Max = D(t, "MAXVAL") ?? 100,
                    Autoscale = S(t, "AUTSCL") == "1",
                    GraphType = typ == "AG" ? "area" : typ == "HG" ? "bars" : "line",
                    Step = step, Points = (int)(gw / step) + 2,
                    LineWidth = Math.Max(1, F(t, "GPHTCK", 1)),
                    Color = AidaColor(S(t, "GPHCOL"), "#FFFF00"),
                    Bg = flags.Length >= 1 && flags[0] == '1' ? AidaColor(S(t, "BGCOL"), "#000000") : null,
                    FrameColor = flags.Length >= 2 && flags[1] == '1' ? AidaColor(S(t, "FRMCOL"), "#666666") : null,
                    GridColor = flags.Length >= 3 && flags[2] == '1' ? AidaColor(S(t, "GRDCOL"), "#008000") : null,
                    GridSize = flags.Length >= 3 && flags[2] == '1' ? F(t, "GRDDNS", 10) : (float?)null,
                });
            }

            // ------------------------------------------------------------ helpers

            /// <summary>
            /// Template that renders the AIDA item's value text. <paramref name="unitText"/> is the
            /// unit the skin displays (when shown); AIDA converts e.g. MHz to "GHz" and MB to "GB".
            /// </summary>
            private string ValueTemplate(string sid, string unitText = null)
            {
                if (map?.Map != null && map.Map.TryGetValue(sid, out var entry))
                {
                    if (entry.Sensor != null)
                    {
                        EnsureSensor(sid);
                        var conv = UnitConversion(entry.Unit, unitText);
                        if (conv != null)
                        {
                            var key = sid + "@" + conv.Value.Unit;
                            if (!layout.Sensors.ContainsKey(key))
                            {
                                var b = Clone(entry.Sensor);
                                b.Scale = (b.Scale ?? 1) * conv.Value.Factor;
                                if (b.Demo.HasValue) b.Demo *= conv.Value.Factor;
                                layout.Sensors[key] = b;
                            }
                            return "{" + key + ":" + conv.Value.Format + "}";
                        }
                        return "{" + sid + ":" + (entry.Format ?? "0") + "}";
                    }
                    if (entry.Text != null)
                    {
                        foreach (Match m in Regex.Matches(entry.Text, @"\{(_[A-Za-z0-9_]+)"))
                            EnsureSensor(m.Groups[1].Value);
                        return entry.Text;
                    }
                }
                Unmapped.Add(sid);
                return "-";
            }

            private static (string Unit, double Factor, string Format)? UnitConversion(string native, string shown)
            {
                if (string.IsNullOrWhiteSpace(native) || string.IsNullOrWhiteSpace(shown)) return null;
                var n = native.Trim().ToLowerInvariant();
                var s = shown.Trim().ToLowerInvariant();
                if (s == n) return null;
                switch (n + ">" + s)
                {
                    case "mhz>ghz": return ("GHz", 0.001, "0.00");
                    case "mb>gb": return ("GB", 1 / 1024.0, "0.0");
                    case "mb>tb": return ("TB", 1 / 1048576.0, "0.00");
                    case "gb>tb": return ("TB", 1 / 1024.0, "0.00");
                    case "gb>mb": return ("MB", 1024, "0");
                    case "kb/s>mb/s": return ("MBps", 1 / 1024.0, "0.00");
                    case "kb/s>kbps": return ("Kbps", 8, "0");
                    case "kb/s>mbps": return ("Mbps", 8 / 1024.0, "0.0");
                    default: return null;
                }
            }

            private static SensorBinding Clone(SensorBinding b) => new SensorBinding
            {
                Name = b.Name, Source = b.Source, Unit = b.Unit, System = b.System, Aggregate = b.Aggregate,
                Index = b.Index, StaleMs = b.StaleMs, Scale = b.Scale, Demo = b.Demo,
            };

            private void EnsureSensor(string sid)
            {
                if (layout.Sensors.ContainsKey(sid)) return;
                if (map?.Map != null && map.Map.TryGetValue(sid, out var entry) && entry.Sensor != null)
                    layout.Sensors[sid] = entry.Sensor;
                else if (!(map?.Map?.ContainsKey(sid) ?? false))
                    Unmapped.Add(sid);
            }

            private List<Threshold> BarThresholds(Dictionary<string, string> t)
            {
                var list = new List<Threshold>();
                for (int i = 1; i <= 3; i++)
                {
                    var at = D(t, "BARLIM" + i);
                    if (!at.HasValue) continue;
                    list.Add(new Threshold
                    {
                        At = at.Value,
                        Color = AidaColor(S(t, $"BARLIM{i}FGC"), null),
                        Bg = AidaColor(S(t, $"BARLIM{i}BGC"), null),
                    });
                }
                return list.Count > 0 ? list : null;
            }

            /// <summary>Gauge frames may be embedded (by file name) or absolute paths on disk.</summary>
            private string FrameKey(string f)
            {
                var name = Path.GetFileName(f);
                if (layout.Images.ContainsKey(name)) return name;
                return Path.IsPathRooted(f) ? f : Path.Combine(dir, f);
            }

            private Size2 ImageSize(string key)
            {
                try
                {
                    byte[] bytes = layout.Images.TryGetValue(key, out var b) ? b : File.Exists(key) ? File.ReadAllBytes(key) : null;
                    if (bytes != null && bytes.Length > 24 && bytes[0] == 0x89 && bytes[1] == 0x50)
                        return new Size2(BigEndian(bytes, 16), BigEndian(bytes, 20));
                    if (bytes != null)
                        using (var ms = new MemoryStream(bytes))
                        using (var img = System.Drawing.Image.FromStream(ms))
                            return new Size2(img.Width, img.Height);
                }
                catch { }
                return new Size2(100, 100);
            }

            private static string Font(Dictionary<string, string> t)
            {
                var f = S(t, "FNTNAM");
                return string.IsNullOrWhiteSpace(f) ? "Tahoma" : f;
            }

            private static float Size(Dictionary<string, string> t) => F(t, "TXTSIZ", 8);
        }

        // ---------------------------------------------------------------- parsing utilities

        private struct Size2
        {
            public readonly float Width, Height;
            public Size2(float w, float h) { Width = w; Height = h; }
        }

        private static int BigEndian(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

        /// <summary>AIDA writes ANSI (cp1252), UTF-8 or UTF-16LE depending on version.</summary>
        internal static string ReadText(string path)
        {
            var b = File.ReadAllBytes(path);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            return Encoding.GetEncoding(1252).GetString(b);
        }

        private static Dictionary<string, string> Tags(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in TagRx.Matches(s))
                if (!d.ContainsKey(m.Groups[1].Value)) d[m.Groups[1].Value] = m.Groups[2].Value;
            return d;
        }

        private static void AddImage(PanelLayout layout, string name, string hex)
        {
            name = Path.GetFileName(name);
            if (string.IsNullOrEmpty(name) || layout.Images.ContainsKey(name) || hex.Length < 16) return;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)((Hex(hex[2 * i]) << 4) | Hex(hex[2 * i + 1]));
            layout.Images[name] = bytes;
        }

        private static int Hex(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

        private static string S(Dictionary<string, string> t, string k) => t.TryGetValue(k, out var v) ? v : null;

        private static float F(Dictionary<string, string> t, string k, float def = 0) =>
            t.TryGetValue(k, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;

        private static double? D(Dictionary<string, string> t, string k) =>
            t.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : (double?)null;

        /// <summary>AIDA colours are Delphi TColor integers: 0x00BBGGRR.</summary>
        internal static string AidaColor(string v, string fallback)
        {
            if (string.IsNullOrEmpty(v) || !long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c)) return fallback;
            int r = (int)(c & 0xFF), g = (int)((c >> 8) & 0xFF), b = (int)((c >> 16) & 0xFF);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        /// <summary>"BIS"-style flags: bold, italic, underline.</summary>
        private static string Style(string flags)
        {
            if (string.IsNullOrEmpty(flags)) return null;
            var parts = new List<string>();
            if (flags.Length > 0 && flags[0] == '1') parts.Add("bold");
            if (flags.Length > 1 && flags[1] == '1') parts.Add("italic");
            if (flags.Length > 2 && flags[2] == '1') parts.Add("underline");
            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }

        private static string Escape(string s) => (s ?? "").Replace("{", "{{").Replace("}", "}}");

        /// <summary>AIDA label variables to template built-ins.</summary>
        private static string Vars(string s)
        {
            s = Escape(s);
            return Regex.Replace(s, @"\$(CPUMODEL|GPU1MODEL|MOBOMODEL|LOCALIP)", m =>
                m.Groups[1].Value == "GPU1MODEL" ? "{GPUMODEL}" : "{" + m.Groups[1].Value + "}");
        }

        /// <summary>Fonts shipped with a skin (its folder and a Fonts subfolder).</summary>
        private static List<string> FindFonts(string dir)
        {
            var list = new List<string>();
            foreach (var d in new[] { dir, Path.Combine(dir, "Fonts"), Path.Combine(dir, "fonts") }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(d)) continue;
                list.AddRange(Directory.GetFiles(d, "*.ttf").Concat(Directory.GetFiles(d, "*.otf")));
            }
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
