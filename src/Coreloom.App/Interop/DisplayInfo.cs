using System.Runtime.InteropServices;

namespace Coreloom.App.Interop;

/// <summary>Reads display refresh rates so animations can run at the monitor's native rate.</summary>
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

    private static int CurrentRefreshRate(string? deviceName)
    {
        var mode = new DevMode { Size = (ushort)sizeof(DevMode) };
        return EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode) ? (int)mode.DisplayFrequency : 0;
    }
}
