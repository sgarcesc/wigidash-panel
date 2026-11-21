using WigiPanel.Layout;
using Xunit;

namespace WigiPanel.Tests
{
    /// <summary>
    /// WigiDash Manager adds every sensor to a static dictionary on each GetSensorList() call and
    /// copies that dictionary on every sensor update, so each rescan makes the Manager permanently
    /// slower. These tests pin how rarely the widget is allowed to rescan.
    /// </summary>
    public class RescanScheduleTests
    {
        [Fact]
        public void Never_due_while_every_binding_is_healthy()
        {
            var s = new RescanSchedule(0);
            Assert.False(s.IsDue(10_000, needsRescan: false));
            Assert.False(s.IsDue(24 * 3600 * 1000, needsRescan: false));
        }

        [Fact]
        public void First_retry_waits_the_minimum_delay()
        {
            var s = new RescanSchedule(0);
            Assert.False(s.IsDue(RescanSchedule.MinDelayMs - 1, needsRescan: true));
            Assert.True(s.IsDue(RescanSchedule.MinDelayMs, needsRescan: true));
        }

        [Fact]
        public void Fruitless_scans_double_the_delay()
        {
            var s = new RescanSchedule(0);
            s.Scanned(1000, improved: false);
            Assert.False(s.IsDue(1000 + 2 * RescanSchedule.MinDelayMs - 1, needsRescan: true));
            Assert.True(s.IsDue(1000 + 2 * RescanSchedule.MinDelayMs, needsRescan: true));
        }

        [Fact]
        public void Delay_is_capped()
        {
            var s = new RescanSchedule(0);
            int now = 0;
            for (int i = 0; i < 40; i++) s.Scanned(now += RescanSchedule.MaxDelayMs, improved: false);
            Assert.True(s.IsDue(now + RescanSchedule.MaxDelayMs, needsRescan: true));
        }

        [Fact]
        public void A_scan_that_changed_bindings_resets_the_delay()
        {
            var s = new RescanSchedule(0);
            int now = 0;
            for (int i = 0; i < 10; i++) s.Scanned(now += 100_000, improved: false);
            s.Scanned(now += 1000, improved: true);
            Assert.True(s.IsDue(now + RescanSchedule.MinDelayMs, needsRescan: true));
        }

        [Fact]
        public void A_healthy_period_resets_the_delay()
        {
            var s = new RescanSchedule(0);
            int now = 0;
            for (int i = 0; i < 10; i++) s.Scanned(now += 100_000, improved: false);
            Assert.False(s.IsDue(now + 500, needsRescan: false));
            Assert.True(s.IsDue(now + RescanSchedule.MinDelayMs, needsRescan: true));
        }

        [Fact]
        public void A_binding_that_never_resolves_costs_few_scans_per_day()
        {
            // The old code rescanned every 5 s: ~17,000 GetSensorList() calls a day.
            var s = new RescanSchedule(0);
            int scans = 0;
            for (int now = 1000; now <= 24 * 3600 * 1000; now += 1000)
            {
                if (!s.IsDue(now, needsRescan: true)) continue;
                s.Scanned(now, improved: false);
                scans++;
            }
            Assert.InRange(scans, 1, 70);
        }

        [Fact]
        public void Survives_tick_count_wraparound()
        {
            int start = int.MaxValue - 500;
            var s = new RescanSchedule(start);
            Assert.False(s.IsDue(unchecked(start + RescanSchedule.MinDelayMs - 1), needsRescan: true));
            Assert.True(s.IsDue(unchecked(start + RescanSchedule.MinDelayMs), needsRescan: true));
        }
    }
}
