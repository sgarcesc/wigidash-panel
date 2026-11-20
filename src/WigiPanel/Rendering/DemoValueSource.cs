using System;
using WigiPanel.Layout;

namespace WigiPanel.Rendering
{
    /// <summary>Feeds each binding's "demo" value (with optional jitter) for previews.</summary>
    public sealed class DemoValueSource : IValueSource
    {
        private readonly PanelLayout layout;
        private readonly Random rng;
        private readonly double jitter;

        public DemoValueSource(PanelLayout layout, double jitter = 0, int seed = 1)
        {
            this.layout = layout;
            this.jitter = jitter;
            rng = new Random(seed);
        }

        public double? Get(string sensorKey)
        {
            if (layout.Sensors == null || !layout.Sensors.TryGetValue(sensorKey, out var b) || !b.Demo.HasValue)
                return null;
            var v = b.Demo.Value;
            if (jitter > 0) v *= 1 + (rng.NextDouble() * 2 - 1) * jitter;
            // Demo values are written in display units; undo the scale the renderer will apply.
            return b.Scale.HasValue && b.Scale.Value != 0 ? v / b.Scale.Value : v;
        }
    }
}
