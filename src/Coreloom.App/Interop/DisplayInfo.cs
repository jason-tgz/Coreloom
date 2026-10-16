using System.Runtime.InteropServices;

namespace Coreloom.App.Interop;

/// <summary>Reads display refresh rates (so animations can run at the monitor's native rate) and whether HDR is on.</summary>
internal static unsafe partial class DisplayInfo
{
    private const int EnumCurrentSettings = -1;
    private const uint DisplayDeviceAttachedToDesktop = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public uint Size;
        public fixed char DeviceName[32];
        public fixed char DeviceString[128];
        public uint StateFlags;
        public fixed char DeviceId[128];
        public fixed char DeviceKey[128];
    }

    /// <summary>DEVMODEW, display-mode variant (220 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        public fixed char DeviceName[32];
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TtOption;
        public short Collate;
        public fixed char FormName[32];
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettings(string? deviceName, int modeIndex, ref DevMode devMode);

    /// <summary>
    /// Highest refresh rate among the displays attached to the desktop (Hz), or 60 when it cannot be read.
    /// Values of 0 or 1 mean "hardware default" and are treated as unknown.
    /// </summary>
    public static int MaxRefreshRate()
    {
        int best = 0;
        try
        {
            for (uint i = 0; i < 16; i++)
            {
                var device = new DisplayDevice { Size = (uint)sizeof(DisplayDevice) };
                if (!EnumDisplayDevices(null, i, ref device, 0))
                {
                    break;
                }

                if ((device.StateFlags & DisplayDeviceAttachedToDesktop) == 0)
                {
                    continue;
                }

                string name = new(device.DeviceName);
                int hz = CurrentRefreshRate(name.TrimEnd('\0'));
                best = Math.Max(best, hz);
            }

            if (best <= 1)
            {
                best = CurrentRefreshRate(null);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not a desktop session; fall through to the default.
        }

        return best > 1 ? best : 60;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public int MonitorLeft;
        public int MonitorTop;
        public int MonitorRight;
        public int MonitorBottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public uint Flags;
        public fixed char Device[32];
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    /// <summary>The refresh rate of the display a window is on (Hz), or 60 when it can't be read.</summary>
    public static int RefreshRateAt(IntPtr hwnd)
    {
        try
        {
            const uint DefaultToNearest = 2;
            IntPtr monitor = MonitorFromWindow(hwnd, DefaultToNearest);
            var info = new MonitorInfoEx { Size = (uint)sizeof(MonitorInfoEx) };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                int hz = CurrentRefreshRate(new string(info.Device).TrimEnd('\0'));
                if (hz > 1)
                {
                    return hz;
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // fall through
        }

        return 60;
    }

    private static int CurrentRefreshRate(string? deviceName)
    {
        var mode = new DevMode { Size = (ushort)sizeof(DevMode) };
        return EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode) ? (int)mode.DisplayFrequency : 0;
    }

    // QueryDisplayConfig: DISPLAYCONFIG_PATH_INFO is 72 bytes (source info 20, target info 48 starting with the adapter's
    // LUID and the target id, flags 4), DISPLAYCONFIG_MODE_INFO 64. The advanced-color requests are a 20-byte header
    // (type, size, adapter LUID, target id) followed by a bit field. DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 (type 15,
    // Windows 11 24H2, 36 bytes): bit 4 = HDR supported, bit 5 = HDR turned on. The older DISPLAYCONFIG_GET_ADVANCED_
    // COLOR_INFO (type 9, 32 bytes): bit 0 = advanced color supported, bit 1 = on, bit 2 = only wide color (automatic
    // color management on an SDR display, not HDR).
    private const uint QdcOnlyActivePaths = 0x2;
    private const int PathInfoSize = 72;
    private const int ModeInfoSize = 64;
    private const int PathTargetOffset = 20;
    private const uint GetAdvancedColorInfo = 9;
    private const uint GetAdvancedColorInfo2 = 15;
    private const int AdvancedColorInfoSize = 36; // room for either

    [LibraryImport("user32.dll")]
    private static partial int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [LibraryImport("user32.dll")]
    private static partial int QueryDisplayConfig(uint flags, ref uint pathCount, byte* paths, ref uint modeCount, byte* modes, IntPtr topology);

    [LibraryImport("user32.dll")]
    private static partial int DisplayConfigGetDeviceInfo(byte* request);

    /// <summary>
    /// Whether any active display can show HDR, and whether HDR (Settings › Display › Use HDR) is on for one of them;
    /// null when Windows doesn't say.
    /// </summary>
    public static (bool Supported, bool Enabled)? Hdr()
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0 || pathCount == 0)
            {
                return null;
            }

            var paths = new byte[pathCount * PathInfoSize];
            var modes = new byte[Math.Max(1, modeCount) * ModeInfoSize];
            fixed (byte* p = paths)
            fixed (byte* m = modes)
            {
                if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, p, ref modeCount, m, IntPtr.Zero) != 0)
                {
                    return null;
                }

                bool supported = false, enabled = false, answered = false;
                byte* request = stackalloc byte[AdvancedColorInfoSize];
                for (int i = 0; i < pathCount; i++)
                {
                    byte* target = p + (i * PathInfoSize) + PathTargetOffset;
                    if (Ask(request, target, GetAdvancedColorInfo2, 36))
                    {
                        uint bits = *(uint*)(request + 20);
                        supported |= (bits & (1u << 4)) != 0;
                        enabled |= (bits & (1u << 5)) != 0;
                        answered = true;
                    }
                    else if (Ask(request, target, GetAdvancedColorInfo, 32))
                    {
                        uint bits = *(uint*)(request + 20);
                        bool wideColorOnly = (bits & 4) != 0;
                        supported |= (bits & 1) != 0 && !wideColorOnly;
                        enabled |= (bits & 2) != 0 && !wideColorOnly;
                        answered = true;
                    }
                }

                return answered ? (supported, enabled) : null;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        static bool Ask(byte* request, byte* target, uint type, int size)
        {
            new Span<byte>(request, AdvancedColorInfoSize).Clear();
            *(uint*)request = type;
            *(uint*)(request + 4) = (uint)size;
            *(ulong*)(request + 8) = *(ulong*)target;      // adapter LUID
            *(uint*)(request + 16) = *(uint*)(target + 8); // target id
            return DisplayConfigGetDeviceInfo(request) == 0;
        }
    }

    /// <summary>The device name ("\\.\DISPLAY1") of the display with the highest refresh rate (the gaming display), or null.</summary>
    public static string? FastestDisplayName()
    {
        string? best = null;
        int bestHz = 0;
        try
        {
            for (uint i = 0; i < 16; i++)
            {
                var device = new DisplayDevice { Size = (uint)sizeof(DisplayDevice) };
                if (!EnumDisplayDevices(null, i, ref device, 0))
                {
                    break;
                }

                if ((device.StateFlags & DisplayDeviceAttachedToDesktop) == 0)
                {
                    continue;
                }

                string name = new string(device.DeviceName).TrimEnd('\0');
                int hz = CurrentRefreshRate(name);
                if (best is null || hz > bestHz)
                {
                    (best, bestHz) = (name, hz);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        return best;
    }
}
