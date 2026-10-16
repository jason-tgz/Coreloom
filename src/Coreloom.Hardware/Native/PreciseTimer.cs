using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Coreloom.Hardware.Native;

/// <summary>
/// A timer for steady redraws: a high-resolution waitable timer (Windows 10 1803 and later), which wakes within a
/// fraction of a millisecond of its time without raising the whole system's timer resolution. Ordinary timers (and the
/// sleeps and timed waits that use them) only wake on the system's 15.6 ms tick, so anything paced by them steps
/// unevenly. Falls back to an ordinary waitable timer on older Windows. One thread waits on it at a time.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class PreciseTimer : IDisposable
{
    private const uint HighResolution = 0x00000002; // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
    private const uint AllAccess = 0x001F0003;      // TIMER_ALL_ACCESS

    private readonly TimerHandle _handle;
    private readonly WaitHandle[] _handles;

    /// <summary>A timer; <paramref name="others"/> are handles that also end a wait (to stop, or to redraw now).</summary>
    public PreciseTimer(params WaitHandle[] others)
    {
        IntPtr handle = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, HighResolution, AllAccess);
        IsHighResolution = handle != IntPtr.Zero;
        if (!IsHighResolution)
        {
            handle = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, 0, AllAccess);
        }

        if (handle == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Couldn't create a timer.");
        }

        _handle = new TimerHandle(new SafeWaitHandle(handle, ownsHandle: true));
        _handles = new WaitHandle[others.Length + 1];
        _handles[0] = _handle;
        others.CopyTo(_handles, 1);
    }

    /// <summary>The timer wakes precisely (false on Windows older than 10 version 1803).</summary>
    public bool IsHighResolution { get; }

    /// <summary>
    /// Waits until <paramref name="deadline"/> (a <see cref="Stopwatch"/> timestamp) or until one of the other handles is
    /// signalled. Returns -1 when the time came, otherwise the index (among the other handles) of the one signalled.
    /// </summary>
    public int WaitUntil(long deadline)
    {
        long remaining = deadline - Stopwatch.GetTimestamp();
        long due = -Math.Max(1, (long)(remaining * (10_000_000.0 / Stopwatch.Frequency))); // relative, in 100 ns
        if (!SetWaitableTimer(_handle.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Couldn't set the timer.");
        }

        int signalled = WaitHandle.WaitAny(_handles);
        return signalled == 0 ? -1 : signalled - 1;
    }

    public void Dispose() => _handle.Dispose();

    private sealed class TimerHandle : WaitHandle
    {
        public TimerHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateWaitableTimerExW(IntPtr attributes, IntPtr name, uint flags, uint access);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr completion, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
