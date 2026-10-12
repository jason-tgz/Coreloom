using System.Runtime.InteropServices;
using Vigil.Core.Hardware;
using Vigil.Hardware.Native;

namespace Vigil.Hardware.Processes;

/// <summary>
/// Samples per-process CPU time and memory for every process with a single NtQuerySystemInformation call
/// (no process handles are opened, so protected processes are included and no admin rights are needed).
/// Results are grouped by image name, like "chrome.exe ×24". Only used while a page displays it.
/// </summary>
public sealed unsafe class ProcessSampler : IProcessSampler
{
    private readonly object _gate = new();
    private Dictionary<(nint Pid, long CreateTime), long> _previousCpu = [];
    private long _previousTimestamp;
    private byte[] _buffer = new byte[512 * 1024];

    public IReadOnlyList<ProcessUsage> Sample()
    {
        lock (_gate)
        {
            if (!Query(out int length))
            {
                return [];
            }

            long now = Environment.TickCount64;
            double elapsedMs = _previousTimestamp == 0 ? 0 : now - _previousTimestamp;
            double capacity100ns = elapsedMs * 10_000.0 * Environment.ProcessorCount;
            var currentCpu = new Dictionary<(nint, long), long>(_previousCpu.Count + 16);
            var groups = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);

            fixed (byte* basePtr = _buffer)
            {
                int offset = 0;
                while (offset + sizeof(SystemProcessInformation) <= length)
                {
                    var info = *(SystemProcessInformation*)(basePtr + offset);
                    nint pid = info.UniqueProcessId;
                    if (pid != 0)
                    {
                        string name = info.ImageNameBuffer != IntPtr.Zero && info.ImageNameLength > 0
                            ? Marshal.PtrToStringUni(info.ImageNameBuffer, info.ImageNameLength / 2)
                            : "System";
                        long cpu = info.UserTime + info.KernelTime;
                        var key = (pid, info.CreateTime);
                        currentCpu[key] = cpu;

                        double cpuPercent = 0;
                        if (capacity100ns > 0 && _previousCpu.TryGetValue(key, out long previous) && cpu >= previous)
                        {
                            cpuPercent = (cpu - previous) * 100.0 / capacity100ns;
                        }

                        if (!groups.TryGetValue(name, out var acc))
                        {
                            acc = new Accumulator();
                            groups[name] = acc;
                        }

                        acc.Count++;
                        acc.Cpu += cpuPercent;
                        acc.WorkingSet += (long)info.WorkingSetSize;
                        acc.Private += (long)info.PrivatePageCount;
                    }

                    if (info.NextEntryOffset == 0)
                    {
                        break;
                    }

                    offset += (int)info.NextEntryOffset;
                }
            }

            _previousCpu = currentCpu;
            _previousTimestamp = now;

            return groups
                .Select(g => new ProcessUsage(g.Key, g.Value.Count, Math.Clamp(g.Value.Cpu, 0, 100), g.Value.WorkingSet, g.Value.Private))
                .ToArray();
        }
    }

    private bool Query(out int length)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            fixed (byte* p = _buffer)
            {
                int status = Win32.NtQuerySystemInformation(Win32.SystemProcessInformationClass, p, (uint)_buffer.Length, out uint needed);
                if (status == 0)
                {
                    length = (int)Math.Min(needed == 0 ? (uint)_buffer.Length : needed, (uint)_buffer.Length);
                    return true;
                }

                if (status != Win32.StatusInfoLengthMismatch)
                {
                    length = 0;
                    return false;
                }

                int next = (int)Math.Max(needed + (64 * 1024), (uint)_buffer.Length * 2);
                if (next > 64 * 1024 * 1024)
                {
                    length = 0;
                    return false;
                }

                _buffer = new byte[next];
            }
        }

        length = 0;
        return false;
    }

    private sealed class Accumulator
    {
        public int Count;
        public double Cpu;
        public long WorkingSet;
        public long Private;
    }
}
