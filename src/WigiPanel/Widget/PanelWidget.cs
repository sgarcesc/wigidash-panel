using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using WigiPanel.Layout;
using WigiPanel.Rendering;

namespace WigiPanel.Widget
{
    /// <summary>Widget definition discovered by WigiDash Manager (one per installed widget).</summary>
    public sealed class PanelWidget : IWidgetObject
    {
        public static readonly Guid WidgetGuid = new Guid("56507051-FDDD-447B-B59B-81D78C7CD961");

        /// <summary>Default layout shipped inside the widget folder.</summary>
        internal const string DefaultLayoutRelative = @"layouts\default.json";

        private readonly Dictionary<Guid, PanelInstance> instances = new Dictionary<Guid, PanelInstance>();
        private Bitmap thumbnail;

        // ---------------------------------------------------------------- IWidgetBase
        public Guid Guid => WidgetGuid;
        public string Name => "Sensor Panel";
        public string Author => "sgarcesc";
        public string Website => "https://github.com/sgarcesc/wigidash-panel";
        public string Description => "Free-form sensor panel drawn from a JSON layout. Place it at 5x4 for a full-screen dashboard.";
        public Version Version => typeof(PanelWidget).Assembly.GetName().Version;
        public SdkVersion TargetSdk => SdkVersion.Version_1;

        public List<WidgetSize> SupportedSizes { get; } = BuildSizes();

        public Bitmap PreviewImage => GetWidgetPreview(new WidgetSize(5, 4));

        // ---------------------------------------------------------------- IWidgetObject
        public IWidgetManager WidgetManager { get; set; }
        public string LastErrorMessage { get; set; }

        /// <summary>Folder the Manager loaded the widget from (holds the DLL, layouts and sensors.txt).</summary>
        internal string ResourcePath { get; private set; }

        public Bitmap WidgetThumbnail
        {
            get
            {
                if (thumbnail == null)
                {
                    var thumbFile = Path.Combine(ResourcePath ?? "", "thumb.png");
                    thumbnail = File.Exists(thumbFile) ? new Bitmap(thumbFile) : RenderDemo(200, 145);
                }
                return thumbnail;
            }
        }

        public WidgetError Load(string resourcePath)
        {
            ResourcePath = resourcePath;
            // AIDA64 skins fall back to the widget's sensor map; fonts dropped in <widget>\fonts load for every layout.
            LayoutLoader.AidaMapFallbacks.Add(Path.Combine(resourcePath ?? "", "layouts", LayoutLoader.AidaMapFileName));
            LayoutLoader.SharedFontDirs.Add(Path.Combine(resourcePath ?? "", "fonts"));
            return WidgetError.NO_ERROR;
        }

        public WidgetError Unload()
        {
            foreach (var i in instances.Values.ToList()) i.Dispose();
            instances.Clear();
            thumbnail?.Dispose();
            thumbnail = null;
            return WidgetError.NO_ERROR;
        }

        public Bitmap GetWidgetPreview(WidgetSize widgetSize)
        {
            var px = PixelSize(widgetSize);
            return RenderDemo(px.Width, px.Height);
        }

        public IWidgetInstance CreateWidgetInstance(WidgetSize widgetSize, Guid instanceGuid)
        {
            var instance = new PanelInstance(this, widgetSize, instanceGuid);
            instances[instanceGuid] = instance;
            return instance;
        }

        public bool RemoveWidgetInstance(Guid instanceGuid)
        {
            if (!instances.TryGetValue(instanceGuid, out var instance)) return false;
            instance.Dispose();
            instances.Remove(instanceGuid);
            return true;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Grid units are 200x145 px with 4 px gutters; 5x4 = 1016x592.</summary>
        internal static Size PixelSize(WidgetSize s) =>
            new Size(s.Width * 200 + (s.Width - 1) * 4, s.Height * 145 + (s.Height - 1) * 4);

        internal string DefaultLayoutPath => Path.Combine(ResourcePath ?? "", DefaultLayoutRelative);

        private Bitmap RenderDemo(int w, int h)
        {
            try
            {
                var layout = LayoutLoader.Load(DefaultLayoutPath);
                using (var r = new PanelRenderer(layout, Path.GetDirectoryName(DefaultLayoutPath)))
                    return r.Render(w, h, new DemoValueSource(layout));
            }
            catch
            {
                var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp))
                using (var f = new Font("Segoe UI", 14f))
                {
                    g.Clear(Color.FromArgb(16, 16, 16));
                    g.DrawString("Sensor Panel", f, Brushes.White, 8, 8);
                }
                return bmp;
            }
        }

        private static List<WidgetSize> BuildSizes()
        {
            var list = new List<WidgetSize>();
            for (int h = 1; h <= 4; h++)
                for (int w = 1; w <= 5; w++)
                    list.Add(new WidgetSize(w, h));
            return list;
        }
    }
}
