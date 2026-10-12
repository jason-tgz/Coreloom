using System.Diagnostics;
using Vigil.Core.Diagnostics;

namespace Vigil.App.Services;

/// <summary>
/// Ensures one Vigil per user session. A second launch signals the first instance to show its window and
/// exits. Restarts (e.g. "Restart as administrator") pass --wait-for &lt;pid&gt; so the new process waits
/// for the old one to exit before claiming the instance.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly string _mutexName;
    private readonly string _eventName;
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _registration;

    public SingleInstance(string id)
    {
        string user = Environment.UserName.Replace('\\', '_');
        _mutexName = $"Local\\{id}.Instance.{user}";
        _eventName = $"Local\\{id}.Activate.{user}";
    }

    /// <summary>Tries to become the primary instance.</summary>
    public bool TryAcquire()
    {
        bool created;
        try
        {
            _mutex = new Mutex(initiallyOwned: true, _mutexName, out created);
        }
        catch (UnauthorizedAccessException)
        {
            // The running instance is elevated and this one is not (or vice versa): it exists, so defer to it.
            return false;
        }

        if (!created)
        {
            try
            {
                // An abandoned mutex (crashed instance) can be taken over.
                created = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                created = true;
            }
        }

        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
        return true;
    }

    /// <summary>Asks the primary instance to show its window.</summary>
    public void SignalPrimary()
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(_eventName);
            handle.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            Log.Warn("SingleInstance", "Could not signal the running instance.", ex);
        }
    }

    /// <summary>Invokes <paramref name="onActivate"/> (on a thread-pool thread) when another launch signals.</summary>
    public void ListenForActivation(Action onActivate)
    {
        if (_activateEvent is null)
        {
            return;
        }

        _registration = ThreadPool.RegisterWaitForSingleObject(_activateEvent, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Waits for a previous instance to exit (used after restarts).</summary>
    public static void WaitForProcessExit(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            // Already exited.
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activateEvent?.Dispose();
        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned by this thread.
            }

            _mutex.Dispose();
        }
    }
}
