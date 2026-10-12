using System.Runtime.InteropServices;
using System.Text;

namespace Vigil.Hardware.Native;

/// <summary>NVML return codes (nvml.h).</summary>
internal enum NvmlReturn
{
    Success = 0,
    Uninitialized = 1,
    InvalidArgument = 2,
    NotSupported = 3,
    NoPermission = 4,
    AlreadyInitialized = 5,
    NotFound = 6,
    InsufficientSize = 7,
    InsufficientPower = 8,
    DriverNotLoaded = 9,
    Timeout = 10,
    IrqIssue = 11,
    LibraryNotFound = 12,
    FunctionNotFound = 13,
    CorruptedInforom = 14,
    GpuIsLost = 15,
    ResetRequired = 16,
    OperatingSystem = 17,
    LibRmVersionMismatch = 18,
    InUse = 19,
    Memory = 20,
    NoData = 21,
    NotReady = 27,
    GpuNotFound = 28,
    InvalidState = 29,
    Unknown = 999,
}

internal enum NvmlClockType
{
    Graphics = 0,
    Sm = 1,
    Memory = 2,
    Video = 3,
}

internal enum NvmlTemperatureThreshold
{
    Shutdown = 0,
    Slowdown = 1,
    MemoryMax = 2,
    GpuMax = 3,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvmlUtilization
{
    public uint Gpu;
    public uint Memory;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NvmlMemory
{
    public ulong Total;
    public ulong Free;
    public ulong Used;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NvmlPciInfo
{
    public fixed byte BusIdLegacy[16];
    public uint Domain;
    public uint Bus;
    public uint Device;
    public uint PciDeviceId;
    public uint PciSubSystemId;
    public fixed byte BusId[32];
}

/// <summary>Thrown when NVML reports a condition that requires re-initialization (driver reset/update, GPU lost).</summary>
internal sealed class NvmlFatalException : Exception
{
    public NvmlFatalException(string function, NvmlReturn code, string message)
        : base($"{function} failed: {message} ({code})")
    {
        Code = code;
    }

    public NvmlReturn Code { get; }
}

/// <summary>
/// Minimal NVIDIA Management Library binding. nvml.dll ships with the NVIDIA driver (System32), so nothing
/// is redistributed. Exports are resolved individually: functions missing from older drivers are reported
/// as <see cref="NvmlReturn.FunctionNotFound"/> instead of crashing. Only read-only queries are bound.
/// </summary>
internal sealed unsafe class NvmlLibrary : IDisposable
{
    private IntPtr _handle;
    private bool _initialized;

    private readonly delegate* unmanaged<int> _init;
    private readonly delegate* unmanaged<int> _shutdown;
    private readonly delegate* unmanaged<int, byte*> _errorString;
    private readonly delegate* unmanaged<byte*, uint, int> _systemGetDriverVersion;
    private readonly delegate* unmanaged<byte*, uint, int> _systemGetNvmlVersion;
    private readonly delegate* unmanaged<uint*, int> _deviceGetCount;
    private readonly delegate* unmanaged<uint, IntPtr*, int> _deviceGetHandleByIndex;
    private readonly delegate* unmanaged<IntPtr, byte*, uint, int> _deviceGetName;
    private readonly delegate* unmanaged<IntPtr, byte*, uint, int> _deviceGetUuid;
    private readonly delegate* unmanaged<IntPtr, byte*, uint, int> _deviceGetVbiosVersion;
    private readonly delegate* unmanaged<IntPtr, NvmlPciInfo*, int> _deviceGetPciInfo;
    private readonly delegate* unmanaged<IntPtr, NvmlUtilization*, int> _deviceGetUtilizationRates;
    private readonly delegate* unmanaged<IntPtr, int, uint*, int> _deviceGetTemperature;
    private readonly delegate* unmanaged<IntPtr, int, uint*, int> _deviceGetTemperatureThreshold;
    private readonly delegate* unmanaged<IntPtr, int, uint*, int> _deviceGetClockInfo;
    private readonly delegate* unmanaged<IntPtr, int, uint*, int> _deviceGetMaxClockInfo;
    private readonly delegate* unmanaged<IntPtr, NvmlMemory*, int> _deviceGetMemoryInfo;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _deviceGetPowerUsage;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _deviceGetEnforcedPowerLimit;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _deviceGetPowerManagementDefaultLimit;
    private readonly delegate* unmanaged<IntPtr, uint*, uint*, int> _deviceGetPowerManagementLimitConstraints;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _deviceGetNumFans;
    private readonly delegate* unmanaged<IntPtr, uint, uint*, int> _deviceGetFanSpeedV2;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _deviceGetFanSpeed;
    private readonly delegate* unmanaged<IntPtr, int*, int> _deviceGetPerformanceState;
    private readonly delegate* unmanaged<IntPtr, uint*, uint*, int> _deviceGetEncoderUtilization;
    private readonly delegate* unmanaged<IntPtr, uint*, uint*, int> _deviceGetDecoderUtilization;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _deviceGetCurrPcieLinkGeneration;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _deviceGetCurrPcieLinkWidth;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _deviceGetMaxPcieLinkGeneration;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _deviceGetMaxPcieLinkWidth;
    private readonly delegate* unmanaged<IntPtr, ulong*, int> _deviceGetClockEventReasons;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _deviceGetMemoryBusWidth;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _deviceGetNumGpuCores;

    private NvmlLibrary(IntPtr handle, string path)
    {
        _handle = handle;
        LibraryPath = path;
        _init = (delegate* unmanaged<int>)Export("nvmlInit_v2");
        _shutdown = (delegate* unmanaged<int>)Export("nvmlShutdown");
        _errorString = (delegate* unmanaged<int, byte*>)Export("nvmlErrorString");
        _systemGetDriverVersion = (delegate* unmanaged<byte*, uint, int>)Export("nvmlSystemGetDriverVersion");
        _systemGetNvmlVersion = (delegate* unmanaged<byte*, uint, int>)Export("nvmlSystemGetNVMLVersion");
        _deviceGetCount = (delegate* unmanaged<uint*, int>)Export("nvmlDeviceGetCount_v2");
        _deviceGetHandleByIndex = (delegate* unmanaged<uint, IntPtr*, int>)Export("nvmlDeviceGetHandleByIndex_v2");
        _deviceGetName = (delegate* unmanaged<IntPtr, byte*, uint, int>)Export("nvmlDeviceGetName");
        _deviceGetUuid = (delegate* unmanaged<IntPtr, byte*, uint, int>)Export("nvmlDeviceGetUUID");
        _deviceGetVbiosVersion = (delegate* unmanaged<IntPtr, byte*, uint, int>)Export("nvmlDeviceGetVbiosVersion");
        _deviceGetPciInfo = (delegate* unmanaged<IntPtr, NvmlPciInfo*, int>)Export("nvmlDeviceGetPciInfo_v3");
        _deviceGetUtilizationRates = (delegate* unmanaged<IntPtr, NvmlUtilization*, int>)Export("nvmlDeviceGetUtilizationRates");
        _deviceGetTemperature = (delegate* unmanaged<IntPtr, int, uint*, int>)Export("nvmlDeviceGetTemperature");
        _deviceGetTemperatureThreshold = (delegate* unmanaged<IntPtr, int, uint*, int>)Export("nvmlDeviceGetTemperatureThreshold");
        _deviceGetClockInfo = (delegate* unmanaged<IntPtr, int, uint*, int>)Export("nvmlDeviceGetClockInfo");
        _deviceGetMaxClockInfo = (delegate* unmanaged<IntPtr, int, uint*, int>)Export("nvmlDeviceGetMaxClockInfo");
        _deviceGetMemoryInfo = (delegate* unmanaged<IntPtr, NvmlMemory*, int>)Export("nvmlDeviceGetMemoryInfo");
        _deviceGetPowerUsage = (delegate* unmanaged<IntPtr, uint*, int>)Export("nvmlDeviceGetPowerUsage");
        _deviceGetEnforcedPowerLimit = (delegate* unmanaged<IntPtr, uint*, int>)Export("nvmlDeviceGetEnforcedPowerLimit");
        _deviceGetPowerManagementDefaultLimit = (delegate* unmanaged<IntPtr, uint*, int>)Export("nvmlDeviceGetPowerManagementDefaultLimit");
        _deviceGetPowerManagementLimitConstraints = (delegate* unmanaged<IntPtr, uint*, uint*, int>)Export("nvmlDeviceGetPowerManagementLimitConstraints");
        _deviceGetNumFans = (delegate* unmanaged<IntPtr, uint*, int>)Export("nvmlDeviceGetNumFans");
        _deviceGetFanSpeedV2 = (delegate* unmanaged<IntPtr, uint, uint*, int>)Export("nvmlDeviceGetFanSpeed_v2");
        _deviceGetFanSpeed = (delegate* unmanaged<IntPtr, uint*, int>)Export("nvmlDeviceGetFanSpeed");
        _deviceGetPerformanceState = (delegate* unmanaged<IntPtr, int*, int>)Export("nvmlDeviceGetPerformanceState");
        _deviceGetEncoderUtilization = (delegate* unmanaged<IntPtr, uint*, uint*, int>)Export("nvmlDeviceGetEncoderUtilization");
        _deviceGetDecoderUtilization = (delegate* unmanaged<IntPtr, uint*, uint*, int>)Export("nvmlDeviceGetDecoderUtilization");
        _deviceGetCurrPcieLinkGeneration = (delegate* unmanaged<IntPtr, uint*, int>)Export("nvmlDeviceGetCurrPcieLinkGeneration");
        _deviceGetCurrPcieLinkWidth = (delegate* unmanaged<IntPtr, uint*, int>)Export("nvmlDeviceGetCurrPcieLinkWidth");
        _deviceGetMaxPcieLinkGeneration = (delegate* unmanaged<IntPtr, uint*, int>)Export("nvmlDeviceGetMaxPcieLinkGeneration");
        _deviceGetMaxPcieLinkWidth = (delegate* unmanaged<IntPtr, uint*, int>)Export("nvmlDeviceGetMaxPcieLinkWidth");
        // Renamed in newer drivers ("throttle" -> "clocks event"); both are the same query.
        _deviceGetClockEventReasons = (delegate* unmanaged<IntPtr, ulong*, int>)Export("nvmlDeviceGetCurrentClocksEventReasons");
        if (_deviceGetClockEventReasons == null)
        {
            _deviceGetClockEventReasons = (delegate* unmanaged<IntPtr, ulong*, int>)Export("nvmlDeviceGetCurrentClocksThrottleReasons");
        }

        _deviceGetMemoryBusWidth = (delegate* unmanaged<IntPtr, uint*, int>)Export("nvmlDeviceGetMemoryBusWidth");
        _deviceGetNumGpuCores = (delegate* unmanaged<IntPtr, uint*, int>)Export("nvmlDeviceGetNumGpuCores");
    }

    public string LibraryPath { get; }

    /// <summary>Loads nvml.dll from trusted absolute locations only (no current-directory search).</summary>
    public static NvmlLibrary? TryLoad(out string? error)
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
        };

        string? programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvml.dll"));
        }

        foreach (string path in candidates)
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle))
            {
                var library = new NvmlLibrary(handle, path);
                if (library._init == null || library._deviceGetCount == null || library._deviceGetHandleByIndex == null)
                {
                    library.Dispose();
                    continue;
                }

                error = null;
                return library;
            }
        }

        error = "nvml.dll was not found. NVML ships with the NVIDIA display driver; it is absent on systems without an NVIDIA GPU.";
        return null;
    }

    public NvmlReturn Initialize()
    {
        var result = (NvmlReturn)_init();
        _initialized = result == NvmlReturn.Success;
        return result;
    }

    public string ErrorString(NvmlReturn code)
    {
        if (_errorString == null)
        {
            return code.ToString();
        }

        byte* text = _errorString((int)code);
        return text == null ? code.ToString() : Marshal.PtrToStringAnsi((IntPtr)text) ?? code.ToString();
    }

    public NvmlReturn GetDriverVersion(out string version) => GetString(_systemGetDriverVersion, 96, out version);

    public NvmlReturn GetNvmlVersion(out string version) => GetString(_systemGetNvmlVersion, 96, out version);

    public NvmlReturn GetDeviceCount(out uint count)
    {
        uint c = 0;
        var r = (NvmlReturn)_deviceGetCount(&c);
        count = c;
        return r;
    }

    public NvmlReturn GetDeviceHandle(uint index, out IntPtr device)
    {
        IntPtr d = IntPtr.Zero;
        var r = (NvmlReturn)_deviceGetHandleByIndex(index, &d);
        device = d;
        return r;
    }

    public NvmlReturn GetName(IntPtr device, out string name) => GetDeviceString(_deviceGetName, device, 96, out name);

    public NvmlReturn GetUuid(IntPtr device, out string uuid) => GetDeviceString(_deviceGetUuid, device, 96, out uuid);

    public NvmlReturn GetVbiosVersion(IntPtr device, out string version) => GetDeviceString(_deviceGetVbiosVersion, device, 32, out version);

    public NvmlReturn GetPciBusId(IntPtr device, out string busId)
    {
        busId = string.Empty;
        if (_deviceGetPciInfo == null)
        {
            return NvmlReturn.FunctionNotFound;
        }

        NvmlPciInfo info;
        var r = (NvmlReturn)_deviceGetPciInfo(device, &info);
        if (r == NvmlReturn.Success)
        {
            busId = Marshal.PtrToStringAnsi((IntPtr)info.BusId) ?? string.Empty;
        }

        return r;
    }

    public NvmlReturn GetUtilization(IntPtr device, out NvmlUtilization utilization)
    {
        utilization = default;
        if (_deviceGetUtilizationRates == null) return NvmlReturn.FunctionNotFound;
        NvmlUtilization u;
        var r = (NvmlReturn)_deviceGetUtilizationRates(device, &u);
        utilization = u;
        return r;
    }

    public NvmlReturn GetTemperature(IntPtr device, out uint celsius) => Query(_deviceGetTemperature, device, 0 /* NVML_TEMPERATURE_GPU */, out celsius);

    public NvmlReturn GetTemperatureThreshold(IntPtr device, NvmlTemperatureThreshold threshold, out uint celsius) => Query(_deviceGetTemperatureThreshold, device, (int)threshold, out celsius);

    public NvmlReturn GetClock(IntPtr device, NvmlClockType type, out uint mhz) => Query(_deviceGetClockInfo, device, (int)type, out mhz);

    public NvmlReturn GetMaxClock(IntPtr device, NvmlClockType type, out uint mhz) => Query(_deviceGetMaxClockInfo, device, (int)type, out mhz);

    public NvmlReturn GetMemoryInfo(IntPtr device, out NvmlMemory memory)
    {
        memory = default;
        if (_deviceGetMemoryInfo == null) return NvmlReturn.FunctionNotFound;
        NvmlMemory m;
        var r = (NvmlReturn)_deviceGetMemoryInfo(device, &m);
        memory = m;
        return r;
    }

    public NvmlReturn GetPowerUsage(IntPtr device, out uint milliwatts) => Query(_deviceGetPowerUsage, device, out milliwatts);

    public NvmlReturn GetEnforcedPowerLimit(IntPtr device, out uint milliwatts) => Query(_deviceGetEnforcedPowerLimit, device, out milliwatts);

    public NvmlReturn GetDefaultPowerLimit(IntPtr device, out uint milliwatts) => Query(_deviceGetPowerManagementDefaultLimit, device, out milliwatts);

    public NvmlReturn GetPowerLimitConstraints(IntPtr device, out uint minMilliwatts, out uint maxMilliwatts)
    {
        minMilliwatts = maxMilliwatts = 0;
        if (_deviceGetPowerManagementLimitConstraints == null) return NvmlReturn.FunctionNotFound;
        uint min = 0, max = 0;
        var r = (NvmlReturn)_deviceGetPowerManagementLimitConstraints(device, &min, &max);
        minMilliwatts = min;
        maxMilliwatts = max;
        return r;
    }

    public NvmlReturn GetFanCount(IntPtr device, out uint count) => Query(_deviceGetNumFans, device, out count);

    public NvmlReturn GetFanSpeed(IntPtr device, uint fan, out uint percent)
    {
        percent = 0;
        if (_deviceGetFanSpeedV2 != null)
        {
            uint p = 0;
            var r = (NvmlReturn)_deviceGetFanSpeedV2(device, fan, &p);
            percent = p;
            return r;
        }

        return fan == 0 ? Query(_deviceGetFanSpeed, device, out percent) : NvmlReturn.FunctionNotFound;
    }

    public NvmlReturn GetPerformanceState(IntPtr device, out int pstate)
    {
        pstate = -1;
        if (_deviceGetPerformanceState == null) return NvmlReturn.FunctionNotFound;
        int p = -1;
        var r = (NvmlReturn)_deviceGetPerformanceState(device, &p);
        pstate = p;
        return r;
    }

    public NvmlReturn GetEncoderUtilization(IntPtr device, out uint percent) => QueryPair(_deviceGetEncoderUtilization, device, out percent);

    public NvmlReturn GetDecoderUtilization(IntPtr device, out uint percent) => QueryPair(_deviceGetDecoderUtilization, device, out percent);

    public NvmlReturn GetCurrentPcieGeneration(IntPtr device, out uint generation) => Query(_deviceGetCurrPcieLinkGeneration, device, out generation);

    public NvmlReturn GetCurrentPcieWidth(IntPtr device, out uint width) => Query(_deviceGetCurrPcieLinkWidth, device, out width);

    public NvmlReturn GetMaxPcieGeneration(IntPtr device, out uint generation) => Query(_deviceGetMaxPcieLinkGeneration, device, out generation);

    public NvmlReturn GetMaxPcieWidth(IntPtr device, out uint width) => Query(_deviceGetMaxPcieLinkWidth, device, out width);

    public NvmlReturn GetClockEventReasons(IntPtr device, out ulong mask)
    {
        mask = 0;
        if (_deviceGetClockEventReasons == null) return NvmlReturn.FunctionNotFound;
        ulong m = 0;
        var r = (NvmlReturn)_deviceGetClockEventReasons(device, &m);
        mask = m;
        return r;
    }

    public NvmlReturn GetMemoryBusWidth(IntPtr device, out uint bits) => Query(_deviceGetMemoryBusWidth, device, out bits);

    public NvmlReturn GetGpuCoreCount(IntPtr device, out uint cores) => Query(_deviceGetNumGpuCores, device, out cores);

    public void Dispose()
    {
        if (_initialized && _shutdown != null)
        {
            try
            {
                _shutdown();
            }
            catch
            {
                // ignore: driver may already be gone
            }

            _initialized = false;
        }

        if (_handle != IntPtr.Zero)
        {
            NativeLibrary.Free(_handle);
            _handle = IntPtr.Zero;
        }
    }

    // ---- helpers ----------------------------------------------------------------------------

    private IntPtr Export(string name) => NativeLibrary.TryGetExport(_handle, name, out IntPtr address) ? address : IntPtr.Zero;

    private static NvmlReturn Query(delegate* unmanaged<IntPtr, uint*, int> fn, IntPtr device, out uint value)
    {
        value = 0;
        if (fn == null) return NvmlReturn.FunctionNotFound;
        uint v = 0;
        var r = (NvmlReturn)fn(device, &v);
        value = v;
        return r;
    }

    private static NvmlReturn Query(delegate* unmanaged<IntPtr, int, uint*, int> fn, IntPtr device, int arg, out uint value)
    {
        value = 0;
        if (fn == null) return NvmlReturn.FunctionNotFound;
        uint v = 0;
        var r = (NvmlReturn)fn(device, arg, &v);
        value = v;
        return r;
    }

    private static NvmlReturn QueryPair(delegate* unmanaged<IntPtr, uint*, uint*, int> fn, IntPtr device, out uint value)
    {
        value = 0;
        if (fn == null) return NvmlReturn.FunctionNotFound;
        uint v = 0, period = 0;
        var r = (NvmlReturn)fn(device, &v, &period);
        value = v;
        return r;
    }

    private static NvmlReturn GetString(delegate* unmanaged<byte*, uint, int> fn, int capacity, out string value)
    {
        value = string.Empty;
        if (fn == null) return NvmlReturn.FunctionNotFound;
        byte* buffer = stackalloc byte[capacity];
        var r = (NvmlReturn)fn(buffer, (uint)capacity);
        if (r == NvmlReturn.Success)
        {
            value = ReadAnsi(buffer, capacity);
        }

        return r;
    }

    private static NvmlReturn GetDeviceString(delegate* unmanaged<IntPtr, byte*, uint, int> fn, IntPtr device, int capacity, out string value)
    {
        value = string.Empty;
        if (fn == null) return NvmlReturn.FunctionNotFound;
        byte* buffer = stackalloc byte[capacity];
        var r = (NvmlReturn)fn(device, buffer, (uint)capacity);
        if (r == NvmlReturn.Success)
        {
            value = ReadAnsi(buffer, capacity);
        }

        return r;
    }

    private static string ReadAnsi(byte* buffer, int capacity)
    {
        int length = 0;
        while (length < capacity && buffer[length] != 0)
        {
            length++;
        }

        return Encoding.UTF8.GetString(buffer, length).Trim();
    }
}
