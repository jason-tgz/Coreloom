using System.Runtime.InteropServices;

namespace Coreloom.App.Overlay;

/// <summary>
/// The parts of the Event Tracing for Windows API (advapi32) a real-time session needs. Struct layouts follow
/// evntrace.h/evntcons.h for 64-bit Windows; <see cref="LayoutIsValid"/> checks the sizes at runtime before anything
/// is started.
/// </summary>
internal static unsafe partial class EtwNative
{
    public const uint ErrorSuccess = 0;
    public const uint ErrorAccessDenied = 5;
    public const uint ErrorInvalidParameter = 87;
    public const uint ErrorAlreadyExists = 183;
    public const uint ErrorCancelled = 1223;

    public const uint WnodeFlagTracedGuid = 0x00020000;
    public const uint EventTraceRealTimeMode = 0x00000100;
    public const uint EventTraceUseMsFlushTimer = 0x00000010;
    public const uint EventTraceNoPerProcessorBuffering = 0x10000000;
    public const uint EventTraceControlStop = 1;
    public const uint EventControlCodeEnableProvider = 1;
    public const byte TraceLevelInformation = 4;

    public const uint ProcessTraceModeRealTime = 0x00000100;
    public const uint ProcessTraceModeRawTimestamp = 0x00001000;
    public const uint ProcessTraceModeEventRecord = 0x10000000;

    /// <summary>OpenTrace's failure value on 64-bit Windows.</summary>
    public const ulong InvalidProcessTraceHandle = ulong.MaxValue;

    /// <summary>Sizes from the Windows SDK headers (x64).</summary>
    public static bool LayoutIsValid =>
        IntPtr.Size == 8
        && sizeof(EventTraceProperties) == 120
        && sizeof(EventTraceLogfile) == 448
        && sizeof(EventHeader) == 80
        && Marshal.OffsetOf<EventTraceLogfile>(nameof(EventTraceLogfile.EventRecordCallback)) == 424
        && Marshal.OffsetOf<EventHeader>(nameof(EventHeader.ProviderId)) == 24;

    [StructLayout(LayoutKind.Sequential)]
    public struct WnodeHeader
    {
        public uint BufferSize;
        public uint ProviderId;
        public ulong HistoricalContext;
        public long TimeStamp;
        public Guid Guid;
        public uint ClientContext;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventTraceProperties
    {
        public WnodeHeader Wnode;
        public uint BufferSize;
        public uint MinimumBuffers;
        public uint MaximumBuffers;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint FlushTimer;
        public uint EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers;
        public uint FreeBuffers;
        public uint EventsLost;
        public uint BuffersWritten;
        public uint LogBuffersLost;
        public uint RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset;
        public uint LoggerNameOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventTraceHeader
    {
        public ushort Size;
        public ushort FieldTypeFlags;
        public uint Version;
        public uint ThreadId;
        public uint ProcessId;
        public long TimeStamp;
        public Guid Guid;
        public ulong ProcessorTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventTrace
    {
        public EventTraceHeader Header;
        public uint InstanceId;
        public uint ParentInstanceId;
        public Guid ParentGuid;
        public IntPtr MofData;
        public uint MofLength;
        public uint ClientContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TimeZoneInformation
    {
        public int Bias;
        public fixed ushort StandardName[32];
        public SystemTime StandardDate;
        public int StandardBias;
        public fixed ushort DaylightName[32];
        public SystemTime DaylightDate;
        public int DaylightBias;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TraceLogfileHeader
    {
        public uint BufferSize;
        public uint Version;
        public uint ProviderVersion;
        public uint NumberOfProcessors;
        public long EndTime;
        public uint TimerResolution;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint BuffersWritten;
        public Guid LogInstanceGuid;
        public IntPtr LoggerName;
        public IntPtr LogFileName;
        public TimeZoneInformation TimeZone;
        public long BootTime;
        public long PerfFreq;
        public long StartTime;
        public uint ReservedFlags;
        public uint BuffersLost;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventTraceLogfile
    {
        public IntPtr LogFileName;
        public IntPtr LoggerName;
        public long CurrentTime;
        public uint BuffersRead;
        public uint ProcessTraceMode;
        public EventTrace CurrentEvent;
        public TraceLogfileHeader LogfileHeader;
        public IntPtr BufferCallback;
        public uint BufferSize;
        public uint Filled;
        public uint EventsLost;
        public IntPtr EventRecordCallback;
        public uint IsKernelTrace;
        public IntPtr Context;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventDescriptor
    {
        public ushort Id;
        public byte Version;
        public byte Channel;
        public byte Level;
        public byte Opcode;
        public ushort Task;
        public ulong Keyword;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventHeader
    {
        public ushort Size;
        public ushort HeaderType;
        public ushort Flags;
        public ushort EventProperty;
        public uint ThreadId;
        public uint ProcessId;
        public long TimeStamp;
        public Guid ProviderId;
        public EventDescriptor EventDescriptor;
        public ulong ProcessorTime;
        public Guid ActivityId;
    }

    /// <summary>The start of an EVENT_RECORD (the header is all the frame counter reads).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EventRecord
    {
        public EventHeader EventHeader;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "StartTraceW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint StartTrace(out ulong sessionHandle, string sessionName, EventTraceProperties* properties);

    [LibraryImport("advapi32.dll", EntryPoint = "ControlTraceW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ControlTrace(ulong sessionHandle, string? sessionName, EventTraceProperties* properties, uint controlCode);

    [LibraryImport("advapi32.dll")]
    public static partial uint EnableTraceEx2(ulong traceHandle, Guid* providerId, uint controlCode, byte level, ulong matchAnyKeyword,
        ulong matchAllKeyword, uint timeout, IntPtr enableParameters);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenTraceW", SetLastError = true)]
    public static partial ulong OpenTrace(EventTraceLogfile* logfile);

    [LibraryImport("advapi32.dll")]
    public static partial uint ProcessTrace(ulong* handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

    [LibraryImport("advapi32.dll")]
    public static partial uint CloseTrace(ulong traceHandle);
}
