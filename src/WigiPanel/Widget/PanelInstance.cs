using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Controls;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using WigiPanel.Layout;
using WigiPanel.Rendering;

namespace WigiPanel.Widget
{
    /// <summary>One placed copy of the widget on a WigiDash page.</summary>
    /// <remarks>
    /// Threading: the Manager calls in on its UI thread and raises sensor events on other threads.
    /// Layout reloads and sensor binding always run on pool threads, rendering never calls into
    /// the Manager while holding a lock, and overlapping ticks are skipped rather than queued.
    /// </remarks>
    public sealed class PanelInstance : IWidgetInstance
    {
        internal const string LayoutPathSetting = "LayoutPath";

        private readonly PanelWidget parent;
        private readonly Size pixelSize;
        private readonly SensorHub sensors;
        private readonly object renderGate = new object();
        private readonly object reloadGate = new object();
        private readonly Timer timer;
        private readonly Timer reloadDebounce;

        private PanelRenderer renderer;
        private FileSystemWatcher watcher;
        private volatile string layoutError;
        private volatile string layoutPath;
        private volatile bool sleeping;
        private volatile bool disposed;
        private int ticking;
        private int snapshotCounter;

        // Two alternating frames: the Manager may still be reading the previous one.
        private readonly Bitmap[] frames = new Bitmap[2];
        private int frameIndex;

        public PanelInstance(PanelWidget parent, WidgetSize size, Guid instanceGuid)
        {
            this.parent = parent;
            WidgetSize = size;
            Guid = instanceGuid;
            pixelSize = PanelWidget.PixelSize(size);

            sensors = new SensorHub(parent.WidgetManager)
            {
                SensorDumpPath = Path.Combine(parent.ResourcePath ?? Path.GetTempPath(), "sensors.txt")
            };

            layoutPath = ReadLayoutSetting();
            reloadDebounce = new Timer(_ => { try { ReloadLayout(); } catch { /* see QueueReload */ } }, null, Timeout.Infinite, Timeout.Infinite);
            timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);

            // Never bind sensors on the Manager's calling thread.
            QueueReload();
        }

        // ---------------------------------------------------------------- IWidgetInstance
        public IWidgetObject WidgetObject => parent;
        public Guid Guid { get; }
        public WidgetSize WidgetSize { get; }
        public event WidgetUpdatedEventHandler WidgetUpdated;

        public void RequestUpdate()
        {
            if (!disposed) ThreadPool.QueueUserWorkItem(_ => Tick());
        }

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            switch (click_type)
            {
                // Double-tap forces a layout reload (handy while editing on the device).
                case ClickType.Double: QueueReload(); break;
                // Swipe or long-press to flip through the skins folder.
                case ClickType.SwipeLeft:
                case ClickType.Long: CycleSkin(+1); break;
                case ClickType.SwipeRight: CycleSkin(-1); break;
            }
        }

        public UserControl GetSettingsControl() => new SettingsControl(this);

        // The Manager can call these after Dispose (e.g. on Windows shutdown it disposes widgets,
        // then sends every page to sleep). Any exception here is unhandled in the Manager and
        // kills it, so they must be no-ops once disposed and never throw.
        public void EnterSleep()
        {
            sleeping = true;
            if (disposed) return;
            try { timer.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
        }

        public void ExitSleep()
        {
            sleeping = false;
            if (disposed) return;
            Schedule();
            RequestUpdate();
        }

        // ---------------------------------------------------------------- settings

        internal string LayoutPath
        {
            get => layoutPath;
            set
            {
                var path = string.IsNullOrWhiteSpace(value) ? parent.DefaultLayoutPath : value;
                var stored = string.Equals(path, parent.DefaultLayoutPath, StringComparison.OrdinalIgnoreCase) ? "" : path;
                try { parent.WidgetManager?.StoreSetting(this, LayoutPathSetting, stored, true); } catch { }
                layoutPath = path;
                QueueReload();
            }
        }

        private string ReadLayoutSetting()
        {
            string stored = null;
            try { parent.WidgetManager?.LoadSetting(this, LayoutPathSetting, out stored); } catch { }
            return string.IsNullOrWhiteSpace(stored) ? parent.DefaultLayoutPath : stored;
        }

        internal const string SkinsFolderSetting = "SkinsFolder";
        private volatile string skinsFolder;

        /// <summary>Folder whose layouts/skins are offered in settings and cycled by swiping.</summary>
        internal string SkinsFolder
        {
            get
            {
                if (skinsFolder == null)
                {
                    string stored = null;
                    try { parent.WidgetManager?.LoadSetting(this, SkinsFolderSetting, out stored); } catch { }
                    skinsFolder = string.IsNullOrWhiteSpace(stored) ? Path.GetDirectoryName(layoutPath) ?? "" : stored;
                }
                return skinsFolder;
            }
            set
            {
                skinsFolder = value ?? "";
                try { parent.WidgetManager?.StoreSetting(this, SkinsFolderSetting, skinsFolder, true); } catch { }
            }
        }

        /// <summary>Switch to the next (+1) or previous (-1) skin in <see cref="SkinsFolder"/>.</summary>
        internal void CycleSkin(int direction)
        {
            var next = SkinCatalog.Next(SkinsFolder, layoutPath, direction);
            if (next != null && !string.Equals(next, layoutPath, StringComparison.OrdinalIgnoreCase))
                LayoutPath = next;
        }

        internal string SensorDumpPath => sensors.SensorDumpPath;
        internal string DefaultLayoutPath => parent.DefaultLayoutPath;

        internal string StatusText
        {
            get
            {
                var err = layoutError;
                if (err != null) return "Layout error: " + err;
                var unbound = sensors.Unbound;
                var status = unbound.Count == 0
                    ? "Layout loaded; all sensors bound."
                    : "Unmatched sensors: " + string.Join(", ", unbound);
                PanelRenderer r;
                lock (renderGate) r = renderer;
                var warnings = r?.Layout.Warnings;
                return warnings != null && warnings.Count > 0 ? status + "\n" + string.Join("\n", warnings) : status;
            }
        }

        // ---------------------------------------------------------------- core

        /// <summary>Reloads on a pool thread; safe to call from the UI thread.</summary>
        internal void QueueReload()
        {
            if (disposed) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                // Pool thread: never let an exception escape (it would terminate WigiDash Manager).
                try { ReloadLayout(); }
                catch (Exception ex) { if (!disposed) Log(LogLevel.ERROR, "Layout reload failed", ex.ToString()); }
            });
        }

        private void ReloadLayout()
        {
            if (disposed) return;
            lock (reloadGate)
            {
                if (disposed) return;
                var path = layoutPath;
                PanelRenderer next = null;
                try
                {
                    var layout = LayoutLoader.Load(path);
                    next = new PanelRenderer(layout, Path.GetDirectoryName(path));
                    sensors.SetBindings(layout.Sensors);
                    layoutError = null;
                }
                catch (Exception ex)
                {
                    next?.Dispose();
                    next = null;
                    layoutError = $"{Path.GetFileName(path)}: {ex.Message}";
                    Log(LogLevel.ERROR, "Failed to load layout", ex.ToString());
                }

                if (next != null)
                {
                    lock (renderGate)
                    {
                        renderer?.Dispose();
                        renderer = next;
                    }
                }
                WatchLayout(path);
            }
            Schedule();
            RequestUpdate();
        }

        private void WatchLayout(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (watcher != null && string.Equals(watcher.Path, dir, StringComparison.OrdinalIgnoreCase)) return;
                watcher?.Dispose();
                watcher = null;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                // Watch the whole folder so edits to images referenced by the layout also refresh it.
                watcher = new FileSystemWatcher(dir) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                FileSystemEventHandler onChange = (s, e) =>
                {
                    if (IsGeneratedFile(e.Name)) return;
                    if (disposed) return;
                    try { reloadDebounce.Change(400, Timeout.Infinite); } catch (ObjectDisposedException) { }
                };
                watcher.Changed += onChange;
                watcher.Created += onChange;
                watcher.Renamed += (s, e) => onChange(s, e);
                watcher.EnableRaisingEvents = true;
            }
            catch
            {
                // Hot reload is a convenience.
            }
        }

        /// <summary>Files this widget (or the preview tool) writes itself; changes to them must not trigger a reload.</summary>
        private bool IsGeneratedFile(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var file = Path.GetFileName(name);
            if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return true;
            if (file.Equals("sensors.txt", StringComparison.OrdinalIgnoreCase)) return true;
            if (file.EndsWith(".preview.png", StringComparison.OrdinalIgnoreCase)) return true;
            PanelRenderer r;
            lock (renderGate) r = renderer;
            var snap = r?.Layout.Snapshot;
            return !string.IsNullOrEmpty(snap) && file.Equals(Path.GetFileName(snap), StringComparison.OrdinalIgnoreCase);
        }

        private void Schedule()
        {
            if (disposed || sleeping) return;
            int ms;
            lock (renderGate) ms = renderer?.RefreshMs ?? 1000;
            try { timer.Change(ms, ms); } catch (ObjectDisposedException) { }
        }

        private void Tick()
        {
            if (disposed || sleeping) return;
            // Skip this tick if the previous one is still running, so threads can never pile up.
            if (Interlocked.CompareExchange(ref ticking, 1, 0) != 0) return;
            // Runs on a pool thread: an escaping exception would terminate WigiDash Manager.
            try { TickCore(); }
            catch (Exception ex) { if (!disposed) Log(LogLevel.ERROR, "Render tick failed", ex.ToString()); }
            finally { Volatile.Write(ref ticking, 0); }
        }

        private void TickCore()
        {
            // Re-resolve unmatched bindings, with backoff: each sensor-list fetch makes
            // WigiDash Manager permanently slower (see RescanSchedule).
            sensors.MaybeRescan();

            Bitmap frame;
            lock (renderGate)
            {
                if (disposed) return;
                frameIndex ^= 1;
                frames[frameIndex]?.Dispose();
                frame = frames[frameIndex] = new Bitmap(pixelSize.Width, pixelSize.Height);
                using (var g = Graphics.FromImage(frame))
                {
                    if (renderer != null) renderer.Render(g, pixelSize.Width, pixelSize.Height, sensors);
                    // (Render wraps the value source so "system" bindings resolve locally.)
                    else g.Clear(Color.Black);
                    var err = layoutError;
                    if (err != null) DrawError(g, err);
                }
                if (++snapshotCounter % 10 == 1) SaveSnapshot(frame);
            }

            try
            {
                WidgetUpdated?.Invoke(this, new WidgetUpdatedEventArgs { WidgetBitmap = frame, Offset = Point.Empty, WaitMax = 1000 });
            }
            catch (Exception ex)
            {
                Log(LogLevel.WARN, "WidgetUpdated handler threw", ex.ToString());
            }
        }

        private void SaveSnapshot(Bitmap frame)
        {
            var rel = renderer?.Layout.Snapshot;
            if (string.IsNullOrWhiteSpace(rel)) return;
            try
            {
                var path = Path.IsPathRooted(rel) ? rel : Path.Combine(Path.GetDirectoryName(layoutPath) ?? "", rel);
                var tmp = path + ".tmp";
                frame.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                // Debug aid only.
            }
        }

        private static void DrawError(Graphics g, string message)
        {
            using (var bg = new SolidBrush(Color.FromArgb(200, 120, 0, 0)))
            using (var font = new Font("Segoe UI", 11f))
            {
                var size = g.VisibleClipBounds;
                var rect = new RectangleF(8, size.Height - 64, size.Width - 16, 56);
                g.FillRectangle(bg, rect);
                g.DrawString(message, font, Brushes.White, rect);
            }
        }

        private void Log(LogLevel level, string message, string verbose)
        {
            try { parent.WidgetManager?.WriteLogMessage(this, level, "[SensorPanel] " + message, verbose ?? message); } catch { }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timer.Dispose();
            reloadDebounce.Dispose();
            watcher?.Dispose();
            sensors.Dispose();
            lock (renderGate)
            {
                renderer?.Dispose();
                renderer = null;
                // The last frame handed to the Manager may still be in use; let GC finalise it.
                frames[frameIndex ^ 1]?.Dispose();
            }
        }
    }
}
