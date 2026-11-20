using System;
using System.IO;
using System.Runtime.InteropServices;

namespace WigiPanel.Rendering
{
    /// <summary>
    /// Values the widget computes itself (HWiNFO doesn't provide them), referenced by
    /// bindings with "system": drive.&lt;L&gt;.usedpct|usedgb|freegb|totalgb, volume, refresh, uptime.
    /// </summary>
    public static class SystemSensors
    {
        public static double? Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                var parts = id.ToLowerInvariant().Split('.');
                switch (parts[0])
                {
                    case "drive": return parts.Length == 3 ? Drive(parts[1], parts[2]) : null;
                    case "volume": return Audio.MasterVolumePercent();
                    case "refresh": return Display.Current().Refresh;
                    case "uptime": return (uint)Environment.TickCount / 1000.0;
                }
            }
            catch
            {
                // Missing drive, no audio device, ...
            }
            return null;
        }

        private static double? Drive(string letter, string what)
        {
            var d = new DriveInfo(letter.ToUpperInvariant() + ":\\");
            if (!d.IsReady) return null;
            const double GB = 1024.0 * 1024 * 1024;
            double total = d.TotalSize, free = d.TotalFreeSpace;
            switch (what)
            {
                case "usedpct": return total > 0 ? (total - free) / total * 100 : (double?)null;
                case "usedgb": return (total - free) / GB;
                case "freegb": return free / GB;
                case "totalgb": return total / GB;
                default: return null;
            }
        }

        // ---------------------------------------------------------------- display

        internal static class Display
        {
            private static DateTime cachedAt;
            private static (int Width, int Height, int Refresh) cached;

            public static (int Width, int Height, int Refresh) Current()
            {
                if ((DateTime.UtcNow - cachedAt).TotalSeconds < 10) return cached;
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                if (EnumDisplaySettings(null, -1 /* ENUM_CURRENT_SETTINGS */, ref dm))
                    cached = (dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
                cachedAt = DateTime.UtcNow;
                return cached;
            }

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct DEVMODE
            {
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
                public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
                public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
                public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
                public short dmLogPixels;
                public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
                public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
            }
        }

        // ---------------------------------------------------------------- audio (Core Audio COM)

        internal static class Audio
        {
            public static double? MasterVolumePercent()
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                try
                {
                    if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device) != 0 || device == null) return null;
                    try
                    {
                        var iid = typeof(IAudioEndpointVolume).GUID;
                        if (device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj) != 0) return null;
                        var vol = (IAudioEndpointVolume)obj;
                        try
                        {
                            vol.GetMute(out var muted);
                            if (muted) return 0;
                            vol.GetMasterVolumeLevelScalar(out var level);
                            return Math.Round(level * 100);
                        }
                        finally { Marshal.ReleaseComObject(vol); }
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
                finally { Marshal.ReleaseComObject(enumerator); }
            }

            [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
            private class MMDeviceEnumerator { }

            [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IMMDeviceEnumerator
            {
                int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
                [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            }

            [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IMMDevice
            {
                [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            }

            [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IAudioEndpointVolume
            {
                int RegisterControlChangeNotify(IntPtr notify);
                int UnregisterControlChangeNotify(IntPtr notify);
                int GetChannelCount(out int count);
                int SetMasterVolumeLevel(float levelDb, ref Guid context);
                int SetMasterVolumeLevelScalar(float level, ref Guid context);
                int GetMasterVolumeLevel(out float levelDb);
                int GetMasterVolumeLevelScalar(out float level);
                int SetChannelVolumeLevel(int channel, float levelDb, ref Guid context);
                int SetChannelVolumeLevelScalar(int channel, float level, ref Guid context);
                int GetChannelVolumeLevel(int channel, out float levelDb);
                int GetChannelVolumeLevelScalar(int channel, out float level);
                int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
                int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
            }
        }
    }
}
