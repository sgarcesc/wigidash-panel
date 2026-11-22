using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WigiPanel.Layout;
using WigiPanel.Layout.Aida;
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

        [Fact]
        public void Aida_water_id_reads_octo_temperature_2()
        {
            // The original AIDA64 panel showed water from "Temperature #4" (TTEMP4). AIDA numbers the
            // Octo's channels after the motherboard's two, so its #4 is HWiNFO's Temperature 2.
            var sensors = SensorDump.Load("aqua-octo.txt");
            var map = AidaSensorMap.Load(Path.Combine(RepoRoot(), "layouts", LayoutLoader.AidaMapFileName));
            var hits = SensorMatcher.Select(map.Map["TTEMP4"].Sensor, sensors, s => s.Name, s => s.Source, s => s.Unit);
            Assert.True(hits.Count == 1, $"TTEMP4 matched {hits.Count} sensors: {string.Join(", ", hits)}");
            Assert.Equal("Aqua Computer Octo", hits[0].Source);
            Assert.Equal("Temperature 2", hits[0].Name);
        }

        [Theory]
        // The original AIDA64 panel showed the pump (the Octo's Fan 4) from "Fan #23". AIDA numbers fans
        // in one sequence, so the Octo's Fan n is AIDA's #(19 + n).
        [InlineData("FFAN21", "Fan 2")]
        [InlineData("FFAN23", "Fan 4")]
        [InlineData("FFAN24", "Fan 5")]
        [InlineData("FAIOPUMP", "Fan 4")]
        public void Aida_fan_ids_follow_the_octo_numbering(string aidaId, string octoFan)
        {
            var sensors = SensorDump.Load("aqua-octo.txt");
            var map = AidaSensorMap.Load(Path.Combine(RepoRoot(), "layouts", LayoutLoader.AidaMapFileName));
            var hits = SensorMatcher.Select(map.Map[aidaId].Sensor, sensors, s => s.Name, s => s.Source, s => s.Unit);
            Assert.True(hits.Count == 1, $"{aidaId} matched {hits.Count} sensors: {string.Join(", ", hits)}");
            Assert.Equal("Aqua Computer Octo", hits[0].Source);
            Assert.Equal(octoFan, hits[0].Name);
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
