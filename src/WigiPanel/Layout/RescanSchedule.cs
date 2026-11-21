using System;
using System.Collections.Generic;
using System.Linq;

namespace WigiPanel.Layout
{
    /// <summary>
    /// Decides when the widget may ask WigiDash Manager for its sensor list again.
    /// </summary>
    /// <remarks>
    /// Every <c>IWidgetManager.GetSensorList()</c> call permanently adds one entry per sensor to a
    /// static dictionary inside the Manager, and the Manager copies that whole dictionary on every
    /// sensor update. Frequent rescans therefore make the Manager slower the longer it runs (GC
    /// thrash, late sensor values). The Manager only rebuilds the list when it (re)starts HWiNFO,
    /// so a rescan can only help while some binding is unmatched.
    ///
    /// Rules, all decided here so they are unit-tested:
    ///  - Rescan only while some binding is unmatched.
    ///  - An empty list costs the Manager nothing, so while it is empty (HWiNFO not up yet) retry
    ///    at the minimum delay.
    ///  - A scan that changed the bindings or the list itself (HWiNFO still filling it in) resets
    ///    the delay; any other scan doubles it, up to <see cref="MaxDelayMs"/>.
    /// All times are <see cref="Environment.TickCount"/> values and survive its wraparound.
    /// </remarks>
    public sealed class RescanSchedule
    {
        public const int MinDelayMs = 1000;
        public const int MaxDelayMs = 30 * 60 * 1000;

        private int lastScan;
        private int lastListSignature;
        private int delay = MinDelayMs;

        /// <param name="now">Tick count of the scan that just ran.</param>
        /// <param name="listSignature">Signature of the list that scan returned.</param>
        public RescanSchedule(int now, int listSignature)
        {
            lastScan = now;
            lastListSignature = listSignature;
        }

        /// <summary>Whether to rescan now.</summary>
        /// <param name="anyUnbound">Some binding matched no sensor on the last scan.</param>
        public bool IsDue(int now, bool anyUnbound) =>
            anyUnbound && unchecked(now - lastScan) >= delay;

        /// <summary>Records a rescan.</summary>
        /// <param name="sensorCount">Number of sensors the Manager returned.</param>
        /// <param name="listSignature">Signature of the returned list (see <see cref="ListSignature"/>).</param>
        /// <param name="bindingsChanged">The rescan changed which sensors the bindings read.</param>
        public void Scanned(int now, int sensorCount, int listSignature, bool bindingsChanged)
        {
            bool listChanged = listSignature != lastListSignature;
            lastScan = now;
            lastListSignature = listSignature;
            delay = sensorCount == 0 || bindingsChanged || listChanged
                ? MinDelayMs
                : Math.Min(delay * 2, MaxDelayMs);
        }

        /// <summary>Cheap fingerprint of a sensor list: its size and the set of sources.</summary>
        public static int ListSignature(int count, IEnumerable<string> sources)
        {
            int signature = count;
            foreach (var src in sources.Select(s => s ?? "").Distinct().OrderBy(s => s, StringComparer.Ordinal))
                signature = unchecked(signature * 31 + StableHash(src));
            return signature;
        }

        /// <summary>Whether two binding maps (key to sensor identities) differ.</summary>
        public static bool BindingsChanged(IReadOnlyDictionary<string, string[]> before, IReadOnlyDictionary<string, string[]> after) =>
            before.Count != after.Count
            || after.Any(kv => !before.TryGetValue(kv.Key, out var old) || !old.SequenceEqual(kv.Value));

        private static int StableHash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s) h = h * 31 + c;
                return h;
            }
        }
    }
}
