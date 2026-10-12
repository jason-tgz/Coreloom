using Coreloom.Core.Alerts;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Tests;

public class AlertEngineTests
{
    private readonly MetricCatalog _catalog = new();
    private readonly MetricHandle _handle;

    public AlertEngineTests()
    {
        _handle = _catalog.Register(new MetricDescriptor("gpu/0/temp", "GPU", MetricCategory.Gpu, MetricKind.Temperature), "test");
    }

    private TelemetryFrame Frame(long t, double? value)
    {
        var samples = new Dictionary<string, MetricSample>();
        if (value is double v)
        {
            samples["gpu/0/temp"] = new MetricSample(v, _handle);
        }

        return new TelemetryFrame(t, t, DateTime.Now, TimeSpan.Zero, samples);
    }

    private static AlertEngine CreateEngine(int sustainSeconds = 5, double hysteresis = 3, int cooldownMinutes = 10)
    {
        var engine = new AlertEngine(TimeSpan.FromMinutes(cooldownMinutes));
        engine.SetRules([new AlertRule { Id = "r", Name = "GPU hot", MetricId = "gpu/0/temp", Threshold = 80, SustainSeconds = sustainSeconds, Hysteresis = hysteresis }]);
        return engine;
    }

    [Fact]
    public void FiresOnlyAfterSustainTime()
    {
        var engine = CreateEngine(sustainSeconds: 5);
        Assert.Empty(engine.Evaluate(Frame(0, 85)));
        Assert.Empty(engine.Evaluate(Frame(4000, 85)));
        var fired = engine.Evaluate(Frame(5000, 86));
        Assert.Single(fired);
        Assert.Equal(86, fired[0].Value);
    }

    [Fact]
    public void ShortSpikes_DoNotFire()
    {
        var engine = CreateEngine(sustainSeconds: 5);
        engine.Evaluate(Frame(0, 85));
        engine.Evaluate(Frame(3000, 70));
        engine.Evaluate(Frame(4000, 85));
        Assert.Empty(engine.Evaluate(Frame(8000, 85)));
        Assert.Single(engine.Evaluate(Frame(9000, 85)));
    }

    [Fact]
    public void DoesNotRefire_UntilRecoveredBelowHysteresis_AndCooldownElapsed()
    {
        var engine = CreateEngine(sustainSeconds: 0, hysteresis: 3, cooldownMinutes: 1);
        Assert.Single(engine.Evaluate(Frame(0, 81)));

        // Still hot: no repeat.
        Assert.Empty(engine.Evaluate(Frame(1000, 90)));
        // Dips just below threshold but not below threshold - hysteresis: not re-armed.
        Assert.Empty(engine.Evaluate(Frame(2000, 79)));
        Assert.Empty(engine.Evaluate(Frame(120_000, 81)));
        // Recovers properly, then breaches again after cooldown.
        Assert.Empty(engine.Evaluate(Frame(121_000, 76)));
        Assert.Single(engine.Evaluate(Frame(122_000, 82)));
    }

    [Fact]
    public void Cooldown_SuppressesRepeatsEvenWhenRearmed()
    {
        var engine = CreateEngine(sustainSeconds: 0, hysteresis: 1, cooldownMinutes: 10);
        Assert.Single(engine.Evaluate(Frame(0, 85)));
        engine.Evaluate(Frame(1000, 70));
        Assert.Empty(engine.Evaluate(Frame(2000, 85)));
        Assert.Single(engine.Evaluate(Frame(601_000, 85)));
    }

    [Fact]
    public void MissingData_ResetsSustainTimer()
    {
        var engine = CreateEngine(sustainSeconds: 5);
        engine.Evaluate(Frame(0, 85));
        engine.Evaluate(Frame(3000, null));
        Assert.Empty(engine.Evaluate(Frame(5000, 85)));
        Assert.Single(engine.Evaluate(Frame(10_000, 85)));
    }

    [Fact]
    public void DisabledEngineOrRule_NeverFires()
    {
        var engine = CreateEngine(sustainSeconds: 0);
        engine.Enabled = false;
        Assert.Empty(engine.Evaluate(Frame(0, 99)));

        engine.Enabled = true;
        engine.SetRules([new AlertRule { Id = "r", MetricId = "gpu/0/temp", Threshold = 80, SustainSeconds = 0, Enabled = false }]);
        Assert.Empty(engine.Evaluate(Frame(1000, 99)));
    }

    [Fact]
    public void BelowComparison_Works()
    {
        var engine = new AlertEngine(TimeSpan.FromMinutes(1));
        engine.SetRules([new AlertRule { Id = "low", MetricId = "gpu/0/temp", Comparison = AlertComparison.Below, Threshold = 10, SustainSeconds = 0 }]);
        Assert.Empty(engine.Evaluate(Frame(0, 20)));
        Assert.Single(engine.Evaluate(Frame(1000, 5)));
    }

    [Fact]
    public void AlertRaisedEvent_IsInvoked()
    {
        var engine = CreateEngine(sustainSeconds: 0);
        AlertEvent? received = null;
        engine.AlertRaised += e => received = e;
        engine.Evaluate(Frame(0, 99));
        Assert.NotNull(received);
        Assert.Equal("GPU hot", received!.Rule.Name);
        Assert.Equal(MetricKind.Temperature, received.Metric!.Kind);
    }
}
