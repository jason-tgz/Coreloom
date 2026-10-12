using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Coreloom.Hardware.Native;

internal sealed record PhysicalDiskInfo(int Number, string Model, string BusType, bool? IsSsd, long SizeBytes, bool Removable);

/// <summary>
/// Storage queries through IOCTLs that only need a handle opened with zero access rights,
/// so they work without administrator privileges. Nothing here writes to a disk.
/// </summary>
internal static unsafe class StorageInterop
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint IoctlDiskGetDriveGeometryEx = 0x000700A0;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int StorageDeviceTemperatureProperty = 52;
    private const uint GenericRead = 0x80000000;

    public static string PhysicalDrivePath(int number) => $@"\\.\PhysicalDrive{number}";

    public static PhysicalDiskInfo? TryQueryDisk(int number)
    {
        using SafeFileHandle handle = Win32.OpenDeviceForQuery(PhysicalDrivePath(number));
        if (handle.IsInvalid)
        {
            return null;
        }

        string model = $"Disk {number}";
        string bus = "Unknown";
        bool removable = false;
        byte[] buffer = new byte[1024];
        if (QueryProperty(handle, StorageDeviceProperty, buffer, out uint returned) && returned >= 36)
        {
            var span = buffer.AsSpan(0, (int)returned);
            removable = span[10] != 0;
            string vendor = ReadAnsi(span, BinaryPrimitives.ReadInt32LittleEndian(span[12..]));
            string product = ReadAnsi(span, BinaryPrimitives.ReadInt32LittleEndian(span[16..]));
            bus = BusTypeName(BinaryPrimitives.ReadInt32LittleEndian(span[28..]));
            model = ComposeModel(vendor, product, number);
        }

        bool? ssd = null;
        if (QueryProperty(handle, StorageDeviceSeekPenaltyProperty, buffer, out returned) && returned >= 9)
        {
            ssd = buffer[8] == 0; // IncursSeekPenalty == FALSE → solid state
        }

        long size = 0;
        fixed (byte* p = buffer)
        {
            if (Win32.DeviceIoControl(handle, IoctlDiskGetDriveGeometryEx, null, 0, p, (uint)buffer.Length, out returned, IntPtr.Zero) && returned >= 32)
            {
                size = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(24));
            }
        }

        return new PhysicalDiskInfo(number, model, bus, ssd, size, removable);
    }

    /// <summary>Reads the drive's composite temperature (°C) via StorageDeviceTemperatureProperty.</summary>
    public static double? TryReadTemperature(int number, bool elevated)
    {
        foreach (uint access in elevated ? new[] { 0u, GenericRead } : [0u])
        {
            using SafeFileHandle handle = Win32.OpenDeviceForQuery(PhysicalDrivePath(number), access);
            if (handle.IsInvalid)
            {
                continue;
            }

            byte[] buffer = new byte[512];
            if (QueryProperty(handle, StorageDeviceTemperatureProperty, buffer, out uint returned) && returned >= 28)
            {
                int infoCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(12));
                if (infoCount > 0)
                {
                    short temperature = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(26));
                    // Values outside a plausible range indicate "not reported".
                    if (temperature is > -40 and < 150 and not 0)
                    {
                        return temperature;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Returns the physical disk number hosting the volume (first extent), or null.</summary>
    public static int? TryGetVolumeDiskNumber(string driveLetter)
    {
        string letter = driveLetter.TrimEnd('\\', ':');
        using SafeFileHandle handle = Win32.OpenDeviceForQuery($@"\\.\{letter}:");
        if (handle.IsInvalid)
        {
            return null;
        }

        byte[] buffer = new byte[256];
        fixed (byte* p = buffer)
        {
            bool ok = Win32.DeviceIoControl(handle, IoctlVolumeGetVolumeDiskExtents, null, 0, p, (uint)buffer.Length, out uint returned, IntPtr.Zero);
            // ERROR_MORE_DATA (234) still fills the first extent for spanned volumes.
            if ((ok || Marshal.GetLastPInvokeError() == 234) && BinaryPrimitives.ReadUInt32LittleEndian(buffer) > 0)
            {
                return (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8));
            }
        }

        return null;
    }

    private static bool QueryProperty(SafeFileHandle handle, int propertyId, byte[] output, out uint returned)
    {
        byte* query = stackalloc byte[12];
        new Span<byte>(query, 12).Clear();
        *(int*)query = propertyId;      // PropertyId
        *(int*)(query + 4) = 0;         // PropertyStandardQuery
        fixed (byte* p = output)
        {
            return Win32.DeviceIoControl(handle, IoctlStorageQueryProperty, query, 12, p, (uint)output.Length, out returned, IntPtr.Zero);
        }
    }

    private static string ReadAnsi(ReadOnlySpan<byte> buffer, int offset)
    {
        if (offset <= 0 || offset >= buffer.Length)
        {
            return string.Empty;
        }

        int end = buffer[offset..].IndexOf((byte)0);
        var slice = end < 0 ? buffer[offset..] : buffer.Slice(offset, end);
        return Encoding.ASCII.GetString(slice).Trim();
    }

    internal static string ComposeModel(string vendor, string product, int number)
    {
        vendor = vendor.Trim();
        product = product.Trim();
        if (product.Length == 0)
        {
            return vendor.Length == 0 ? $"Disk {number}" : vendor;
        }

        if (vendor.Length == 0 || vendor.Equals("NVMe", StringComparison.OrdinalIgnoreCase) || vendor.Equals("ATA", StringComparison.OrdinalIgnoreCase)
            || product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase))
        {
            return product;
        }

        return $"{vendor} {product}";
    }

    internal static string BusTypeName(int busType) => busType switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE 1394",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "Virtual (file-backed)",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "Unknown",
    };
}
