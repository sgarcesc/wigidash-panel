using WigiPanel.Layout;

namespace WigiPanel.Rendering
{
    /// <summary>
    /// Routes bindings with "system" to <see cref="SystemSensors"/> and everything else to the
    /// inner source (live HWiNFO values on the device, demo values in the preview).
    /// </summary>
    public sealed class LayoutValueSource : IValueSource
    {
        private readonly PanelLayout layout;
        private readonly IValueSource inner;

        public LayoutValueSource(PanelLayout layout, IValueSource inner)
        {
            this.layout = layout;
            this.inner = inner;
        }

        public double? Get(string key)
        {
            if (layout?.Sensors != null && layout.Sensors.TryGetValue(key, out var b) && !string.IsNullOrEmpty(b.System))
                return SystemSensors.Get(b.System);
            return inner?.Get(key);
        }
    }
}
