using System;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace WigiPanel.Rendering
{
    /// <summary>Static machine facts exposed to text templates as {CPUMODEL}, {GPUMODEL}, {HOSTNAME}.</summary>
    public static class SystemInfo
    {
        private static readonly Lazy<string> cpu = new Lazy<string>(ReadCpu);
        private static readonly Lazy<string> gpu = new Lazy<string>(ReadGpu);

        private static readonly Lazy<string> board = new Lazy<string>(() =>
            string.Join(" ", new[] { QueryProp("SELECT Manufacturer FROM Win32_BaseBoard", "Manufacturer"), QueryProp("SELECT Product FROM Win32_BaseBoard", "Product") }
                .Where(s => !string.IsNullOrEmpty(s))).Replace("ASUSTeK COMPUTER INC.", "Asus").Trim());
        private static readonly Lazy<string> bios = new Lazy<string>(() => QueryProp("SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion") ?? "");
        private static readonly Lazy<string> memType = new Lazy<string>(ReadMemoryType);

        /// <summary>
        /// When true, identifying values (host name, IP addresses, BIOS version) return fixed
        /// placeholders instead of the real ones. The preview tool sets this so rendered images
        /// that get committed or shared never leak the machine's identity.
        /// </summary>
        public static bool UsePlaceholders { get; set; }

        public static string CpuModel => cpu.Value;
        public static string GpuModel => gpu.Value;
        public static string HostName => UsePlaceholders ? "MY-PC" : Environment.MachineName;
        public static string MotherboardModel => board.Value;
        public static string BiosVersion => UsePlaceholders ? "1.0" : bios.Value;
        /// <summary>"DDR5", "DDR4", ... from SMBIOS.</summary>
        public static string MemoryType => memType.Value;

        public static string DesktopResolution
        {
            get
            {
                var d = SystemSensors.Display.Current();
                return d.Width > 0 ? $"{d.Width} x {d.Height}" : "";
            }
        }

        /// <summary>System uptime as AIDA shows it: "1d 02:03:04" (days only when non-zero).</summary>
        public static string Uptime
        {
            get
            {
                var t = TimeSpan.FromMilliseconds((uint)Environment.TickCount);
                return t.Days > 0 ? $"{t.Days}d {t:hh\\:mm\\:ss}" : t.ToString("hh\\:mm\\:ss");
            }
        }

        /// <summary>First IPv4 address of an up, non-loopback adapter that has a gateway.</summary>
        public static string LocalIp
        {
            get
            {
                if (UsePlaceholders) return "192.168.1.100";
                try
                {
                    foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                        if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                        var props = ni.GetIPProperties();
                        if (props.GatewayAddresses.Count == 0) continue;
                        var addr = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                        if (addr != null) return addr.Address.ToString();
                    }
                }
                catch { }
                return "";
            }
        }

        /// <summary>
        /// Global IPv6 address of an up adapter with a gateway, preferring the stable address over
        /// temporary (privacy) ones. Empty when the machine has no global IPv6.
        /// </summary>
        public static string LocalIp6
        {
            get
            {
                if (UsePlaceholders) return "2001:db8::1"; // RFC 3849 documentation prefix
                try
                {
                    string temporary = null;
                    foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                        if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                        var props = ni.GetIPProperties();
                        if (props.GatewayAddresses.Count == 0) continue;
                        foreach (var a in props.UnicastAddresses)
                        {
                            var ip = a.Address;
                            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) continue;
                            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Teredo || System.Net.IPAddress.IPv6Loopback.Equals(ip)) continue;
                            if ((ip.GetAddressBytes()[0] & 0xFE) == 0xFC) continue; // unique local fc00::/7
                            if (a.SuffixOrigin == System.Net.NetworkInformation.SuffixOrigin.Random) { temporary = temporary ?? ip.ToString(); continue; }
                            return ip.ToString();
                        }
                    }
                    return temporary ?? "";
                }
                catch { return ""; }
            }
        }

        private static string ReadMemoryType()
        {
            var v = QueryProp("SELECT SMBIOSMemoryType FROM Win32_PhysicalMemory", "SMBIOSMemoryType");
            switch (v)
            {
                case "34": return "DDR5";
                case "35": return "LPDDR5";
                case "26": return "DDR4";
                case "30": return "LPDDR4";
                case "24": return "DDR3";
                default: return "DDR";
            }
        }

        private static string QueryProp(string wql, string prop)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(wql))
                using (var results = searcher.Get())
                    foreach (ManagementBaseObject o in results)
                    {
                        var v = o[prop]?.ToString().Trim();
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
            }
            catch { }
            return null;
        }

        private static string ReadCpu()
        {
            var name = Query("SELECT Name FROM Win32_Processor").FirstOrDefault() ?? "CPU";
            // "AMD Ryzen 7 9800X3D 8-Core Processor" -> "AMD Ryzen 7 9800X3D"
            name = Regex.Replace(name, @"\s+\d+-Core Processor\s*$", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\((R|TM)\)", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\s+CPU\s+@.*$", "", RegexOptions.IgnoreCase);
            return Regex.Replace(name, @"\s{2,}", " ").Trim();
        }

        private static string ReadGpu()
        {
            var names = Query("SELECT Name FROM Win32_VideoController").ToList();
            // Prefer a discrete card over integrated graphics and virtual adapters.
            var best = names.FirstOrDefault(n => Regex.IsMatch(n, @"GeForce|RTX|GTX|Radeon RX|Arc", RegexOptions.IgnoreCase))
                       ?? names.FirstOrDefault(n => !Regex.IsMatch(n, @"Basic|Virtual|Remote|\(TM\) Graphics", RegexOptions.IgnoreCase))
                       ?? names.FirstOrDefault()
                       ?? "GPU";
            return Regex.Replace(best, @"\((R|TM)\)", "").Trim();
        }

        private static string[] Query(string wql)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(wql))
                using (var results = searcher.Get())
                {
                    return results.Cast<ManagementBaseObject>()
                        .Select(o => (o["Name"] as string ?? "").Trim())
                        .Where(s => s.Length > 0)
                        .ToArray();
                }
            }
            catch
            {
                return new string[0];
            }
        }
    }
}
