using System.Text;
using Vigil.Core.Diagnostics;
using Vigil.Core.Hardware;
using Vigil.Core.Telemetry;
using Vigil.Core.Updates;

namespace Vigil.Core.Tests;

public class SmbiosParserTests
{
    private static byte[] Structure(byte type, byte[] formattedAfterHeader, params string[] strings)
    {
        var bytes = new List<byte> { type, (byte)(4 + formattedAfterHeader.Length), 0x00, 0x10 };
        bytes.AddRange(formattedAfterHeader);
        if (strings.Length == 0)
        {
            bytes.Add(0);
        }

        foreach (string s in strings)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(s));
            bytes.Add(0);
        }

        bytes.Add(0);
        return bytes.ToArray();
    }

    private static byte[] MemoryDevice(ushort sizeMb, byte type, ushort speed, ushort configured)
    {
        var f = new byte[0x22 - 4];
        void Put(int offset, ushort value)
        {
            f[offset - 4] = (byte)(value & 0xFF);
            f[offset - 4 + 1] = (byte)(value >> 8);
        }

        Put(0x0C, sizeMb);
        f[0x10 - 4] = 1; // device locator -> string 1
        f[0x11 - 4] = 2; // bank locator -> string 2
        f[0x12 - 4] = type;
        Put(0x15, speed);
        f[0x17 - 4] = 3; // manufacturer
        f[0x1A - 4] = 4; // part number
        Put(0x20, configured);
        return f;
    }

    [Fact]
    public void ParsesBoardAndMemoryModules()
    {
        var table = new List<byte>();
        var board = new byte[4];
        board[0] = 1; // manufacturer string index (offset 0x04)
        board[1] = 2; // product (offset 0x05)
        table.AddRange(Structure(2, board, "ASRock", "X870E Nova WiFi"));
        table.AddRange(Structure(17, MemoryDevice(16384, 0x22, 6000, 6000), "DIMM_A2", "BANK 0", "G.Skill", "F5-6000J3036F16G "));
        table.AddRange(Structure(17, MemoryDevice(0, 0x22, 0, 0), "DIMM_A1", "BANK 0", "Unknown", "Unknown")); // empty slot
        table.AddRange(Structure(127, []));

        var raw = new List<byte> { 0, 3, 6, 0 };
        raw.AddRange(BitConverter.GetBytes(table.Count));
        raw.AddRange(table);

        var info = SmbiosParser.Parse(raw.ToArray());

        Assert.Equal("ASRock", info.BoardManufacturer);
        Assert.Equal("X870E Nova WiFi", info.BoardProduct);
        Assert.Single(info.MemoryModules);
        var module = info.MemoryModules[0];
        Assert.Equal(16L * 1024 * 1024 * 1024, module.SizeBytes);
        Assert.Equal("DDR5", module.MemoryType);
        Assert.Equal(6000, module.SpeedMtps);
        Assert.Equal(6000, module.ConfiguredSpeedMtps);
        Assert.Equal("G.Skill", module.Manufacturer);
        Assert.Equal("F5-6000J3036F16G", module.PartNumber);
        Assert.Equal("DIMM_A2", module.Locator);
    }

    [Fact]
    public void GarbageInput_DoesNotThrow()
    {
        var info = SmbiosParser.Parse(new byte[] { 1, 2, 3 });
        Assert.Empty(info.MemoryModules);
        info = SmbiosParser.Parse(Enumerable.Repeat((byte)0xFF, 200).ToArray());
        Assert.Empty(info.MemoryModules);
    }
}

public class DeviceRegistryTests
{
    [Fact]
    public void Upsert_MergesPropertiesAndSources()
    {
        var registry = new DeviceRegistry();
        registry.Upsert(new DeviceInfo("gpu/0", MetricCategory.Gpu, "NVIDIA GeForce RTX 5090")
        {
            Source = "NVML",
            Properties = [new("Driver", "581.57"), new("VRAM", "32 GB")],
        });
        registry.Upsert(new DeviceInfo("gpu/0", MetricCategory.Gpu, "gpu/0")
        {
            Source = "LibreHardwareMonitor",
            IsIntegrated = false,
            Properties = [new("VRAM", "31.8 GB"), new("Hot spot", "available")],
        });

        var device = registry.Get("gpu/0")!;
        Assert.Equal("NVIDIA GeForce RTX 5090", device.Name);
        Assert.Equal("NVML, LibreHardwareMonitor", device.Source);
        Assert.False(device.IsIntegrated);
        Assert.Equal(3, device.Properties.Count);
        Assert.Equal("31.8 GB", device.GetProperty("vram"));
        Assert.Equal("Driver", device.Properties[0].Key);
    }

    [Fact]
    public void GetByCategory_FiltersAndOrders()
    {
        var registry = new DeviceRegistry();
        registry.Upsert(new DeviceInfo("disk/1", MetricCategory.Storage, "B") { Order = 2 });
        registry.Upsert(new DeviceInfo("disk/0", MetricCategory.Storage, "A") { Order = 1 });
        registry.Upsert(new DeviceInfo("cpu", MetricCategory.Cpu, "CPU"));
        var disks = registry.GetByCategory(MetricCategory.Storage);
        Assert.Equal(new[] { "A", "B" }, disks.Select(d => d.Name).ToArray());
    }
}

public class MetricCatalogTests
{
    [Fact]
    public void Register_ReusesDescriptorForSameId()
    {
        var catalog = new MetricCatalog();
        var a = catalog.Register(new MetricDescriptor("gpu/0/temp", "GPU temperature", MetricCategory.Gpu, MetricKind.Temperature), "NVML", priority: 100);
        var b = catalog.Register(new MetricDescriptor("gpu/0/temp", "GPU Core", MetricCategory.Gpu, MetricKind.Temperature), "LHM", priority: 50);
        Assert.Same(a.Descriptor, b.Descriptor);
        Assert.Equal("GPU temperature", b.Descriptor.Name);
        Assert.Equal(1, catalog.Count);
    }

    [Fact]
    public void UnavailableReason_CanBeSetBeforeRegistration()
    {
        var catalog = new MetricCatalog();
        catalog.SetUnavailableReason("cpu/temp", "Requires administrator");
        var handle = catalog.Register(new MetricDescriptor("cpu/temp", "CPU", MetricCategory.Cpu, MetricKind.Temperature), "LHM");
        Assert.Equal("Requires administrator", handle.Descriptor.UnavailableReason);
        catalog.SetUnavailableReason("cpu/temp", null);
        Assert.Null(handle.Descriptor.UnavailableReason);
    }
}

public sealed class AppPathsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vigil-paths-" + Guid.NewGuid().ToString("N"));

    public AppPathsTests()
    {
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void InstalledMode_UsesAppData()
    {
        var paths = AppPaths.Resolve(_dir, "/roaming", "/local");
        Assert.False(paths.IsPortable);
        Assert.Equal(Path.Combine("/roaming", "Vigil", "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine("/local", "Vigil", "Logs"), paths.LogDirectory);
    }

    [Fact]
    public void PortableMarker_StoresDataBesideApp()
    {
        File.WriteAllText(Path.Combine(_dir, AppPaths.PortableMarkerFile), string.Empty);
        var paths = AppPaths.Resolve(_dir, "/roaming", "/local");
        Assert.True(paths.IsPortable);
        Assert.Equal(Path.Combine(_dir, "Data", "settings.json"), paths.SettingsFile);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // ignore
        }
    }
}

public class DiagnosticReportTests
{
    [Fact]
    public void Build_ListsAvailableAndMissingSensors()
    {
        var catalog = new MetricCatalog();
        var handle = catalog.Register(new MetricDescriptor(MetricIds.GpuTemperature(0), "GPU temperature", MetricCategory.Gpu, MetricKind.Temperature), "NVML");
        catalog.SetUnavailableReason(MetricIds.CpuTemperature, "Requires administrator rights and the PawnIO driver");
        catalog.Register(new MetricDescriptor(MetricIds.CpuTemperature, "CPU temperature", MetricCategory.Cpu, MetricKind.Temperature), "LHM");
        var frame = new TelemetryFrame(1, 1, DateTime.Now, TimeSpan.FromMilliseconds(12), new Dictionary<string, MetricSample>
        {
            [MetricIds.GpuTemperature(0)] = new(38, handle),
        });

        string report = DiagnosticReportBuilder.Build(new DiagnosticReportInput
        {
            App = new AppVersionInfo(SemanticVersion.Parse("1.0.0"), "1.0.0", "o/r", null),
            Capabilities = new SystemCapabilities { IsElevated = false, OsBuild = 26100 },
            Providers = [new ProviderStatus("NVML", MonitoringSection.Gpu, ProviderState.Running, null, null, 0, 0, 1, 1, 50, DateTime.Now, 20)],
            Devices =
            [
                new DeviceInfo("gpu/0", MetricCategory.Gpu, "NVIDIA GeForce RTX 5090"),
                new DeviceInfo("net/eth", MetricCategory.Network, "Ethernet") { Properties = [new("Type", "Ethernet"), new("IPv4 address", "192.168.1.20")] },
            ],
            Metrics = catalog.Snapshot(),
            Frame = frame,
        });

        Assert.Contains("NVIDIA GeForce RTX 5090", report);
        Assert.Contains("| GPU temperature | 38 °C | NVML | Measured | OK |", report);
        Assert.Contains("Unavailable: Requires administrator rights and the PawnIO driver", report);
        Assert.Contains("PawnIO", report);
        Assert.Contains("- Type: Ethernet", report);
        Assert.DoesNotContain("192.168.1.20", report);
    }

    [Fact]
    public void RedactIdentifiers_HidesComputerAndUserNames()
    {
        string text = "### JERRY-PC (`system`)\n- Log: C:\\Users\\jerry\\AppData\\Local\\Vigil\\Logs\n- Board: JERRY-PCB rev 1";
        string redacted = DiagnosticReportBuilder.RedactIdentifiers(text, "jerry-pc", "Jerry");

        Assert.Contains("### <computer-name> (`system`)", redacted);
        Assert.Contains(@"C:\Users\<user>\AppData", redacted);
        Assert.Contains("JERRY-PCB rev 1", redacted);   // only whole names are replaced
        Assert.DoesNotContain("JERRY-PC ", redacted);
    }
}

public class LogTests
{
    [Fact]
    public void WarnThrottled_SuppressesRepeats()
    {
        var sink = new MemoryLogSink();
        Log.AddSink(sink);
        try
        {
            string key = "test-" + Guid.NewGuid();
            for (int i = 0; i < 5; i++)
            {
                Log.WarnThrottled(key, TimeSpan.FromMinutes(5), "Test", "boom");
            }

            Assert.Single(sink.Snapshot(), e => e.Message == "boom");
        }
        finally
        {
            Log.RemoveSink(sink);
        }
    }
}
