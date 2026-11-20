using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WigiPanel.Layout
{
    /// <summary>Lists the layouts/skins available in a skins folder (top level plus one level of subfolders).</summary>
    public static class SkinCatalog
    {
        public sealed class Entry
        {
            public string Path { get; set; }
            /// <summary>Path relative to the skins folder, without extension for top-level files.</summary>
            public string DisplayName { get; set; }
            public override string ToString() => DisplayName;
        }

        public static List<Entry> List(string folder)
        {
            var list = new List<Entry>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return list;

            IEnumerable<string> files = Directory.GetFiles(folder);
            foreach (var sub in SafeDirs(folder)) files = files.Concat(SafeFiles(sub));

            foreach (var f in files.Where(LayoutLoader.IsLayoutFile).Where(f => !IsMapOverride(f)))
            {
                var rel = f.Substring(folder.TrimEnd('\\', '/').Length).TrimStart('\\', '/');
                var name = System.IO.Path.GetDirectoryName(rel).Length == 0
                    ? System.IO.Path.GetFileNameWithoutExtension(rel)
                    : System.IO.Path.GetDirectoryName(rel) + " / " + System.IO.Path.GetFileNameWithoutExtension(rel);
                list.Add(new Entry { Path = f, DisplayName = name });
            }
            return list.OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>The skin after (or before) <paramref name="current"/>, wrapping around.</summary>
        public static string Next(string folder, string current, int direction)
        {
            var list = List(folder);
            if (list.Count == 0) return null;
            int i = list.FindIndex(e => string.Equals(e.Path, current, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return list[0].Path;
            return list[((i + direction) % list.Count + list.Count) % list.Count].Path;
        }

        /// <summary>
        /// Folder to browse for skins given a picked file: its own folder, or the parent when the
        /// file sits alone in a per-skin subfolder (as AIDA skin downloads often do).
        /// </summary>
        public static string FolderFor(string file)
        {
            var dir = System.IO.Path.GetDirectoryName(file);
            if (dir == null) return null;
            var parent = System.IO.Path.GetDirectoryName(dir);
            bool alone = SafeFiles(dir).Count(LayoutLoader.IsLayoutFile) <= 1;
            bool parentHasSkins = parent != null && SafeFiles(parent).Any(LayoutLoader.IsLayoutFile);
            return alone && parentHasSkins ? parent : dir;
        }

        private static bool IsMapOverride(string f) =>
            f.EndsWith("." + LayoutLoader.AidaMapFileName, StringComparison.OrdinalIgnoreCase);

        private static string[] SafeFiles(string dir)
        {
            try { return Directory.GetFiles(dir); } catch { return new string[0]; }
        }

        private static string[] SafeDirs(string dir)
        {
            try { return Directory.GetDirectories(dir); } catch { return new string[0]; }
        }
    }
}
