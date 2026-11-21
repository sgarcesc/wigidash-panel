using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using WigiDashWidgetFramework;
using WigiPanel.Layout;
using WigiPanel.Rendering;

namespace WigiPanel.Widget
{
    /// <summary>
    /// Resolves the layout's sensor bindings against the WigiDash Manager sensor list
    /// (HWiNFO-backed), subscribes to them and caches the latest values.
    /// </summary>
    /// <remarks>
    /// Threading rules (the Manager raises <see cref="IWidgetManager.SensorUpdated"/> on its own
    /// thread, possibly while holding internal locks):
    ///  - <see cref="OnSensorUpdated"/> takes no locks at all; it reads an immutable snapshot.
    ///  - <see cref="bindGate"/> serialises binding changes only and is never taken by callbacks.
    ///  - Binding work must not run on the UI thread (the Manager may marshal to it).
    /// </remarks>
    internal sealed class SensorHub : IValueSource, IDisposable
    {
        private readonly IWidgetManager manager;
        private readonly object bindGate = new object();
        // latest raw value per sensor identity
        private readonly ConcurrentDictionary<string, double> values = new ConcurrentDictionary<string, double>();
        // Environment.TickCount of each sensor's last report
        private readonly ConcurrentDictionary<string, int> lastSeen = new ConcurrentDictionary<string, int>();

        // binding key -> sensor identities it reads; replaced wholesale (copy-on-write) so readers need no lock
        private volatile Dictionary<string, string[]> itemsByKey = new Dictionary<string, string[]>();
        // identities of every monitored sensor (same copy-on-write rule)
        private volatile HashSet<string> monitoredIds = new HashSet<string>();
        private readonly List<SensorItem> monitored = new List<SensorItem>();
        private volatile Dictionary<string, SensorBinding> bindings = new Dictionary<string, SensorBinding>();
        // Signature of the last sensor list written to SensorDumpPath (-1 forces a re-dump).
        private int dumpedSignature = -1;
        // Size and signature of the last sensor list fetched. Guarded by bindGate.
        private int listCount;
        private int listSignature;
        // Guarded by bindGate. See RescanSchedule for why GetSensorList() must be called rarely.
        private RescanSchedule rescan = new RescanSchedule(Environment.TickCount, 0);
        private bool subscribed;
        private volatile bool disposed;

        public SensorHub(IWidgetManager manager)
        {
            this.manager = manager;
        }

        /// <summary>Binding keys that did not match any sensor on the last attempt.</summary>
        public IReadOnlyList<string> Unbound { get; private set; } = new string[0];

        public string SensorDumpPath { get; set; }

        public void SetBindings(Dictionary<string, SensorBinding> newBindings)
        {
            lock (bindGate)
            {
                if (disposed) return;
                ReleaseAll();
                bindings = newBindings ?? new Dictionary<string, SensorBinding>();
                dumpedSignature = -1;
                BindCore();
                rescan = new RescanSchedule(Environment.TickCount, listSignature);
            }
        }

        /// <summary>
        /// Re-matches bindings while some key is unbound (the Manager's list can be empty or
        /// incomplete while HWiNFO initialises), as often as <see cref="RescanSchedule"/> allows.
        /// A bound key that goes silent is left alone: the Manager's list is a snapshot, so a
        /// rescan could not re-match it. Cheap when there is nothing to do.
        /// Never blocks: if another thread is binding, this attempt is skipped.
        /// </summary>
        public void MaybeRescan()
        {
            if (Unbound.Count == 0) return;
            if (!Monitor.TryEnter(bindGate)) return;
            try
            {
                if (disposed || !rescan.IsDue(Environment.TickCount, Unbound.Count > 0)) return;
                bool changed = BindCore();
                rescan.Scanned(Environment.TickCount, listCount, listSignature, changed);
            }
            finally
            {
                Monitor.Exit(bindGate);
            }
        }

        public double? Get(string key)
        {
            if (!itemsByKey.TryGetValue(key, out var ids)) return null;
            bindings.TryGetValue(key, out var b);
            int staleMs = b?.StaleMs ?? 0;
            int now = Environment.TickCount;

            var got = new List<double>(ids.Length);
            foreach (var id in ids)
            {
                if (!values.TryGetValue(id, out var v)) continue;
                if (staleMs > 0 && (!lastSeen.TryGetValue(id, out var seen) || unchecked(now - seen) > staleMs)) continue;
                got.Add(v);
            }
            if (got.Count == 0) return null;
            if (got.Count == 1) return got[0];

            switch ((b?.Aggregate ?? "avg").ToLowerInvariant())
            {
                case "max": return got.Max();
                case "min": return got.Min();
                case "sum": return got.Sum();
                default: return got.Average();
            }
        }

        /// <summary>Returns whether any binding now reads different sensors than before.</summary>
        private bool BindCore()
        {
            if (manager == null) return false;
            List<SensorItem> all;
            try { all = manager.GetSensorList() ?? new List<SensorItem>(); }
            catch { return false; }
            var before = itemsByKey;

            // Re-dump when the set of sensor sources changes, not just the count
            // (switching foreground apps renames the PresentMon source without changing the count).
            listCount = all.Count;
            listSignature = RescanSchedule.ListSignature(all.Count, all.Select(s => s.Source));
            if (listSignature != dumpedSignature)
            {
                dumpedSignature = listSignature;
                DumpSensors(all);
            }

            var byKey = new Dictionary<string, string[]>(itemsByKey);
            var ids = new HashSet<string>(monitoredIds);

            // Sensors can disappear (e.g. PresentMon's per-app source changes with the foreground
            // app). Drop bindings that point at vanished sensors so they get matched again below.
            var present = new HashSet<string>(all.Select(Identity));
            foreach (var key in byKey.Keys.ToList())
                if (byKey[key].Any(id => !present.Contains(id))) byKey.Remove(key);
            var stillUsed = new HashSet<string>(byKey.Values.SelectMany(v => v));
            foreach (var gone in ids.Where(id => !present.Contains(id) && !stillUsed.Contains(id)).ToList())
            {
                ids.Remove(gone);
                values.TryRemove(gone, out _);
                lastSeen.TryRemove(gone, out _);
                var item = monitored.FirstOrDefault(m => Identity(m) == gone);
                if (item != null)
                {
                    monitored.Remove(item);
                    try { manager.RemoveMonitoringItem(item); } catch { }
                }
            }

            var unbound = new List<string>();
            foreach (var kv in bindings)
            {
                if (byKey.ContainsKey(kv.Key)) continue;
                if (!string.IsNullOrEmpty(kv.Value.System)) continue; // computed locally, see SystemSensors
                var matches = SensorMatcher.Select(kv.Value, all, s => s.Name, s => s.Source, s => s.Unit);
                if (matches.Count == 0) { unbound.Add(kv.Key); continue; }

                foreach (var item in matches)
                {
                    var id = Identity(item);
                    if (ids.Add(id))
                    {
                        try { manager.AddMonitoringItem(item); monitored.Add(item); }
                        catch { /* keep the binding; values just won't arrive */ }
                    }
                }
                byKey[kv.Key] = matches.Select(Identity).Distinct().ToArray();
            }

            monitoredIds = ids;
            itemsByKey = byKey;
            Unbound = unbound;

            if (!subscribed && ids.Count > 0)
            {
                manager.SensorUpdated += OnSensorUpdated;
                subscribed = true;
            }

            return RescanSchedule.BindingsChanged(before, byKey);
        }

        private void OnSensorUpdated(SensorItem item, double value)
        {
            if (item == null || disposed) return;
            var id = Identity(item);
            if (!monitoredIds.Contains(id)) return;
            values[id] = value;
            lastSeen[id] = Environment.TickCount;
        }

        private static string Identity(SensorItem s) =>
            s.Guid.ToString("N") + "|" + s.ReadingType + "|" + s.SensorId1 + "|" + s.SensorId2 + "|" + s.Name;

        /// <summary>Writes every available sensor to a text file so bindings can be written against real names.</summary>
        private void DumpSensors(List<SensorItem> all)
        {
            if (string.IsNullOrEmpty(SensorDumpPath)) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# WigiDash Manager sensor list, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("# Use these in layout \"sensors\" bindings: name / source / unit are regular expressions.");
                sb.AppendLine("# Names are trimmed before matching. Escape regex characters, e.g. \"^GPU Core Load$\".");
                sb.AppendLine();
                foreach (var group in all.GroupBy(s => s.Source ?? ""))
                {
                    sb.AppendLine("[" + group.Key + "]");
                    foreach (var s in group)
                        sb.AppendLine($"  {(s.Name ?? "").Trim()}  ({s.Unit})    [type={s.ReadingType} id={s.SensorId1}/{s.SensorId2}]");
                    sb.AppendLine();
                }
                File.WriteAllText(SensorDumpPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch
            {
                // Diagnostics only.
            }
        }

        private void ReleaseAll()
        {
            if (subscribed && manager != null)
            {
                try { manager.SensorUpdated -= OnSensorUpdated; } catch { }
                subscribed = false;
            }
            foreach (var item in monitored)
            {
                try { manager?.RemoveMonitoringItem(item); } catch { }
            }
            monitored.Clear();
            itemsByKey = new Dictionary<string, string[]>();
            monitoredIds = new HashSet<string>();
            values.Clear();
            lastSeen.Clear();
        }

        public void Dispose()
        {
            disposed = true;
            // Dispose may be called on the UI thread; don't wait on a binding pass that might itself
            // be waiting on the UI thread. If we can't get the gate quickly, the in-flight pass sees
            // 'disposed' and the Manager drops the subscriptions when the instance goes away.
            if (!Monitor.TryEnter(bindGate, 2000)) return;
            try { ReleaseAll(); }
            finally { Monitor.Exit(bindGate); }
        }
    }
}
