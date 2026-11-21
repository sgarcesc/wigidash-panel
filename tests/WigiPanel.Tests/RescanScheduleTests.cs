using System.Collections.Generic;
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
        private const int Sig = 12345;
        private const int Sensors = 461;

        [Fact]
        public void Never_due_while_every_binding_is_bound()
        {
            var s = new RescanSchedule(0, Sig);
            Assert.False(s.IsDue(10_000, anyUnbound: false));
            Assert.False(s.IsDue(24 * 3600 * 1000, anyUnbound: false));
        }

        [Fact]
        public void First_retry_waits_the_minimum_delay()
        {
            var s = new RescanSchedule(0, Sig);
            Assert.False(s.IsDue(RescanSchedule.MinDelayMs - 1, anyUnbound: true));
            Assert.True(s.IsDue(RescanSchedule.MinDelayMs, anyUnbound: true));
        }

        [Fact]
        public void Fruitless_scans_double_the_delay()
        {
            var s = new RescanSchedule(0, Sig);
            s.Scanned(1000, Sensors, Sig, bindingsChanged: false);
            Assert.False(s.IsDue(1000 + 2 * RescanSchedule.MinDelayMs - 1, anyUnbound: true));
            Assert.True(s.IsDue(1000 + 2 * RescanSchedule.MinDelayMs, anyUnbound: true));
        }

        [Fact]
        public void Delay_is_capped()
        {
            var s = new RescanSchedule(0, Sig);
            int now = 0;
            for (int i = 0; i < 40; i++) s.Scanned(now += RescanSchedule.MaxDelayMs, Sensors, Sig, bindingsChanged: false);
            Assert.True(s.IsDue(now + RescanSchedule.MaxDelayMs, anyUnbound: true));
        }

        [Fact]
        public void A_scan_that_changed_bindings_resets_the_delay()
        {
            var s = Backed_off(out int now);
            s.Scanned(now += 1000, Sensors, Sig, bindingsChanged: true);
            Assert.True(s.IsDue(now + RescanSchedule.MinDelayMs, anyUnbound: true));
        }

        [Fact]
        public void Bound_ticks_do_not_reset_the_delay()
        {
            // A binding that flaps between bound and unbound must not restart a burst of fetches.
            var s = Backed_off(out int now);
            Assert.False(s.IsDue(now + 500, anyUnbound: false));
            Assert.False(s.IsDue(now + RescanSchedule.MinDelayMs, anyUnbound: true));
        }

        [Fact]
        public void An_empty_sensor_list_is_retried_at_the_minimum_delay()
        {
            // HWiNFO not up yet. An empty list adds nothing to the Manager's dictionary, so
            // retrying it often is free, and the panel binds as soon as HWiNFO appears.
            var s = new RescanSchedule(0, RescanSchedule.ListSignature(0, new string[0]));
            int now = 0;
            for (int i = 0; i < 100; i++)
            {
                now += RescanSchedule.MinDelayMs;
                Assert.True(s.IsDue(now, anyUnbound: true));
                s.Scanned(now, 0, RescanSchedule.ListSignature(0, new string[0]), bindingsChanged: false);
            }
        }

        [Fact]
        public void A_sensor_list_that_changed_resets_the_delay()
        {
            // HWiNFO still adding sensors after start-up, or the Manager restarting its provider.
            var s = Backed_off(out int now);
            s.Scanned(now += 1000, Sensors + 20, Sig + 1, bindingsChanged: false);
            Assert.True(s.IsDue(now + RescanSchedule.MinDelayMs, anyUnbound: true));
        }

        [Fact]
        public void A_binding_that_never_resolves_costs_few_scans_per_day()
        {
            // The old code rescanned every 5 s: ~17,000 GetSensorList() calls a day.
            var s = new RescanSchedule(0, Sig);
            int scans = 0;
            for (int now = 1000; now <= 24 * 3600 * 1000; now += 1000)
            {
                if (!s.IsDue(now, anyUnbound: true)) continue;
                s.Scanned(now, Sensors, Sig, bindingsChanged: false);
                scans++;
            }
            Assert.InRange(scans, 1, 70);
        }

        [Fact]
        public void Survives_tick_count_wraparound()
        {
            int start = int.MaxValue - 500;
            var s = new RescanSchedule(start, Sig);
            Assert.False(s.IsDue(unchecked(start + RescanSchedule.MinDelayMs - 1), anyUnbound: true));
            Assert.True(s.IsDue(unchecked(start + RescanSchedule.MinDelayMs), anyUnbound: true));
        }

        [Fact]
        public void List_signature_tracks_count_and_sources_but_not_order()
        {
            var a = RescanSchedule.ListSignature(3, new[] { "CPU [#0]", "PresentMon [dwm.exe]", "CPU [#0]" });
            Assert.Equal(a, RescanSchedule.ListSignature(3, new[] { "PresentMon [dwm.exe]", "CPU [#0]" }));
            Assert.NotEqual(a, RescanSchedule.ListSignature(4, new[] { "CPU [#0]", "PresentMon [dwm.exe]" }));
            Assert.NotEqual(a, RescanSchedule.ListSignature(3, new[] { "CPU [#0]", "PresentMon [game.exe]" }));
        }

        [Fact]
        public void Bindings_changed_detects_added_removed_and_rematched_keys()
        {
            var before = new Dictionary<string, string[]> { ["fps"] = new[] { "a" }, ["cpu"] = new[] { "b" } };
            Assert.False(RescanSchedule.BindingsChanged(before, new Dictionary<string, string[]> { ["fps"] = new[] { "a" }, ["cpu"] = new[] { "b" } }));
            Assert.True(RescanSchedule.BindingsChanged(before, new Dictionary<string, string[]> { ["fps"] = new[] { "a" } }));
            Assert.True(RescanSchedule.BindingsChanged(before, new Dictionary<string, string[]> { ["fps"] = new[] { "c" }, ["cpu"] = new[] { "b" } }));
            Assert.True(RescanSchedule.BindingsChanged(before, new Dictionary<string, string[]> { ["fps"] = new[] { "a" }, ["gpu"] = new[] { "b" } }));
        }

        /// <summary>A schedule whose delay has grown well past the minimum.</summary>
        private static RescanSchedule Backed_off(out int now)
        {
            var s = new RescanSchedule(0, Sig);
            now = 0;
            for (int i = 0; i < 10; i++) s.Scanned(now += 100_000, Sensors, Sig, bindingsChanged: false);
            return s;
        }
    }
}
