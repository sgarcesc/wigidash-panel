using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace WigiPanel.Layout.Aida
{
    /// <summary>Contents of aida-sensors.json: AIDA64 item IDs mapped to bindings or text templates.</summary>
    [DataContract]
    public sealed class AidaSensorMap
    {
        [DataMember(Name = "snapshot")] public string Snapshot { get; set; }
        [DataMember(Name = "map")] public Dictionary<string, AidaMapEntry> Map { get; set; }

        /// <summary>File the map was loaded from (snapshot paths are relative to it).</summary>
        [IgnoreDataMember] public string SourcePath { get; set; }

        public static AidaSensorMap Load(string path)
        {
            var map = LayoutLoader.ParseJson<AidaSensorMap>(File.ReadAllText(path));
            map.SourcePath = path;
            if (map.Map == null) map.Map = new Dictionary<string, AidaMapEntry>();
            return map;
        }
    }

    [DataContract]
    public sealed class AidaMapEntry
    {
        /// <summary>Numeric source (HWiNFO binding or system value).</summary>
        [DataMember(Name = "sensor")] public SensorBinding Sensor { get; set; }

        /// <summary>
        /// Native unit of the value (MHz, MB, GB, KB/s). When a skin's unit text names a related
        /// unit (GHz, GB, TB, MB/s, Mbps), the value is converted like AIDA64 does.
        /// </summary>
        [DataMember(Name = "unit")] public string Unit { get; set; }

        /// <summary>.NET number format for the value text (default "0").</summary>
        [DataMember(Name = "format")] public string Format { get; set; }

        /// <summary>Text-only value: a template of {tokens} (built-ins, vars or helper keys).</summary>
        [DataMember(Name = "text")] public string Text { get; set; }
    }
}
