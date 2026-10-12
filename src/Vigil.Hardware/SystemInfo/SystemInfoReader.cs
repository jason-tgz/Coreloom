using System.Globalization;
using Microsoft.Win32;
using Vigil.Core.Diagnostics;
using Vigil.Core.Hardware;
using Vigil.Core.Telemetry;
using Vigil.Hardware.Native;

namespace Vigil.Hardware.SystemInfo;

/// <summary>Reads static system information from the registry, SMBIOS and processor topology APIs.
/// None of these require administrator rights.</summary>
public static class SystemInfoReader
{
    private static readonly Lazy<SmbiosInfo> SmbiosCache = new(ReadSmbiosSafe);

    public static SmbiosInfo Smbios => SmbiosCache.Value;

    public static SystemCapabilities DetectCapabilities()
    {
        var (pawnInstalled, pawnVersion) = DetectPawnIo();
        return new SystemCapabilities
        {
            IsElevated = Environment.IsPrivilegedProcess,
            PawnIoInstalled = pawnInstalled,
            PawnIoVersion = pawnVersion,
            OsBuild = Environment.OSVersion.Version.Build,
        };
    }

    /// <summary>
    /// PawnIO is the signed kernel driver LibreHardwareMonitor uses for MSR/PCI/SuperIO access (it replaced
    /// the deprecated WinRing0). Vigil never installs it; we only detect it to explain missing sensors.
    /// </summary>
    public static (bool Installed, string? Version) DetectPawnIo()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (key?.GetValue("DisplayVersion") is string version && !string.IsNullOrWhiteSpace(version))
                {
                    return (true, version.Trim());
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // ignore
            }
        }

        return (false, null);
    }

    public static SystemSummary ReadSummary()
    {
        try
        {
            string cpuName = ReadCpuName();
            var (cores, _, _, _) = SafeTopology();
            var smbios = Smbios;
            Win32.GetPhysicallyInstalledSystemMemory(out ulong installedKb);
            long installed = installedKb > 0 ? (long)installedKb * 1024 : smbios.MemoryModules.Sum(m => m.SizeBytes);

            return new SystemSummary(
                Environment.MachineName,
                ReadOsName(),
                ReadOsVersion(),
                ReadMotherboard(smbios),
                ReadBios(smbios),
                cpuName,
                cores,
                Environment.ProcessorCount,
                installed,
                smbios.MemoryModules);
        }
        catch (Exception ex)
        {
            Log.Warn("SystemInfo", "Reading the system summary failed.", ex);
            return SystemSummary.Unknown;
        }
    }

    public static string ReadCpuName()
    {
        string? name = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
        return string.IsNullOrWhiteSpace(name) ? "Processor" : CollapseSpaces(name);
    }

    public static string? ReadCpuVendor() => ReadRegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "VendorIdentifier");

    /// <summary>Nominal processor frequency in MHz as reported by the firmware.</summary>
    public static int ReadCpuBaseMhz()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("~MHz") is int mhz ? mhz : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static (int PhysicalCores, long L3CacheBytes, int PerformanceCores, int EfficiencyCores) SafeTopology()
    {
        try
        {
            return Win32.ReadProcessorTopology();
        }
        catch (Exception ex)
        {
            Log.Warn("SystemInfo", "Reading processor topology failed.", ex);
            return (0, 0, 0, 0);
        }
    }

    public static string ReadOsName()
    {
        string product = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? "Windows";
        // The registry still says "Windows 10" on Windows 11; the build number is authoritative.
        if (Environment.OSVersion.Version.Build >= 22000 && product.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            product = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        return product;
    }

    public static string ReadOsVersion()
    {
        string? display = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
        string build = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild") ?? Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
        int ubr = 0;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            ubr = key?.GetValue("UBR") is int u ? u : 0;
        }
        catch
        {
            // ignore
        }

        string fullBuild = ubr > 0 ? $"{build}.{ubr}" : build;
        return string.IsNullOrWhiteSpace(display) ? $"Build {fullBuild}" : $"{display} (build {fullBuild})";
    }

    public static string? ReadMotherboard(SmbiosInfo smbios)
    {
        string? manufacturer = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardManufacturer") ?? smbios.BoardManufacturer;
        string? product = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct") ?? smbios.BoardProduct;
        if (string.IsNullOrWhiteSpace(product))
        {
            return null;
        }

        manufacturer = string.IsNullOrWhiteSpace(manufacturer) ? null : manufacturer.Trim();
        product = product.Trim();
        return manufacturer is null || product.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase) ? product : $"{manufacturer} {product}";
    }

    public static string? ReadBios(SmbiosInfo smbios)
    {
        string? version = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\BIOS", "BIOSVersion") ?? smbios.BiosVersion;
        string? date = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\BIOS", "BIOSReleaseDate") ?? smbios.BiosDate;
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(date) ? version.Trim() : $"{version.Trim()} ({date.Trim()})";
    }

    private static SmbiosInfo ReadSmbiosSafe()
    {
        try
        {
            byte[]? raw = Win32.ReadSmbios();
            return raw is null ? SmbiosParser.Empty : SmbiosParser.Parse(raw);
        }
        catch (Exception ex)
        {
            Log.Warn("SystemInfo", "Reading SMBIOS failed.", ex);
            return SmbiosParser.Empty;
        }
    }

    internal static string? ReadRegistryString(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    internal static string CollapseSpaces(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
