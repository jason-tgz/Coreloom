using Coreloom.Core.Diagnostics;
using Coreloom.Core.Formatting;
using Coreloom.Core.Settings;

namespace Coreloom.ViewModels.Services;

/// <summary>
/// Holds the live settings object and persists changes with a short debounce so dragging a slider
/// does not write the file dozens of times. Raise <see cref="Changed"/> after edits so views update.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _dirty;

    public SettingsService(SettingsStore store, AppSettings settings)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Current = settings ?? throw new ArgumentNullException(nameof(settings));
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        Formatter = CreateFormatter(settings);
    }

    public AppSettings Current { get; }

    public UnitFormatter Formatter { get; private set; }

    /// <summary>Raised on the calling thread after a change was recorded. The argument names the section.</summary>
    public event Action<string>? Changed;

    /// <summary>Records a change: refreshes derived state, raises <see cref="Changed"/>, schedules a save.</summary>
    public void Update(string section, Action<AppSettings>? mutate = null)
    {
        mutate?.Invoke(Current);
        Formatter = CreateFormatter(Current);
        lock (_gate)
        {
            _dirty = true;
            _timer.Change(600, Timeout.Infinite);
        }

        Changed?.Invoke(section);
    }

    /// <summary>Writes pending changes immediately (called on exit).</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
        }

        try
        {
            _store.Save(Current);
        }
        catch (Exception ex)
        {
            Log.Warn("Settings", "Saving settings failed.", ex);
        }
    }

    public static UnitFormatter CreateFormatter(AppSettings settings) =>
        new(settings.Monitoring.TemperatureUnit, settings.Monitoring.DataUnits, settings.Monitoring.NetworkUnits);

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
    }
}
