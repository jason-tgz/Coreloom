using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Coreloom.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public readonly record struct LogEntry(DateTime Time, LogLevel Level, string Category, string Message, Exception? Exception)
{
    public string Format()
    {
        var sb = new StringBuilder(128);
        sb.Append(Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        sb.Append(' ').Append(Level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            _ => "ERR",
        });
        sb.Append(" [").Append(Category).Append("] ").Append(Message);
        if (Exception is not null)
        {
            sb.Append(Environment.NewLine).Append("    ").Append(Exception.GetType().FullName).Append(": ").Append(Exception.Message);
            if (Exception.StackTrace is { } trace)
            {
                sb.Append(Environment.NewLine).Append(trace);
            }
        }

        return sb.ToString();
    }
}

public interface ILogSink
{
    void Write(in LogEntry entry);

    void Flush();
}

/// <summary>
/// Minimal, dependency-free logging facade. Sinks are added at startup; logging is a no-op until then.
/// </summary>
public static class Log
{
    private static readonly object SinkGate = new();
    private static ILogSink[] _sinks = [];
    private static readonly ConcurrentDictionary<string, ThrottleState> Throttles = new(StringComparer.Ordinal);

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static void AddSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (SinkGate)
        {
            _sinks = [.. _sinks, sink];
        }
    }

    public static void RemoveSink(ILogSink sink)
    {
        lock (SinkGate)
        {
            _sinks = _sinks.Where(s => !ReferenceEquals(s, sink)).ToArray();
        }
    }

    public static bool IsEnabled(LogLevel level) => level >= MinimumLevel;

    public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message, null);

    public static void Info(string category, string message) => Write(LogLevel.Info, category, message, null);

    public static void Warn(string category, string message, Exception? exception = null) => Write(LogLevel.Warning, category, message, exception);

    public static void Error(string category, string message, Exception? exception = null) => Write(LogLevel.Error, category, message, exception);

    /// <summary>
    /// Logs a warning at most once per <paramref name="window"/> for the given key, reporting how many
    /// repeats were suppressed. Used for polling failures so a broken sensor cannot flood the log.
    /// </summary>
    public static void WarnThrottled(string key, TimeSpan window, string category, string message, Exception? exception = null)
    {
        var now = DateTime.UtcNow;
        var state = Throttles.GetOrAdd(key, _ => new ThrottleState());
        int suppressed;
        lock (state)
        {
            if (state.LastLoggedUtc != DateTime.MinValue && now - state.LastLoggedUtc < window)
            {
                state.Suppressed++;
                return;
            }

            suppressed = state.Suppressed;
            state.Suppressed = 0;
            state.LastLoggedUtc = now;
        }

        Write(LogLevel.Warning, category, suppressed > 0 ? $"{message} (repeated {suppressed}× since last report)" : message, exception);
    }

    public static void Write(LogLevel level, string category, string message, Exception? exception)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var sinks = Volatile.Read(ref _sinks);
        if (sinks.Length == 0)
        {
            return;
        }

        var entry = new LogEntry(DateTime.Now, level, category, message, exception);
        foreach (var sink in sinks)
        {
            try
            {
                sink.Write(entry);
            }
            catch
            {
                // Logging must never throw.
            }
        }
    }

    public static void Flush()
    {
        foreach (var sink in Volatile.Read(ref _sinks))
        {
            try
            {
                sink.Flush();
            }
            catch
            {
                // ignore
            }
        }
    }

    private sealed class ThrottleState
    {
        public DateTime LastLoggedUtc = DateTime.MinValue;
        public int Suppressed;
    }
}

/// <summary>Keeps the most recent entries in memory for the in-app diagnostics view.</summary>
public sealed class MemoryLogSink : ILogSink
{
    private readonly int _capacity;
    private readonly Queue<LogEntry> _entries = new();
    private readonly object _gate = new();

    public MemoryLogSink(int capacity = 250)
    {
        _capacity = Math.Max(10, capacity);
    }

    public void Write(in LogEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    public void Flush()
    {
    }
}

/// <summary>
/// Buffered, size-rolling file log. Entries are queued and written by a timer every few seconds
/// (and immediately for errors) to keep disk writes to a minimum.
/// </summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private readonly string _directory;
    private readonly string _prefix;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly Timer _timer;
    private readonly object _writeGate = new();
    private bool _disposed;

    public FileLogSink(string directory, string prefix = "coreloom", long maxBytes = 2 * 1024 * 1024, int maxFiles = 5, TimeSpan? flushInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _prefix = prefix;
        _maxBytes = Math.Max(64 * 1024, maxBytes);
        _maxFiles = Math.Max(1, maxFiles);
        Directory.CreateDirectory(directory);
        var interval = flushInterval ?? TimeSpan.FromSeconds(5);
        _timer = new Timer(_ => Flush(), null, interval, interval);
    }

    public string CurrentFile => Path.Combine(_directory, $"{_prefix}.log");

    public void Write(in LogEntry entry)
    {
        if (_disposed)
        {
            return;
        }

        _queue.Enqueue(entry.Format());
        if (entry.Level >= LogLevel.Error)
        {
            Flush();
        }
    }

    public void Flush()
    {
        if (_queue.IsEmpty)
        {
            return;
        }

        lock (_writeGate)
        {
            try
            {
                RollIfNeeded();
                using var stream = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                while (_queue.TryDequeue(out var line))
                {
                    writer.WriteLine(line);
                }
            }
            catch
            {
                // Disk full / permissions: drop the log rather than crash the monitor.
                while (_queue.TryDequeue(out _))
                {
                }
            }
        }
    }

    private void RollIfNeeded()
    {
        var current = new FileInfo(CurrentFile);
        if (!current.Exists || current.Length < _maxBytes)
        {
            return;
        }

        for (int i = _maxFiles - 1; i >= 1; i--)
        {
            string older = Path.Combine(_directory, $"{_prefix}.{i}.log");
            string newer = i == 1 ? CurrentFile : Path.Combine(_directory, $"{_prefix}.{i - 1}.log");
            if (File.Exists(newer))
            {
                File.Copy(newer, older, overwrite: true);
            }
        }

        File.WriteAllText(CurrentFile, string.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        Flush();
    }
}
