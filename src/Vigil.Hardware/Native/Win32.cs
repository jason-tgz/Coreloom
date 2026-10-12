using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Vigil.Hardware.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct MemoryStatusEx
{
    public uint Length;
    public uint MemoryLoad;
    public ulong TotalPhys;
    public ulong AvailPhys;
    public ulong TotalPageFile;
    public ulong AvailPageFile;
    public ulong TotalVirtual;
    public ulong AvailVirtual;
    public ulong AvailExtendedVirtual;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PerformanceInformation
{
    public uint Size;
    public nuint CommitTotal;
    public nuint CommitLimit;
    public nuint CommitPeak;
    public nuint PhysicalTotal;
    public nuint PhysicalAvailable;
    public nuint SystemCache;
    public nuint KernelTotal;
    public nuint KernelPaged;
    public nuint KernelNonpaged;
    public nuint PageSize;
    public uint HandleCount;
    public uint ProcessCount;
    public uint ThreadCount;
}

/// <summary>SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION (NtQuerySystemInformation class 8).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemProcessorPerformanceInformation
{
    public long IdleTime;
    public long KernelTime;
    public long UserTime;
    public long DpcTime;
    public long InterruptTime;
    public uint InterruptCount;
}

/// <summary>SYSTEM_PROCESS_INFORMATION header (NtQuerySystemInformation class 5), x64 layout per phnt.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemProcessInformation
{
    public uint NextEntryOffset;
    public uint NumberOfThreads;
    public long WorkingSetPrivateSize;
    public uint HardFaultCount;
    public uint NumberOfThreadsHighWatermark;
    public ulong CycleTime;
    public long CreateTime;
    public long UserTime;
    public long KernelTime;
    public ushort ImageNameLength;
    public ushort ImageNameMaximumLength;
    public IntPtr ImageNameBuffer;
    public int BasePriority;
    public IntPtr UniqueProcessId;
    public IntPtr InheritedFromUniqueProcessId;
    public uint HandleCount;
    public uint SessionId;
    public nuint UniqueProcessKey;
    public nuint PeakVirtualSize;
    public nuint VirtualSize;
    public uint PageFaultCount;
    public nuint PeakWorkingSetSize;
    public nuint WorkingSetSize;
    public nuint QuotaPeakPagedPoolUsage;
    public nuint QuotaPagedPoolUsage;
    public nuint QuotaPeakNonPagedPoolUsage;
    public nuint QuotaNonPagedPoolUsage;
    public nuint PagefileUsage;
    public nuint PeakPagefileUsage;
    public nuint PrivatePageCount;
}

internal static partial class Win32
{
    public const uint FileShareRead = 0x1;
    public const uint FileShareWrite = 0x2;
    public const uint OpenExisting = 3;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    public const int SystemProcessInformationClass = 5;
    public const int SystemProcessorPerformanceInformationClass = 8;
    public const uint RsmbSignature = 0x52534D42; // 'RSMB'

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPerformanceInfo(ref PerformanceInformation info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static unsafe partial uint GetSystemFirmwareTable(uint providerSignature, uint tableId, byte* buffer, uint bufferSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetLogicalProcessorInformationEx(int relationship, byte* buffer, ref uint returnedLength);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, void* inBuffer, uint inBufferSize, void* outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

    [LibraryImport("ntdll.dll")]
    public static unsafe partial int NtQuerySystemInformation(int informationClass, void* buffer, uint length, out uint returnLength);

    /// <summary>Reads the raw SMBIOS table (no administrator rights required).</summary>
    public static unsafe byte[]? ReadSmbios()
    {
        uint size = GetSystemFirmwareTable(RsmbSignature, 0, null, 0);
        if (size == 0 || size > 4 * 1024 * 1024)
        {
            return null;
        }

        var buffer = new byte[size];
        fixed (byte* p = buffer)
        {
            uint written = GetSystemFirmwareTable(RsmbSignature, 0, p, size);
            if (written == 0 || written > size)
            {
                return null;
            }
        }

        return buffer;
    }

    /// <summary>Counts physical cores and sums L3 cache via GetLogicalProcessorInformationEx.</summary>
    public static unsafe (int PhysicalCores, long L3CacheBytes, int PerformanceCores, int EfficiencyCores) ReadProcessorTopology()
    {
        const int relationAll = 0xFFFF;
        const int relationProcessorCore = 0;
        const int relationCache = 2;
        uint length = 0;
        GetLogicalProcessorInformationEx(relationAll, null, ref length);
        if (length == 0 || length > 16 * 1024 * 1024)
        {
            return (0, 0, 0, 0);
        }

        var buffer = new byte[length];
        fixed (byte* p = buffer)
        {
            if (!GetLogicalProcessorInformationEx(relationAll, p, ref length))
            {
                return (0, 0, 0, 0);
            }

            int cores = 0;
            long l3 = 0;
            var efficiencyClasses = new List<byte>();
            uint offset = 0;
            while (offset + 8 <= length)
            {
                int relationship = *(int*)(p + offset);
                uint size = *(uint*)(p + offset + 4);
                if (size == 0)
                {
                    break;
                }

                if (relationship == relationProcessorCore)
                {
                    cores++;
                    efficiencyClasses.Add(*(p + offset + 8 + 1)); // PROCESSOR_RELATIONSHIP.EfficiencyClass
                }
                else if (relationship == relationCache)
                {
                    byte level = *(p + offset + 8);
                    uint cacheSize = *(uint*)(p + offset + 8 + 4);
                    if (level == 3)
                    {
                        l3 += cacheSize;
                    }
                }

                offset += size;
            }

            // On hybrid CPUs (Intel P/E cores) the highest efficiency class marks performance cores.
            int performance = 0, efficiency = 0;
            if (efficiencyClasses.Count > 0 && efficiencyClasses.Distinct().Count() > 1)
            {
                byte max = efficiencyClasses.Max();
                performance = efficiencyClasses.Count(c => c == max);
                efficiency = efficiencyClasses.Count - performance;
            }

            return (cores, l3, performance, efficiency);
        }
    }

    /// <summary>Opens a device for IOCTL queries without requesting read/write access (works without admin).</summary>
    public static SafeFileHandle OpenDeviceForQuery(string path, uint access = 0) =>
        CreateFile(path, access, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
}
