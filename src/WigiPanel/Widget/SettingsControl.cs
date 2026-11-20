using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using WigiPanel.Layout;

namespace WigiPanel.Widget
{
    /// <summary>Settings page shown by WigiDash Manager when the widget is selected. Built in code (no XAML).</summary>
    internal sealed class SettingsControl : UserControl
    {
        private readonly PanelInstance instance;
        private readonly TextBox folderBox;
        private readonly ComboBox skinBox;
        private readonly TextBlock status;
        private bool updatingList;
        private string listedFolder;

        public SettingsControl(PanelInstance instance)
        {
            this.instance = instance;

            var root = new StackPanel { Margin = new Thickness(12) };

            // ---- skin picker
            root.Children.Add(Header("Skin"));
            var skinRow = new DockPanel { LastChildFill = true };
            var next = Button("▶", (s, e) => { instance.CycleSkin(+1); Refresh(); });
            var prev = Button("◀", (s, e) => { instance.CycleSkin(-1); Refresh(); });
            DockPanel.SetDock(next, Dock.Right);
            DockPanel.SetDock(prev, Dock.Right);
            skinRow.Children.Add(next);
            skinRow.Children.Add(prev);
            skinBox = new ComboBox { Margin = new Thickness(0, 0, 6, 6), VerticalContentAlignment = VerticalAlignment.Center };
            skinBox.SelectionChanged += OnSkinSelected;
            skinRow.Children.Add(skinBox);
            root.Children.Add(skinRow);

            // ---- folder
            root.Children.Add(Header("Skins folder"));
            var folderRow = new DockPanel { LastChildFill = true };
            var browse = Button("Browse…", OnBrowse);
            DockPanel.SetDock(browse, Dock.Right);
            folderRow.Children.Add(browse);
            folderBox = new TextBox { IsReadOnly = true, Margin = new Thickness(0, 0, 6, 6), VerticalContentAlignment = VerticalAlignment.Center };
            folderRow.Children.Add(folderBox);
            root.Children.Add(folderRow);

            var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            buttons.Children.Add(Button("Reload", (s, e) => { instance.QueueReload(); status.Text = "Reloading…"; }));
            buttons.Children.Add(Button("Open folder", (s, e) => Open(instance.SkinsFolder)));
            buttons.Children.Add(Button("Edit layout", (s, e) => Open(instance.LayoutPath)));
            buttons.Children.Add(Button("AIDA sensor map", (s, e) => Open(CurrentAidaMap())));
            buttons.Children.Add(Button("Sensor list", (s, e) => Open(instance.SensorDumpPath)));
            buttons.Children.Add(Button("Use default", (s, e) =>
            {
                instance.LayoutPath = instance.DefaultLayoutPath;
                instance.SkinsFolder = Path.GetDirectoryName(instance.DefaultLayoutPath);
                Refresh();
            }));
            root.Children.Add(buttons);

            status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
            root.Children.Add(status);

            root.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Opacity = 0.7,
                Text = "Skins can be JSON layouts or AIDA64 exports (.gslcd, .sensorpanel). On the WigiDash, swipe or " +
                       "long-press the panel to change skin and double-tap to reload. AIDA64 sensor IDs are mapped to " +
                       "your sensors in aida-sensors.json (\"AIDA sensor map\"); \"Sensor list\" shows every sensor you can use."
            });

            Content = root;
            Refresh();

            // Reloads and sensor binding happen in the background; keep the status line current.
            var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            poll.Tick += (s, e) => Refresh();
            Loaded += (s, e) => poll.Start();
            Unloaded += (s, e) => poll.Stop();
        }

        private void Refresh()
        {
            var folder = instance.SkinsFolder;
            folderBox.Text = folder;
            status.Text = instance.StatusText;

            updatingList = true;
            try
            {
                if (!string.Equals(folder, listedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    skinBox.ItemsSource = SkinCatalog.List(folder);
                    listedFolder = folder;
                }
                var items = skinBox.ItemsSource as System.Collections.Generic.List<SkinCatalog.Entry>;
                var current = items?.FirstOrDefault(i => string.Equals(i.Path, instance.LayoutPath, StringComparison.OrdinalIgnoreCase));
                if (!ReferenceEquals(skinBox.SelectedItem, current)) skinBox.SelectedItem = current;
            }
            finally
            {
                updatingList = false;
            }
        }

        private void OnSkinSelected(object sender, SelectionChangedEventArgs e)
        {
            if (updatingList || !(skinBox.SelectedItem is SkinCatalog.Entry entry)) return;
            if (!string.Equals(entry.Path, instance.LayoutPath, StringComparison.OrdinalIgnoreCase))
                instance.LayoutPath = entry.Path;
            status.Text = "Loading " + entry.DisplayName + "…";
        }

        /// <summary>Pick any skin or layout; its folder becomes the skins folder.</summary>
        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose a skin (its folder becomes the skins folder)",
                Filter = "Skins and layouts (*.gslcd;*.sensorpanel;*.json)|*.gslcd;*.sensorpanel;*.json|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(instance.SkinsFolder) ? instance.SkinsFolder : Path.GetDirectoryName(instance.LayoutPath),
            };
            // Owned by the Manager window so the dialog can't open hidden behind it.
            var owner = Window.GetWindow(this);
            if ((owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog()) == true)
            {
                instance.SkinsFolder = SkinCatalog.FolderFor(dlg.FileName);
                instance.LayoutPath = dlg.FileName;
                Refresh();
            }
        }

        /// <summary>The most specific aida-sensors.json that applies to the current skin.</summary>
        private string CurrentAidaMap()
        {
            var files = LayoutLoader.AidaMapFiles(instance.LayoutPath);
            return files.Count > 0 ? files[files.Count - 1] : LayoutLoader.AidaMapFallbacks.FirstOrDefault();
        }

        private void Open(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                else
                    status.Text = "Not found: " + path;
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }

        private static TextBlock Header(string text) =>
            new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) };

        private static Button Button(string text, RoutedEventHandler onClick)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6) };
            b.Click += onClick;
            return b;
        }
    }
}
