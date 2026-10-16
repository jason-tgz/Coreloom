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
/// <para>
/// Some PCs (older Windows, some virtual machines) don't keep the high-resolution promise. The timer notices when its
/// short waits keep waking late and then asks Windows for a 1 ms timer for this process, only while it is doing short
/// waits (so not while it idles), and gives it back when disposed. Games ask for the same, so it costs nothing extra
/// while one runs.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class PreciseTimer : IDisposable
{
    private const uint HighResolution = 0x00000002; // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
    private const uint AllAccess = 0x001F0003;      // TIMER_ALL_ACCESS
    private const double ShortWaitMilliseconds = 50;
    private const double LateMilliseconds = 2.5;
    private const int WakesToJudge = 16;

    private readonly TimerHandle _handle;
    private readonly WaitHandle[] _handles;
    private double _averageLate;
    private int _judged;
    private bool _coarse;
    private bool _raised;

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
        _coarse = !IsHighResolution;
    }

    /// <summary>The timer was created as a high-resolution one (false on Windows older than 10 version 1803).</summary>
    public bool IsHighResolution { get; }

    /// <summary>Its short waits woke late, so it uses a 1 ms system timer while doing them.</summary>
    public bool UsesFineSystemTimer => _coarse;

    /// <summary>
    /// Waits until <paramref name="deadline"/> (a <see cref="Stopwatch"/> timestamp) or until one of the other handles is
    /// signalled. Returns -1 when the time came, otherwise the index (among the other handles) of the one signalled.
    /// </summary>
    public int WaitUntil(long deadline)
    {
        long start = Stopwatch.GetTimestamp();
        long remaining = deadline - start;
        double remainingMs = remaining * 1000.0 / Stopwatch.Frequency;
        bool shortWait = remainingMs < ShortWaitMilliseconds;
        SetFineSystemTimer(_coarse && shortWait);

        long due = -Math.Max(1, (long)(remaining * (10_000_000.0 / Stopwatch.Frequency))); // relative, in 100 ns
        if (!SetWaitableTimer(_handle.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Couldn't set the timer.");
        }

        int signalled = WaitHandle.WaitAny(_handles);
        if (signalled == 0 && shortWait && !_coarse)
        {
            Judge((Stopwatch.GetTimestamp() - Math.Max(deadline, start)) * 1000.0 / Stopwatch.Frequency);
        }

        return signalled == 0 ? -1 : signalled - 1;
    }

    public void Dispose()
    {
        SetFineSystemTimer(false);
        _handle.Dispose();
    }

    /// <summary>Keeps a running average of how late short waits wake; late on average means the timer is coarse.</summary>
    private void Judge(double lateMilliseconds)
    {
        _judged++;
        _averageLate = _judged == 1 ? lateMilliseconds : (_averageLate * 0.85) + (Math.Max(0, lateMilliseconds) * 0.15);
        if (_judged >= WakesToJudge && _averageLate > LateMilliseconds)
        {
            _coarse = true;
        }
    }

    private void SetFineSystemTimer(bool fine)
    {
        if (fine == _raised)
        {
            return;
        }

        if (fine)
        {
            _raised = TimeBeginPeriod(1) == 0;
        }
        else
        {
            TimeEndPeriod(1);
            _raised = false;
        }
    }

    private sealed class TimerHandle : WaitHandle
    {
        public TimerHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint period);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint period);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateWaitableTimerExW(IntPtr attributes, IntPtr name, uint flags, uint access);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr completion, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
