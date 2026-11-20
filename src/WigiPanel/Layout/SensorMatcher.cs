using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WigiPanel.Layout
{
    /// <summary>
    /// Decides which sensors a <see cref="SensorBinding"/> reads. Kept free of the WigiDash API
    /// so the same rules can be checked against saved sensor lists in tests.
    /// </summary>
    public static class SensorMatcher
    {
        /// <summary>True if the binding's name / source / unit patterns all match.</summary>
        public static bool Matches(SensorBinding b, string name, string source, string unit)
        {
            if (b == null) return false;
            if (string.IsNullOrEmpty(b.Name) && string.IsNullOrEmpty(b.Source)) return false;
            return IsMatch(b.Name, name) && IsMatch(b.Source, source) && IsMatch(b.Unit, unit);
        }

        /// <summary>
        /// The sensors a binding reads, in list order: every match when it aggregates,
        /// otherwise the single match at <see cref="SensorBinding.Index"/> (default the first).
        /// </summary>
        public static List<T> Select<T>(SensorBinding b, IEnumerable<T> all,
            Func<T, string> name, Func<T, string> source, Func<T, string> unit)
        {
            var matches = all.Where(s => Matches(b, name(s), source(s), unit(s)));
            return !string.IsNullOrEmpty(b.Aggregate)
                ? matches.ToList()
                : matches.Skip(Math.Max(0, b.Index ?? 0)).Take(1).ToList();
        }

        /// <summary>Case-insensitive regex match; values are trimmed (HWiNFO pads some names with spaces).</summary>
        public static bool IsMatch(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            value = (value ?? "").Trim();
            try { return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
            catch (ArgumentException) { return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase); }
        }
    }
}
