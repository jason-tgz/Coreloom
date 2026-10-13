using System.Runtime.InteropServices;

namespace Coreloom.App.Interop;

/// <summary>How long the signed-in user has been away from the keyboard and mouse (used to time background updates).</summary>
internal static partial class UserIdle
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);

    /// <summary>Time since the last keyboard or mouse input in this session, or zero when it can't be read (treated as "in use").</summary>
    public static TimeSpan SinceLastInput()
    {
        var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        // Both values are milliseconds since boot in 32 bits; unsigned subtraction handles the 49.7-day wrap.
        uint idle = unchecked((uint)Environment.TickCount - info.DwTime);
        return TimeSpan.FromMilliseconds(idle);
    }
}
