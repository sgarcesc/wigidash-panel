using System;

namespace WigiPanel.Layout
{
    /// <summary>
    /// Decides when the widget may ask WigiDash Manager for its sensor list again.
    /// </summary>
    /// <remarks>
    /// Every <c>IWidgetManager.GetSensorList()</c> call permanently adds one entry per sensor to a
    /// static dictionary inside the Manager, and the Manager copies that whole dictionary on every
    /// sensor update. Frequent rescans therefore make the Manager slower the longer it runs (GC
    /// thrash, late sensor values). The Manager also only reads the list when it starts HWiNFO, so
    /// rescanning rarely loses nothing. Retries back off exponentially while they find nothing new.
    /// All times are <see cref="Environment.TickCount"/> values and survive its wraparound.
    /// </remarks>
    public sealed class RescanSchedule
    {
        public const int MinDelayMs = 1000;
        public const int MaxDelayMs = 30 * 60 * 1000;

        private int lastScan;
        private int delay = MinDelayMs;

        /// <param name="now">Tick count of the scan that just ran.</param>
        public RescanSchedule(int now)
        {
            lastScan = now;
        }

        /// <summary>Whether to rescan now.</summary>
        /// <param name="needsRescan">Some binding is unmatched or has stopped reporting.</param>
        public bool IsDue(int now, bool needsRescan)
        {
            if (!needsRescan)
            {
                // Healthy again: the next problem gets a fast first retry.
                delay = MinDelayMs;
                return false;
            }
            return unchecked(now - lastScan) >= delay;
        }

        /// <summary>Records a rescan.</summary>
        /// <param name="improved">The rescan changed which sensors the bindings read.</param>
        public void Scanned(int now, bool improved)
        {
            lastScan = now;
            delay = improved ? MinDelayMs : Math.Min(delay * 2, MaxDelayMs);
        }
    }
}
