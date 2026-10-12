using System.Runtime.InteropServices;

namespace Vigil.Hardware.Native;

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct PdhFmtCounterValue
{
    [FieldOffset(0)]
    public uint CStatus;

    [FieldOffset(8)]
    public double DoubleValue;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PdhFmtCounterValueItem
{
    public IntPtr Name;
    public PdhFmtCounterValue Value;
}

internal static partial class PdhNative
{
    public const uint ErrorSuccess = 0;
    public const uint PdhMoreData = 0x800007D2;
    public const uint PdhCStatusValidData = 0x0;
    public const uint PdhCStatusNewData = 0x1;
    public const uint PdhFmtDouble = 0x00000200;
    public const uint PdhFmtNoCap100 = 0x00008000;

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCollectQueryData(IntPtr query);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    public static partial uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCloseQuery(IntPtr query);
}

internal sealed class PdhException : Exception
{
    public PdhException(string message, uint status)
        : base($"{message} (PDH status 0x{status:X8})")
    {
        Status = status;
    }

    public uint Status { get; }
}

/// <summary>
/// A Performance Data Helper query. Counters are added by English path so they work on localized Windows.
/// Rate counters need two collections before they return valid data.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private IntPtr _query;

    private PdhQuery(IntPtr query)
    {
        _query = query;
    }

    public static PdhQuery Open()
    {
        uint status = PdhNative.PdhOpenQuery(null, IntPtr.Zero, out IntPtr query);
        if (status != PdhNative.ErrorSuccess)
        {
            throw new PdhException("PdhOpenQuery failed", status);
        }

        return new PdhQuery(query);
    }

    public PdhCounter? TryAddCounter(string englishPath, out uint status)
    {
        status = PdhNative.PdhAddEnglishCounter(_query, englishPath, IntPtr.Zero, out IntPtr counter);
        return status == PdhNative.ErrorSuccess ? new PdhCounter(counter, englishPath) : null;
    }

    public uint Collect() => _query == IntPtr.Zero ? 0xC0000BB0u : PdhNative.PdhCollectQueryData(_query);

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhNative.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }
}

internal sealed class PdhCounter
{
    private readonly IntPtr _counter;
    private IntPtr _buffer;
    private uint _bufferSize;

    public PdhCounter(IntPtr counter, string path)
    {
        _counter = counter;
        Path = path;
    }

    public string Path { get; }

    public bool TryGetValue(out double value, bool noCap100 = false)
    {
        uint format = PdhNative.PdhFmtDouble | (noCap100 ? PdhNative.PdhFmtNoCap100 : 0);
        uint status = PdhNative.PdhGetFormattedCounterValue(_counter, format, out _, out var result);
        if (status == PdhNative.ErrorSuccess && result.CStatus is PdhNative.PdhCStatusValidData or PdhNative.PdhCStatusNewData && double.IsFinite(result.DoubleValue))
        {
            value = result.DoubleValue;
            return true;
        }

        value = double.NaN;
        return false;
    }

    /// <summary>Reads all instances of a wildcard counter into <paramref name="results"/> (cleared first).</summary>
    public unsafe bool TryGetArray(List<KeyValuePair<string, double>> results, bool noCap100 = false)
    {
        results.Clear();
        uint format = PdhNative.PdhFmtDouble | (noCap100 ? PdhNative.PdhFmtNoCap100 : 0);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint size = _bufferSize;
            uint status = PdhNative.PdhGetFormattedCounterArray(_counter, format, ref size, out uint count, _buffer);
            if (status == PdhNative.PdhMoreData)
            {
                if (_buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_buffer);
                }

                _bufferSize = Math.Max(size, 1024u) * 2;
                _buffer = Marshal.AllocHGlobal((int)_bufferSize);
                continue;
            }

            if (status != PdhNative.ErrorSuccess)
            {
                return false;
            }

            var items = (PdhFmtCounterValueItem*)_buffer;
            for (int i = 0; i < count; i++)
            {
                var item = items[i];
                if (item.Value.CStatus is not (PdhNative.PdhCStatusValidData or PdhNative.PdhCStatusNewData) || !double.IsFinite(item.Value.DoubleValue))
                {
                    continue;
                }

                string name = Marshal.PtrToStringUni(item.Name) ?? string.Empty;
                results.Add(new KeyValuePair<string, double>(name, item.Value.DoubleValue));
            }

            return true;
        }

        return false;
    }

    ~PdhCounter()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }
}
