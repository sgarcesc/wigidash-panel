using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WigiPanel.Layout;
using Xunit;

namespace WigiPanel.Tests
{
    /// <summary>
    /// The loop's water temperature probe is on the Aqua Computer Octo's "Temperature 2".
    /// Every native layout's "water" binding must read exactly that sensor.
    /// </summary>
    public class WaterBindingTests
    {
        [Fact]
        public void Every_water_binding_reads_octo_temperature_2()
        {
            var sensors = SensorDump.Load("aqua-octo.txt");
            var dir = Path.Combine(RepoRoot(), "layouts");
            var layouts = Directory.GetFiles(dir, "*.json")
                .Where(p => !Path.GetFileName(p).Equals(LayoutLoader.AidaMapFileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var checkedFiles = new List<string>();
            foreach (var path in layouts)
            {
                var sensorsInLayout = LayoutLoader.Parse(File.ReadAllText(path)).Sensors;
                if (sensorsInLayout == null || !sensorsInLayout.TryGetValue("water", out var water)) continue;
                var file = Path.GetFileName(path);
                checkedFiles.Add(file);

                var hits = SensorMatcher.Select(water, sensors, s => s.Name, s => s.Source, s => s.Unit);
                Assert.True(hits.Count == 1, $"{file}: water matched {hits.Count} sensors: {string.Join(", ", hits)}");
                Assert.Equal("Aqua Computer Octo", hits[0].Source);
                Assert.True(hits[0].Name == "Temperature 2", $"{file}: water reads '{hits[0].Name}'");
            }

            // Guards against silently checking nothing: the hand-written default and the generated designs.
            Assert.Contains("default.json", checkedFiles);
            Assert.True(checkedFiles.Count >= 9, "water bindings found in: " + string.Join(", ", checkedFiles));
        }

        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "layouts")) && Directory.Exists(Path.Combine(dir.FullName, "src")))
                    return dir.FullName;
            throw new DirectoryNotFoundException("Repository root (with layouts/) not found above " + AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}
