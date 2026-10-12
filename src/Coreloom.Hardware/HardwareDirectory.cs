using System.Text.RegularExpressions;

namespace Coreloom.Hardware;

/// <summary>
/// Assigns stable "slots" to GPUs so that several providers (NVML, LibreHardwareMonitor) reporting the same
/// physical GPU write to the same canonical metric ids (gpu/{slot}/...). Integrated GPUs (e.g. the Radeon
/// graphics inside a Ryzen 9000 CPU) get their own slot and are never merged with a discrete card.
/// </summary>
public sealed partial class GpuDirectory
{
    private readonly object _gate = new();
    private readonly List<GpuEntry> _entries = [];

    public sealed class GpuEntry
    {
        internal GpuEntry(int slot, string vendor, string name, bool isIntegrated, string? pciBusId)
        {
            Slot = slot;
            Vendor = vendor;
            Name = name;
            IsIntegrated = isIntegrated;
            PciBusId = pciBusId;
        }

        public int Slot { get; }

        public string Vendor { get; }

        public string Name { get; }

        public bool IsIntegrated { get; }

        public string? PciBusId { get; }

        internal HashSet<string> SourceKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal HashSet<string> Families { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<GpuEntry> Entries
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    /// <summary>
    /// Registers a GPU seen by a provider. <paramref name="sourceKey"/> must be unique per provider device,
    /// formatted "family:id" (e.g. "nvml:0", "lhm:/gpu-nvidia/0"). Returns the slot.
    /// </summary>
    public int Register(string sourceKey, string vendor, string name, bool isIntegrated, string? pciBusId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        string family = sourceKey.Split(':')[0];
        string normalized = Normalize(name);
        lock (_gate)
        {
            var existing = _entries.FirstOrDefault(e => e.SourceKeys.Contains(sourceKey));
            if (existing is not null)
            {
                return existing.Slot;
            }

            GpuEntry? match = null;
            if (!string.IsNullOrEmpty(pciBusId))
            {
                match = _entries.FirstOrDefault(e => !e.Families.Contains(family) && string.Equals(e.PciBusId, pciBusId, StringComparison.OrdinalIgnoreCase));
            }

            match ??= _entries.FirstOrDefault(e =>
                !e.Families.Contains(family)
                && string.Equals(e.Vendor, vendor, StringComparison.OrdinalIgnoreCase)
                && Normalize(e.Name) == normalized);

            if (match is null)
            {
                match = new GpuEntry(_entries.Count, vendor, name.Trim(), isIntegrated, pciBusId);
                _entries.Add(match);
            }

            match.SourceKeys.Add(sourceKey);
            match.Families.Add(family);
            return match.Slot;
        }
    }

    /// <summary>Slot registered for an exact source key (e.g. "nvml:0"), or null.</summary>
    public int? SlotForSource(string sourceKey)
    {
        lock (_gate)
        {
            return _entries.FirstOrDefault(e => e.SourceKeys.Contains(sourceKey))?.Slot;
        }
    }

    /// <summary>The GPU shown on the Overview: first discrete GPU, NVIDIA/AMD preferred; otherwise slot 0.</summary>
    public int? PrimarySlot
    {
        get
        {
            lock (_gate)
            {
                if (_entries.Count == 0)
                {
                    return null;
                }

                return (_entries.FirstOrDefault(e => !e.IsIntegrated && e.Vendor is "NVIDIA" or "AMD")
                    ?? _entries.FirstOrDefault(e => !e.IsIntegrated)
                    ?? _entries[0]).Slot;
            }
        }
    }

    public static string Normalize(string name) =>
        Whitespace().Replace(name.Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase), " ")
            .Trim()
            .ToUpperInvariant();

    /// <summary>Heuristic for integrated GPUs where the API gives no explicit flag.</summary>
    public static bool LooksIntegrated(string vendor, string name)
    {
        string n = Normalize(name);
        if (vendor.Equals("Intel", StringComparison.OrdinalIgnoreCase))
        {
            // Intel Arc A/B-series discrete cards have a model number; iGPUs are "UHD", "Iris", "Arc Graphics".
            return !ArcDiscrete().IsMatch(n);
        }

        if (vendor.Equals("AMD", StringComparison.OrdinalIgnoreCase))
        {
            return n is "AMD RADEON GRAPHICS" or "RADEON GRAPHICS" || AmdMobileIgpu().IsMatch(n);
        }

        return false;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"ARC\s+(A|B)\d{3}")]
    private static partial Regex ArcDiscrete();

    [GeneratedRegex(@"RADEON\s+\d{3}M\b")]
    private static partial Regex AmdMobileIgpu();
}

/// <summary>Physical disks discovered by the storage provider, used to match LibreHardwareMonitor drives.</summary>
public sealed class DiskDirectory
{
    private readonly object _gate = new();
    private IReadOnlyList<DiskEntry> _disks = [];

    public sealed record DiskEntry(int Number, string Model, string? Serial, string BusType, bool? IsSsd, long SizeBytes);

    public IReadOnlyList<DiskEntry> Disks
    {
        get { lock (_gate) return _disks; }
    }

    public void Set(IReadOnlyList<DiskEntry> disks)
    {
        lock (_gate)
        {
            _disks = disks.ToArray();
        }
    }

    /// <summary>Finds the disk number whose model matches <paramref name="name"/>, skipping already-claimed disks.</summary>
    public int? MatchByName(string name, ISet<int> claimed)
    {
        string n = GpuDirectory.Normalize(name);
        lock (_gate)
        {
            foreach (var disk in _disks.OrderBy(d => d.Number))
            {
                if (claimed.Contains(disk.Number))
                {
                    continue;
                }

                string model = GpuDirectory.Normalize(disk.Model);
                if (model.Length > 0 && (model == n || n.Contains(model, StringComparison.Ordinal) || model.Contains(n, StringComparison.Ordinal)))
                {
                    return disk.Number;
                }
            }
        }

        return null;
    }
}

/// <summary>Shared state between providers.</summary>
public sealed class HardwareServices
{
    public GpuDirectory Gpus { get; } = new();

    public DiskDirectory Disks { get; } = new();
}
