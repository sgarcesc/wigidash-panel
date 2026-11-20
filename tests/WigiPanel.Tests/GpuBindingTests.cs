using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WigiPanel.Layout;
using WigiPanel.Layout.Aida;
using Xunit;

namespace WigiPanel.Tests
{
    /// <summary>
    /// Runs every GPU binding in the shipped layouts against saved sensor lists, so a change in
    /// how HWiNFO names GPU sources shows up here instead of as a blank panel.
    /// </summary>
    public class GpuBindingTests
    {
        /// <summary>Fixture file and the source every GPU binding must read from.</summary>
        public static IEnumerable<object[]> Machines() => new[]
        {
            new object[] { "dgpu-igpu.txt", @"^dGPU \[#0\]" },
            new object[] { "legacy-names.txt", @"^GPU \[#0\]" },
            new object[] { "dual-dgpu.txt", @"^dGPU \[#0\]" },
        };

        [Theory]
        [MemberData(nameof(Machines))]
        public void Every_gpu_binding_reads_gpu_0(string fixture, string expectedSource)
        {
            var sensors = SensorDump.Load(fixture);
            var failures = new List<string>();

            foreach (var (layout, key, binding) in GpuBindings())
            {
                var hits = Select(binding, sensors);
                if (hits.Count == 0)
                    failures.Add($"{layout} {key}: unbound");
                foreach (var s in hits.Where(s => !Regex.IsMatch(s.Source, expectedSource)))
                    failures.Add($"{layout} {key}: reads '{s.Name}' from '{s.Source}'");
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }

        [Fact]
        public void Integrated_only_machine_still_gets_gpu_readings()
        {
            var sensors = SensorDump.Load("igpu-only.txt");
            var failures = new List<string>();

            foreach (var (layout, key, binding) in GpuBindings())
            {
                // An iGPU doesn't report everything a discrete card does (e.g. "GPU Power"),
                // so only the sensors it does have must bind.
                var hits = Select(binding, sensors);
                var exists = sensors.Any(s => SensorMatcher.IsMatch(binding.Name, s.Name) && SensorMatcher.IsMatch(binding.Unit, s.Unit));
                if (exists && hits.Count == 0)
                    failures.Add($"{layout} {key}: unbound although the iGPU reports it");
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }

        [Fact]
        public void Layouts_contain_gpu_bindings()
        {
            // Guards the other tests against silently checking nothing.
            var layouts = GpuBindings().Select(b => b.Layout).Distinct().ToList();
            Assert.Contains("default.json", layouts);
            Assert.Contains(LayoutLoader.AidaMapFileName, layouts);
            Assert.True(layouts.Count >= 10, "GPU bindings found in: " + string.Join(", ", layouts));
        }

        private static List<SensorDump.Sensor> Select(SensorBinding b, List<SensorDump.Sensor> sensors) =>
            SensorMatcher.Select(b, sensors, s => s.Name, s => s.Source, s => s.Unit);

        private static IEnumerable<(string Layout, string Key, SensorBinding Binding)> GpuBindings()
        {
            var dir = Path.Combine(RepoRoot(), "layouts");
            foreach (var path in Directory.GetFiles(dir, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var file = Path.GetFileName(path);
                IEnumerable<KeyValuePair<string, SensorBinding>> bindings;
                if (file.Equals(LayoutLoader.AidaMapFileName, StringComparison.OrdinalIgnoreCase))
                    bindings = (AidaSensorMap.Load(path).Map ?? new Dictionary<string, AidaMapEntry>())
                        .Where(kv => kv.Value?.Sensor != null)
                        .Select(kv => new KeyValuePair<string, SensorBinding>(kv.Key, kv.Value.Sensor));
                else
                    bindings = LayoutLoader.Parse(File.ReadAllText(path)).Sensors ?? new Dictionary<string, SensorBinding>();

                foreach (var kv in bindings.Where(kv => IsGpu(kv.Value)))
                    yield return (file, kv.Key, kv.Value);
            }
        }

        private static bool IsGpu(SensorBinding b) =>
            string.IsNullOrEmpty(b.System) &&
            ((b.Source ?? "").IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
             (b.Name ?? "").IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0);

        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "layouts")) && Directory.Exists(Path.Combine(dir.FullName, "src")))
                    return dir.FullName;
            throw new DirectoryNotFoundException("Repository root (with layouts/) not found above " + AppDomain.CurrentDomain.BaseDirectory);
        }
    }

    /// <summary>Reads the sensors.txt format the widget writes (see SensorHub.DumpSensors).</summary>
    internal static class SensorDump
    {
        internal sealed class Sensor
        {
            public string Source, Name, Unit;
            public override string ToString() => $"{Source} / {Name} ({Unit})";
        }

        private static readonly Regex Line = new Regex(@"^  (.+?)  \((.*?)\)\s+\[type=", RegexOptions.Compiled);

        public static List<Sensor> Load(string fixture)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", fixture);
            var list = new List<Sensor>();
            string source = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                if (raw.StartsWith("[") && raw.EndsWith("]")) { source = raw.Substring(1, raw.Length - 2); continue; }
                var m = Line.Match(raw);
                if (m.Success && source != null)
                    list.Add(new Sensor { Source = source, Name = m.Groups[1].Value.Trim(), Unit = m.Groups[2].Value });
            }
            Assert.NotEmpty(list);
            return list;
        }
    }
}
