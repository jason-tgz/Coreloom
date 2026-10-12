using Coreloom.Core.Formatting;
using Coreloom.Core.History;
using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coreloom-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "settings.json");

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var settings = new SettingsStore(FilePath).Load();
        Assert.Equal(1000, settings.Monitoring.PollingIntervalMs);
        Assert.Equal(DashboardCatalog.All.Count, settings.Dashboard.Cards.Count);
        Assert.Equal(4, settings.Alerts.Rules.Count);
    }

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var store = new SettingsStore(FilePath);
        var settings = new AppSettings();
        settings.Appearance.Theme = ThemePreference.Light;
        settings.Appearance.Accent = "#45D6A0";
        settings.Monitoring.PollingIntervalMs = 2000;
        settings.Monitoring.TemperatureUnit = TemperatureUnit.Fahrenheit;
        settings.Monitoring.DefaultWindow = HistoryWindow.Minutes15;
        settings.Dashboard.Cards[0].Visible = false;
        settings.Alerts.Rules[0].Threshold = 77;
        settings.Window = new WindowPlacement { Left = 10, Top = 20, Width = 1400, Height = 900, Maximized = true };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(ThemePreference.Light, loaded.Appearance.Theme);
        Assert.Equal("#45D6A0", loaded.Appearance.Accent);
        Assert.Equal(2000, loaded.Monitoring.PollingIntervalMs);
        Assert.Equal(TemperatureUnit.Fahrenheit, loaded.Monitoring.TemperatureUnit);
        Assert.Equal(HistoryWindow.Minutes15, loaded.Monitoring.DefaultWindow);
        Assert.False(loaded.Dashboard.Cards[0].Visible);
        Assert.Equal(77, loaded.Alerts.Rules[0].Threshold);
        Assert.True(loaded.Window!.Maximized);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Save_ReplacesExistingFileAtomically()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new AppSettings());
        var second = new AppSettings();
        second.Monitoring.PollingIntervalMs = 500;
        store.Save(second);
        Assert.Equal(500, store.Load().Monitoring.PollingIntervalMs);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults_AndKeepsBackup()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ this is not json");
        var settings = new SettingsStore(FilePath).Load();
        Assert.Equal(1000, settings.Monitoring.PollingIntervalMs);
        Assert.True(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void UnknownFields_AreIgnored_AndEnumsReadAsStrings()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            {
              "futureSetting": 42,
              "appearance": { "theme": "Light", "accent": "not-a-color" },
              "monitoring": { "pollingIntervalMs": 5, "temperatureUnit": "Fahrenheit" },
              // comments are tolerated
            }
            """);
        var settings = new SettingsStore(FilePath).Load();
        Assert.Equal(ThemePreference.Light, settings.Appearance.Theme);
        Assert.Equal(AccentPalette.Default, settings.Appearance.Accent);
        Assert.Equal(250, settings.Monitoring.PollingIntervalMs);
        Assert.Equal(TemperatureUnit.Fahrenheit, settings.Monitoring.TemperatureUnit);
    }

    [Fact]
    public void Serialize_WritesEnumsAsStrings()
    {
        string json = SettingsStore.Serialize(new AppSettings());
        Assert.Contains("\"theme\": \"Dark\"", json);
        Assert.Contains("\"pollingIntervalMs\": 1000", json);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // ignore
        }
    }
}

public class DashboardCatalogTests
{
    [Fact]
    public void Normalize_RemovesUnknownAndDuplicates_AndAppendsMissing()
    {
        var cards = new List<DashboardCardSetting>
        {
            new() { Id = DashboardCatalog.Network, Visible = false },
            new() { Id = "bogus", Visible = true },
            new() { Id = DashboardCatalog.Network, Visible = true },
            new() { Id = DashboardCatalog.CpuLoad, Visible = true },
        };

        var normalized = DashboardCatalog.Normalize(cards);

        Assert.Equal(DashboardCatalog.All.Count, normalized.Count);
        Assert.Equal(DashboardCatalog.Network, normalized[0].Id);
        Assert.False(normalized[0].Visible);
        Assert.Equal(DashboardCatalog.CpuLoad, normalized[1].Id);
        Assert.DoesNotContain(normalized, c => c.Id == "bogus");
        Assert.Equal(normalized.Count, normalized.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Normalize_HandlesNull()
    {
        Assert.Equal(DashboardCatalog.All.Count, DashboardCatalog.Normalize(null).Count);
    }
}

public class UpdateSettingsTests
{
    [Fact]
    public void IsCheckDue_FollowsFrequency()
    {
        var now = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        var s = new UpdateSettings { Frequency = UpdateCheckFrequency.Daily, LastCheckUtc = now.AddHours(-23) };
        Assert.False(s.IsCheckDue(now, isStartup: true));
        s.LastCheckUtc = now.AddHours(-25);
        Assert.True(s.IsCheckDue(now, isStartup: false));

        s.Frequency = UpdateCheckFrequency.OnStartup;
        Assert.True(s.IsCheckDue(now, isStartup: true));
        Assert.False(s.IsCheckDue(now, isStartup: false));

        s.Frequency = UpdateCheckFrequency.Never;
        Assert.False(s.IsCheckDue(now, isStartup: true));
    }
}
