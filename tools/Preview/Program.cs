using System;
using System.Drawing.Imaging;
using System.IO;
using WigiPanel.Layout;
using WigiPanel.Rendering;

namespace WigiPanel.Preview
{
    /// <summary>
    /// Renders a layout to PNG using each sensor binding's "demo" value, so layouts can be
    /// designed without WigiDash Manager or the device.
    ///
    ///   wigipanel-preview &lt;layout.json&gt; [out.png] [width height]
    ///
    /// Default size is 1016x592 (a 5x4 widget).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: wigipanel-preview <layout.json> [out.png] [width height]");
                return 2;
            }

            // Preview images get committed and shared: never render the real host name or IPs.
            SystemInfo.UsePlaceholders = true;

            var layoutPath = Path.GetFullPath(args[0]);
            var outPath = args.Length > 1 ? args[1] : Path.ChangeExtension(layoutPath, ".preview.png");
            int w = args.Length > 3 ? int.Parse(args[2]) : 1016;
            int h = args.Length > 3 ? int.Parse(args[3]) : 592;

            // Find the repo's layouts\aida-sensors.json (and fonts\) by walking up from the exe.
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var map = Path.Combine(dir.FullName, "layouts", LayoutLoader.AidaMapFileName);
                if (!File.Exists(map)) continue;
                LayoutLoader.AidaMapFallbacks.Add(map);
                LayoutLoader.SharedFontDirs.Add(Path.Combine(dir.FullName, "fonts"));
                break;
            }

            try
            {
                var layout = LayoutLoader.Load(layoutPath);
                foreach (var w2 in layout.Warnings ?? new System.Collections.Generic.List<string>())
                    Console.Error.WriteLine("warning: " + w2);
                using (var r = new PanelRenderer(layout, Path.GetDirectoryName(layoutPath)))
                {
                    // Warm graph history with a little jitter so graphs have something to show.
                    var jittery = new DemoValueSource(layout, jitter: 0.15);
                    for (int i = 0; i < 90; i++) r.Render(w, h, jittery).Dispose();

                    using (var bmp = r.Render(w, h, new DemoValueSource(layout)))
                        bmp.Save(outPath, ImageFormat.Png);
                }
                Console.WriteLine(outPath);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
