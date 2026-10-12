using System.Globalization;
using Vigil.Core.Formatting;
using Vigil.Core.Hardware;
using Vigil.Core.Telemetry;
using Vigil.Hardware.SystemInfo;

namespace Vigil.Hardware.Providers;

/// <summary>Publishes static system/board information as devices. Writes no metrics.</summary>
public sealed class SystemInfoProvider : ITelemetryProvider
{
    public SystemInfoProvider(SystemSummary summary)
    {
        Summary = summary;
    }

    public SystemSummary Summary { get; }

    public string Name => "System information";

    public MonitoringSection Section => MonitoringSection.System;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.FromMinutes(10);

    public ProviderInitResult Initialize(IProviderContext context)
    {
        var f = new UnitFormatter(culture: CultureInfo.InvariantCulture);
        var props = new List<KeyValuePair<string, string>>
        {
            new("Computer name", Summary.ComputerName),
            new("Operating system", Summary.OperatingSystem),
            new("Version", Summary.OsVersion),
            new("Processor", Summary.CpuName),
        };
        if (Summary.InstalledMemoryBytes > 0)
        {
            props.Add(new("Installed memory", f.Bytes(Summary.InstalledMemoryBytes, 0).ToString()));
        }

        if (Summary.Motherboard is not null)
        {
            props.Add(new("Motherboard", Summary.Motherboard));
        }

        if (Summary.Bios is not null)
        {
            props.Add(new("BIOS", Summary.Bios));
        }

        context.UpsertDevice(new DeviceInfo(MetricIds.SystemDeviceId, MetricCategory.System, Summary.ComputerName)
        {
            Kind = "Computer",
            Source = "Windows",
            Properties = props,
        });

        if (Summary.Motherboard is not null)
        {
            var smbios = SystemInfoReader.Smbios;
            var boardProps = new List<KeyValuePair<string, string>>();
            if (smbios.BoardManufacturer is { } m) boardProps.Add(new("Manufacturer", m));
            if (smbios.BoardProduct is { } p) boardProps.Add(new("Model", p));
            if (Summary.Bios is not null) boardProps.Add(new("BIOS", Summary.Bios));
            context.UpsertDevice(new DeviceInfo(MetricIds.BoardDeviceId, MetricCategory.Motherboard, Summary.Motherboard)
            {
                Kind = "Motherboard",
                Source = "SMBIOS",
                Properties = boardProps,
            });
        }

        string caps = context.Capabilities.IsElevated ? "administrator" : "standard user";
        return ProviderInitResult.Running($"{Summary.OperatingSystem} {Summary.OsVersion}, running as {caps}");
    }

    public void Poll(IMetricWriter writer)
    {
    }

    public void Shutdown()
    {
    }

    public void Dispose()
    {
    }
}
