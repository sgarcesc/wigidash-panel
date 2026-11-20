using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using WigiPanel.Layout.Aida;

namespace WigiPanel.Layout
{
    public static class LayoutLoader
    {
        /// <summary>File extensions the widget can load as a layout.</summary>
        public static readonly string[] SupportedExtensions = new[] { ".json" }.Concat(AidaSkinImporter.Extensions).ToArray();

        /// <summary>
        /// Fallback aida-sensors.json locations, tried after the skin's own folder
        /// (the widget sets this to its install folder).
        /// </summary>
        public static List<string> AidaMapFallbacks { get; } = new List<string>();

        /// <summary>
        /// Extra folders whose .ttf/.otf files are loaded for every layout (e.g. the widget's
        /// "fonts" folder), so fonts AIDA skins expect don't need installing system-wide.
        /// </summary>
        public static List<string> SharedFontDirs { get; } = new List<string>();

        public static bool IsLayoutFile(string path) =>
            SupportedExtensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant())
            && !Path.GetFileName(path).Equals(AidaMapFileName, StringComparison.OrdinalIgnoreCase);

        public const string AidaMapFileName = "aida-sensors.json";

        public static PanelLayout Load(string path)
        {
            PanelLayout layout;
            if (AidaSkinImporter.IsAidaSkin(path))
            {
                var map = LoadAidaMap(path);
                layout = AidaSkinImporter.Import(path, map);
                if (map == null) (layout.Warnings ?? (layout.Warnings = new List<string>())).Add("aida-sensors.json not found; values will be empty.");
            }
            else
            {
                layout = Parse(File.ReadAllText(path, Encoding.UTF8));
            }

            // Shared fonts, resolved to absolute paths so they work for any layout location.
            var fonts = layout.Fonts ?? new List<string>();
            foreach (var dir in SharedFontDirs.Where(Directory.Exists))
                fonts.AddRange(Directory.GetFiles(dir, "*.ttf").Concat(Directory.GetFiles(dir, "*.otf")));
            layout.Fonts = fonts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return layout;
        }

        /// <summary>
        /// Mapping files that apply to a skin, lowest priority first: the widget's own, the skin
        /// folder's aida-sensors.json, then "&lt;skin file&gt;.aida-sensors.json".
        /// </summary>
        public static List<string> AidaMapFiles(string skinPath)
        {
            var dir = Path.GetDirectoryName(skinPath) ?? "";
            var list = new List<string>();
            var fallback = AidaMapFallbacks.FirstOrDefault(File.Exists);
            if (fallback != null) list.Add(fallback);
            foreach (var p in new[] { Path.Combine(dir, AidaMapFileName), Path.Combine(dir, Path.GetFileName(skinPath) + "." + AidaMapFileName) })
                if (File.Exists(p) && !list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p);
            return list;
        }

        private static AidaSensorMap LoadAidaMap(string skinPath)
        {
            AidaSensorMap merged = null;
            foreach (var file in AidaMapFiles(skinPath))
            {
                var m = AidaSensorMap.Load(file);
                if (merged == null) { merged = m; continue; }
                foreach (var kv in m.Map) merged.Map[kv.Key] = kv.Value;
                if (m.Snapshot != null) { merged.Snapshot = m.Snapshot; merged.SourcePath = m.SourcePath; }
            }
            return merged;
        }

        public static PanelLayout Parse(string json)
        {
            var layout = ParseJson<PanelLayout>(json);
            if (layout == null) throw new InvalidDataException("Layout file is empty.");
            if (layout.Canvas == null) layout.Canvas = new CanvasSpec();
            return layout;
        }

        public static T ParseJson<T>(string json) where T : class
        {
            var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
            var serializer = new DataContractJsonSerializer(typeof(T), settings);
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(StripComments(json))))
                return (T)serializer.ReadObject(ms);
        }

        /// <summary>
        /// Allows // line comments in layout files (outside of strings), which makes
        /// hand-editing much friendlier than strict JSON.
        /// </summary>
        internal static string StripComments(string json)
        {
            var sb = new StringBuilder(json.Length);
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; sb.Append(c); continue; }
                if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
                {
                    while (i < json.Length && json[i] != '\n') i++;
                    if (i < json.Length) sb.Append('\n');
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
