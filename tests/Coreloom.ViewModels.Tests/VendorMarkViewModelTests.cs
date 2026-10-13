using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Pages;

namespace Coreloom.ViewModels.Tests;

public class VendorMarkViewModelTests
{
    [Fact]
    public void GpuPage_ExposesTheVendorKeyOfTheSelectedGpu()
    {
        var (monitor, settings, _) = Fixture.Create();
        var gpu = new GpuViewModel(Fixture.Context(monitor, settings));

        gpu.Activate(monitor.Engine.LatestFrame);

        Assert.Equal(HardwareVendor.Nvidia, gpu.Vendor);
        Assert.Equal("nvidia", gpu.VendorKey);
        settings.Dispose();
    }

    [Fact]
    public void CpuPage_ExposesTheVendorKeyFromTheProcessorName_BeforeDeviceInfoArrives()
    {
        var (monitor, settings, _) = Fixture.Create();
        var cpu = new CpuViewModel(Fixture.Context(monitor, settings));

        Assert.Equal("amd", cpu.VendorKey);
        settings.Dispose();
    }

    [Fact]
    public void CpuPage_PrefersTheVendorReportedByTheDevice()
    {
        var (monitor, settings, _) = Fixture.Create();
        monitor.Engine.Devices.Upsert(new DeviceInfo(MetricIds.CpuDeviceId, MetricCategory.Cpu, "Intel(R) Core(TM) Ultra 9 285K") { Vendor = "Intel" });
        var cpu = new CpuViewModel(Fixture.Context(monitor, settings));

        cpu.Activate(monitor.Engine.LatestFrame);

        Assert.Equal("intel", cpu.VendorKey);
        settings.Dispose();
    }
}
