using Vigil.Core.Telemetry;

namespace Vigil.Core.Alerts;

public enum AlertComparison
{
    Above,
    Below,
}

/// <summary>
/// A user-configurable threshold notification. Defaults are conservative reminders chosen by the user,
/// not statements about safe operating limits of any specific hardware.
/// </summary>
public sealed class AlertRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Alert";

    public string MetricId { get; set; } = string.Empty;

    public AlertComparison Comparison { get; set; } = AlertComparison.Above;

    /// <summary>Threshold in the metric's base unit (°C for temperatures, % for loads).</summary>
    public double Threshold { get; set; }

    /// <summary>The condition must hold continuously for this long before notifying.</summary>
    public int SustainSeconds { get; set; } = 10;

    /// <summary>After notifying, the value must recover by this amount before the rule re-arms.</summary>
    public double Hysteresis { get; set; } = 3;

    public bool Enabled { get; set; } = true;

    public AlertRule Clone() => (AlertRule)MemberwiseClone();
}

public sealed record AlertEvent(AlertRule Rule, MetricDescriptor? Metric, double Value, DateTime LocalTime);

/// <summary>
/// Evaluates alert rules against telemetry frames. Implements sustain time, hysteresis (re-arm) and a
/// global per-rule cooldown so that a value hovering around a threshold cannot spam notifications.
/// Thread-safe: evaluation and rule updates may happen on different threads.
/// </summary>
public sealed class AlertEngine
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RuleState> _states = new(StringComparer.Ordinal);
    private IReadOnlyList<AlertRule> _rules = [];

    public AlertEngine(TimeSpan cooldown)
    {
        Cooldown = cooldown;
    }

    public event Action<AlertEvent>? AlertRaised;

    public bool Enabled { get; set; } = true;

    public TimeSpan Cooldown { get; set; }

    public IReadOnlyList<AlertRule> Rules
    {
        get { lock (_gate) return _rules; }
    }

    public void SetRules(IEnumerable<AlertRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var copy = rules.Select(r => r.Clone()).ToArray();
        lock (_gate)
        {
            _rules = copy;
            var ids = copy.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var stale in _states.Keys.Where(k => !ids.Contains(k)).ToArray())
            {
                _states.Remove(stale);
            }
        }
    }

    /// <summary>Evaluates all rules for the frame and returns (and raises) any alerts that fired.</summary>
    public IReadOnlyList<AlertEvent> Evaluate(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!Enabled)
        {
            return [];
        }

        List<AlertEvent>? fired = null;
        lock (_gate)
        {
            long now = frame.Timestamp;
            foreach (var rule in _rules)
            {
                if (!rule.Enabled || string.IsNullOrEmpty(rule.MetricId))
                {
                    continue;
                }

                if (!_states.TryGetValue(rule.Id, out var state))
                {
                    state = new RuleState();
                    _states[rule.Id] = state;
                }

                if (!frame.TryGetSample(rule.MetricId, out var sample))
                {
                    // No data: do not treat as breach, and restart the sustain timer.
                    state.BreachStartedAt = null;
                    continue;
                }

                double value = sample.Value;
                bool breached = rule.Comparison == AlertComparison.Above ? value > rule.Threshold : value < rule.Threshold;
                if (breached)
                {
                    state.BreachStartedAt ??= now;
                    bool sustained = now - state.BreachStartedAt.Value >= rule.SustainSeconds * 1000L;
                    bool cooledDown = state.LastFiredAt is null || now - state.LastFiredAt.Value >= (long)Cooldown.TotalMilliseconds;
                    if (state.Armed && sustained && cooledDown)
                    {
                        state.Armed = false;
                        state.LastFiredAt = now;
                        (fired ??= []).Add(new AlertEvent(rule, sample.Source.Descriptor, value, frame.LocalTime));
                    }
                }
                else
                {
                    state.BreachStartedAt = null;
                    bool recovered = rule.Comparison == AlertComparison.Above
                        ? value <= rule.Threshold - rule.Hysteresis
                        : value >= rule.Threshold + rule.Hysteresis;
                    if (recovered)
                    {
                        state.Armed = true;
                    }
                }
            }
        }

        if (fired is null)
        {
            return [];
        }

        foreach (var alert in fired)
        {
            AlertRaised?.Invoke(alert);
        }

        return fired;
    }

    /// <summary>Clears sustain/cooldown state (e.g. after the user edits thresholds).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _states.Clear();
        }
    }

    private sealed class RuleState
    {
        public long? BreachStartedAt;
        public long? LastFiredAt;
        public bool Armed = true;
    }
}
