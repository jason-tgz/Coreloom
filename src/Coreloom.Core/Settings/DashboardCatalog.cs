namespace Coreloom.Core.Settings;

/// <summary>Known Overview cards, their default order and display names.</summary>
public static class DashboardCatalog
{
    public const string GpuLoad = "gpu-load";
    public const string GpuTemperature = "gpu-temp";
    public const string CpuLoad = "cpu-load";
    public const string CpuTemperature = "cpu-temp";
    public const string Memory = "ram";
    public const string GpuPower = "gpu-power";
    public const string LoadChart = "chart-load";
    public const string TemperatureChart = "chart-temps";
    public const string PowerChart = "chart-power";
    public const string MemoryDonut = "memory-donut";
    public const string StorageDonut = "storage-donut";
    public const string Network = "network";

    public static IReadOnlyList<(string Id, string Name, bool IsSummaryCard)> All { get; } =
    [
        (GpuLoad, "GPU utilization", true),
        (GpuTemperature, "GPU temperature", true),
        (CpuLoad, "CPU utilization", true),
        (CpuTemperature, "CPU temperature", true),
        (Memory, "RAM utilization", true),
        (GpuPower, "GPU power draw", true),
        (LoadChart, "Processor & graphics load chart", false),
        (TemperatureChart, "Temperatures chart", false),
        (PowerChart, "Power chart", false),
        (MemoryDonut, "Memory breakdown", false),
        (StorageDonut, "Storage", false),
        (Network, "Network", false),
    ];

    public static string NameOf(string id) => All.FirstOrDefault(c => c.Id == id).Name ?? id;

    public static bool IsSummary(string id) => All.FirstOrDefault(c => c.Id == id).IsSummaryCard;

    public static List<DashboardCardSetting> Defaults() =>
        All.Select(c => new DashboardCardSetting { Id = c.Id, Visible = true }).ToList();

    /// <summary>Removes unknown/duplicate ids and appends any missing known cards (visible).</summary>
    public static List<DashboardCardSetting> Normalize(List<DashboardCardSetting>? cards)
    {
        var known = All.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DashboardCardSetting>();
        foreach (var card in cards ?? [])
        {
            if (card is null || string.IsNullOrEmpty(card.Id) || !known.Contains(card.Id) || !seen.Add(card.Id))
            {
                continue;
            }

            result.Add(new DashboardCardSetting { Id = card.Id, Visible = card.Visible });
        }

        foreach (var (id, _, _) in All)
        {
            if (seen.Add(id))
            {
                result.Add(new DashboardCardSetting { Id = id, Visible = true });
            }
        }

        return result;
    }
}
