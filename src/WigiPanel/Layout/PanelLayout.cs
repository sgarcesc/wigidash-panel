using System.Collections.Generic;
using System.Runtime.Serialization;

namespace WigiPanel.Layout
{
    /// <summary>Root of a layout JSON file (also the in-memory result of importing an AIDA64 skin).</summary>
    [DataContract]
    public sealed class PanelLayout
    {
        [DataMember(Name = "name")] public string Name { get; set; }

        [DataMember(Name = "canvas")] public CanvasSpec Canvas { get; set; }

        /// <summary>How often the panel is redrawn, in milliseconds (default 1000).</summary>
        [DataMember(Name = "refreshMs")] public int? RefreshMs { get; set; }

        /// <summary>Optional PNG path (relative to the layout) the live frame is written to every ~10 s, for debugging.</summary>
        [DataMember(Name = "snapshot")] public string Snapshot { get; set; }

        /// <summary>Optional .ttf/.otf files (relative to the layout file) loaded privately.</summary>
        [DataMember(Name = "fonts")] public List<string> Fonts { get; set; }

        /// <summary>Static text variables usable as {NAME}; override built-ins such as CPUMODEL.</summary>
        [DataMember(Name = "vars")] public Dictionary<string, string> Vars { get; set; }

        /// <summary>Named sensor bindings referenced by elements and text templates.</summary>
        [DataMember(Name = "sensors")] public Dictionary<string, SensorBinding> Sensors { get; set; }

        /// <summary>Drawn in order; later elements paint over earlier ones.</summary>
        [DataMember(Name = "elements")] public List<Element> Elements { get; set; }

        /// <summary>Images embedded in an imported skin, keyed by file name. Checked before disk paths.</summary>
        [IgnoreDataMember] public Dictionary<string, byte[]> Images { get; set; }

        /// <summary>Import diagnostics (e.g. AIDA64 sensor IDs with no mapping), shown in settings.</summary>
        [IgnoreDataMember] public List<string> Warnings { get; set; }
    }

    [DataContract]
    public sealed class CanvasSpec
    {
        [DataMember(Name = "width")] public int Width { get; set; } = 1024;
        [DataMember(Name = "height")] public int Height { get; set; } = 600;
        [DataMember(Name = "background")] public string Background { get; set; }
    }

    /// <summary>
    /// Selects one sensor from the WigiDash Manager sensor list. Name/Source/Unit are
    /// case-insensitive regular expressions; all given ones must match.
    /// </summary>
    [DataContract]
    public sealed class SensorBinding
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "source")] public string Source { get; set; }
        [DataMember(Name = "unit")] public string Unit { get; set; }

        /// <summary>
        /// A value computed locally instead of from HWiNFO, e.g. "drive.C.usedpct",
        /// "drive.C.usedgb", "volume", "refresh", "uptime". See SystemSensors.
        /// </summary>
        [DataMember(Name = "system")] public string System { get; set; }

        /// <summary>
        /// Combine every matching sensor instead of using the first: avg, max, min or sum
        /// (e.g. name "^Core \d+ Clock$" with "avg").
        /// </summary>
        [DataMember(Name = "aggregate")] public string Aggregate { get; set; }

        /// <summary>Use the Nth match (0-based) instead of the first, for sensors that share a name.</summary>
        [DataMember(Name = "index")] public int? Index { get; set; }

        /// <summary>
        /// Treat the value as missing if the sensor hasn't reported for this many milliseconds.
        /// Useful for transient sensors such as PresentMon framerate.
        /// </summary>
        [DataMember(Name = "staleMs")] public int? StaleMs { get; set; }

        /// <summary>Multiplier applied to the raw value (e.g. 0.001 for MHz to GHz).</summary>
        [DataMember(Name = "scale")] public double? Scale { get; set; }

        /// <summary>Value used by the offline preview renderer.</summary>
        [DataMember(Name = "demo")] public double? Demo { get; set; }
    }

    /// <summary>
    /// One drawable item. A single flat type keeps the JSON simple; which fields matter
    /// depends on <see cref="Type"/>: rect, text, bar, gauge, imagegauge, graph, image.
    /// </summary>
    [DataContract]
    public sealed class Element
    {
        [DataMember(Name = "type")] public string Type { get; set; }

        [DataMember(Name = "x")] public float X { get; set; }
        [DataMember(Name = "y")] public float Y { get; set; }
        [DataMember(Name = "w")] public float? W { get; set; }
        [DataMember(Name = "h")] public float? H { get; set; }

        /// <summary>Primary colour: fill, text or gauge/bar foreground. #RGB, #RRGGBB or #AARRGGBB.</summary>
        [DataMember(Name = "color")] public string Color { get; set; }
        /// <summary>Track/background colour for bars, gauges and graphs.</summary>
        [DataMember(Name = "bg")] public string Bg { get; set; }
        /// <summary>Outline colour for bars and graphs (none when empty).</summary>
        [DataMember(Name = "frameColor")] public string FrameColor { get; set; }
        [DataMember(Name = "radius")] public float? Radius { get; set; }

        // text
        [DataMember(Name = "text")] public string Text { get; set; }
        [DataMember(Name = "font")] public string Font { get; set; }
        [DataMember(Name = "size")] public float? Size { get; set; }
        /// <summary>Any of bold, italic, underline, strikeout separated by spaces (or "bolditalic").</summary>
        [DataMember(Name = "style")] public string Style { get; set; }
        /// <summary>left, center, right (within x..x+w; without w, x is the anchor)</summary>
        [DataMember(Name = "align")] public string Align { get; set; }
        /// <summary>top (default), middle or bottom: places the text line within y..y+h.</summary>
        [DataMember(Name = "vAlign")] public string VAlign { get; set; }

        // sensor-driven
        [DataMember(Name = "sensor")] public string Sensor { get; set; }
        [DataMember(Name = "min")] public double? Min { get; set; }
        [DataMember(Name = "max")] public double? Max { get; set; }
        /// <summary>Foreground (and optionally background) colour changes once the value reaches each threshold.</summary>
        [DataMember(Name = "thresholds")] public List<Threshold> Thresholds { get; set; }

        // bar
        [DataMember(Name = "vertical")] public bool? Vertical { get; set; }
        /// <summary>Fill from the right (or top, when vertical) instead of the left/bottom.</summary>
        [DataMember(Name = "reverse")] public bool? Reverse { get; set; }

        // gauge
        /// <summary>Start angle in degrees, 0 = 3 o'clock, increasing clockwise.</summary>
        [DataMember(Name = "start")] public float? Start { get; set; }
        [DataMember(Name = "sweep")] public float? Sweep { get; set; }
        [DataMember(Name = "thickness")] public float? Thickness { get; set; }
        /// <summary>Quantise the fill into N steps (AIDA image gauges use 16). 0 = smooth.</summary>
        [DataMember(Name = "steps")] public int? Steps { get; set; }
        [DataMember(Name = "roundCaps")] public bool? RoundCaps { get; set; }

        // imagegauge: one image per state, chosen by value between min and max
        [DataMember(Name = "frames")] public List<string> Frames { get; set; }

        // graph
        /// <summary>Number of samples kept for the history graph.</summary>
        [DataMember(Name = "points")] public int? Points { get; set; }
        [DataMember(Name = "fill")] public bool? Fill { get; set; }
        /// <summary>line, area or bars (default: area unless fill is false).</summary>
        [DataMember(Name = "graphType")] public string GraphType { get; set; }
        /// <summary>Pixels between samples (default: spread "points" across the width).</summary>
        [DataMember(Name = "step")] public float? Step { get; set; }
        [DataMember(Name = "lineWidth")] public float? LineWidth { get; set; }
        [DataMember(Name = "gridColor")] public string GridColor { get; set; }
        /// <summary>Grid spacing in pixels (no grid when 0 or unset).</summary>
        [DataMember(Name = "gridSize")] public float? GridSize { get; set; }
        /// <summary>Scale the graph to the largest visible sample instead of max.</summary>
        [DataMember(Name = "autoscale")] public bool? Autoscale { get; set; }

        // image
        [DataMember(Name = "path")] public string Path { get; set; }

        /// <summary>
        /// Template; the element is drawn only when it expands to something non-empty
        /// (e.g. "{LOCALIP6}" hides an IPv6 row on machines without IPv6).
        /// </summary>
        [DataMember(Name = "showIf")] public string ShowIf { get; set; }

        /// <summary>Set to false to keep an element in the file without drawing it.</summary>
        [DataMember(Name = "visible")] public bool? Visible { get; set; }
    }

    [DataContract]
    public sealed class Threshold
    {
        [DataMember(Name = "at")] public double At { get; set; }
        [DataMember(Name = "color")] public string Color { get; set; }
        [DataMember(Name = "bg")] public string Bg { get; set; }
    }
}
