using System.IO;
using System.Runtime.InteropServices;
using Coreloom.Core.Devices;

namespace Coreloom.App.Interop;

/// <summary>
/// The Win32 side of the Devices page: the Plug and Play device tree (configuration manager), the Bluetooth radio
/// (last-used times, disconnecting a device) and the Bluetooth audio drivers' connect/disconnect requests.
/// </summary>
internal static unsafe partial class DeviceNative
{
    private const uint CrSuccess = 0;
    private const uint CrBufferSmall = 0x1A;
    private const uint CrRemoveVetoed = 0x17;
    private const uint FilterEnumerator = 0x1;
    private const uint FilterPresent = 0x100;
    private const uint InterfaceListPresent = 0;
    private const uint DnRemovable = 0x00004000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x80;
    private const uint DevPropTypeString = 0x12;
    private const uint DevPropTypeGuid = 0x0D;
    private const uint DevPropTypeUInt32 = 0x07;
    private const uint DevPropTypeByte = 0x03;

    /// <summary>CTL_CODE(FILE_DEVICE_KS, 0, METHOD_NEITHER, FILE_ANY_ACCESS).</summary>
    private const uint IoctlKsProperty = 0x002F0003;

    /// <summary>IOCTL_BTH_DISCONNECT_DEVICE (bthioctl.h): input is the remote device's BTH_ADDR.</summary>
    private const uint IoctlBthDisconnectDevice = 0x0041000C;

    private const uint KsPropertyTypeGet = 0x1;

    /// <summary>KSPROPERTY_ONESHOT_RECONNECT / _DISCONNECT in KSPROPSETID_BtAudio: what Windows' own Connect button sends.</summary>
    private const uint KsOneshotReconnect = 0;
    private const uint KsOneshotDisconnect = 1;

    private static readonly Guid KsPropSetBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private static readonly Guid KsCategoryAudio = new("6994AD04-93EF-11D0-A3CC-00A0C9223196");

    private static readonly DevPropKey DeviceDesc = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    private static readonly DevPropKey ClassName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 9);
    private static readonly DevPropKey Manufacturer = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 13);
    private static readonly DevPropKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    private static readonly DevPropKey ContainerId = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
    private static readonly DevPropKey BusReportedDeviceDesc = new(new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4);
    private static readonly DevPropKey Parent = new(new Guid("4340a6c5-93fa-4706-972c-7b648008a5a7"), 8);
    private static readonly DevPropKey InstanceId = new(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);

    /// <summary>The battery level Windows records for Bluetooth headsets and other devices that report one (0–100).</summary>
    private static readonly DevPropKey BluetoothBattery = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);

    private static readonly string[] BluetoothEnumerators = ["BTHENUM", "BTHLE", "BTHLEDEVICE", "BTHHFENUM"];

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DevPropKey(Guid fmtid, uint pid)
    {
        public readonly Guid Fmtid = fmtid;
        public readonly uint Pid = pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KsProperty
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
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

    /// <summary>BLUETOOTH_DEVICE_INFO (560 bytes on x64).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothDeviceInfoNative
    {
        public uint Size;
        public ulong Address;
        public uint ClassOfDevice;
        public int Connected;
        public int Remembered;
        public int Authenticated;
        public SystemTime LastSeen;
        public SystemTime LastUsed;
        public fixed ushort Name[248];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams
    {
        public uint Size;
    }

    /// <summary>Checks the structure sizes against the Windows SDK (x64) before they're handed to Windows.</summary>
    public static bool LayoutIsValid => sizeof(BluetoothDeviceInfoNative) == 560 && sizeof(KsProperty) == 24 && sizeof(DevPropKey) == 20;

    /// <summary>A Bluetooth device node: its instance id, container and the battery level recorded on it.</summary>
    public readonly record struct BluetoothNode(string InstanceId, Guid? Container, int? Battery);

    // ---- Device tree ------------------------------------------------------------------------------

    /// <summary>Every device node that's present, with what the Devices page needs (skipping the PC's own devices early).</summary>
    public static List<DevNode> PresentNodes()
    {
        var nodes = new List<DevNode>();
        foreach (string id in DeviceIds(null, FilterPresent))
        {
            if (CM_Locate_DevNode(out uint node, id, 0) != CrSuccess)
            {
                continue;
            }

            Guid? container = GuidProperty(node, ContainerId);
            if (container is null || container == WiredDevices.InternalContainer)
            {
                nodes.Add(new DevNode(id, null, container, null)); // inside the PC: not listed, no need to read more
                continue;
            }

            CM_Get_DevNode_Status(out uint status, out uint problem, node, 0);
            nodes.Add(new DevNode(id, StringProperty(node, Parent), container, StringProperty(node, ClassName))
            {
                FriendlyName = StringProperty(node, FriendlyName),
                Description = StringProperty(node, DeviceDesc),
                BusDescription = StringProperty(node, BusReportedDeviceDesc),
                Manufacturer = StringProperty(node, Manufacturer),
                Problem = (int)problem,
                IsRemovable = (status & DnRemovable) != 0,
            });
        }

        return nodes;
    }

    /// <summary>The Bluetooth device nodes (profiles and services of paired devices) with their containers and batteries.</summary>
    public static List<BluetoothNode> BluetoothNodes()
    {
        var nodes = new List<BluetoothNode>();
        foreach (string enumerator in BluetoothEnumerators)
        {
            foreach (string id in DeviceIds(enumerator, FilterEnumerator | FilterPresent))
            {
                if (CM_Locate_DevNode(out uint node, id, 0) == CrSuccess)
                {
                    nodes.Add(new BluetoothNode(id, GuidProperty(node, ContainerId), ByteProperty(node, BluetoothBattery)));
                }
            }
        }

        return nodes;
    }

    /// <summary>
    /// The container of the device with this address: Bluetooth device nodes carry the address in their instance id
    /// ("BTHENUM\…&amp;A0E9DB123456_C00000000", "BTHLE\DEV_C1C2C3C4C5C6\…").
    /// </summary>
    public static Guid? ContainerFor(IEnumerable<BluetoothNode> nodes, ulong address)
    {
        string hex = BluetoothAddresses.Hex(address);
        return nodes.FirstOrDefault(n => n.Container is not null && n.InstanceId.Contains(hex, StringComparison.OrdinalIgnoreCase)).Container;
    }

    public static int? BatteryFor(IEnumerable<BluetoothNode> nodes, Guid? container, ulong address)
    {
        string hex = BluetoothAddresses.Hex(address);
        return nodes.Where(n => (container is not null && n.Container == container) || n.InstanceId.Contains(hex, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Battery)
            .FirstOrDefault(b => b is >= 0 and <= 100);
    }

    /// <summary>"Safely remove": asks Windows to eject the device (no dialog). Returns null on success, else why not.</summary>
    public static string? Eject(string instanceId)
    {
        if (CM_Locate_DevNode(out uint node, instanceId, 0) != CrSuccess)
        {
            return "It isn't plugged in anymore.";
        }

        char* veto = stackalloc char[260];
        veto[0] = '\0';
        uint result = CM_Request_Device_Eject(node, out int vetoType, veto, 260, 0);
        if (result == CrSuccess)
        {
            return null;
        }

        string who = new string(veto).Trim();
        return result != CrRemoveVetoed ? $"Windows couldn't eject it (error 0x{result:X}). Try again in a moment."
            : vetoType == 3 && who.Length > 0 ? $"{Path.GetFileName(who)} is using it. Close it, then try again."
            : vetoType is 2 or 5 ? "A file or app is still using it. Close it, then try again."
            : "Windows says it's in use. Close files and apps using it, then try again.";
    }

    // ---- Bluetooth radio --------------------------------------------------------------------------

    /// <summary>Opens the first Bluetooth radio (close with <see cref="CloseHandle"/>), or zero.</summary>
    public static IntPtr OpenRadio()
    {
        try
        {
            var findParams = new BluetoothFindRadioParams { Size = (uint)sizeof(BluetoothFindRadioParams) };
            IntPtr find = BluetoothFindFirstRadio(&findParams, out IntPtr radio);
            if (find == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            BluetoothFindRadioClose(find);
            return radio;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>When a Classic device was last used with this PC (local time), as Windows recorded it.</summary>
    public static DateTime? LastUsed(IntPtr radio, ulong address)
    {
        if (radio == IntPtr.Zero || !LayoutIsValid)
        {
            return null;
        }

        var info = new BluetoothDeviceInfoNative { Size = (uint)sizeof(BluetoothDeviceInfoNative), Address = address };
        if (BluetoothGetDeviceInfo(radio, &info) != 0)
        {
            return null;
        }

        var t = info.LastUsed;
        if (t.Year < 2000 || t.Month is < 1 or > 12 || t.Day is < 1 or > 31)
        {
            return null;
        }

        try
        {
            return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, DateTimeKind.Utc).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Drops the link to a device (IOCTL_BTH_DISCONNECT_DEVICE on the radio). Returns the Win32 error, 0 on success.</summary>
    public static int Disconnect(IntPtr radio, ulong address)
    {
        if (radio == IntPtr.Zero)
        {
            return 2; // ERROR_FILE_NOT_FOUND
        }

        ulong value = address;
        return DeviceIoControl(radio, IoctlBthDisconnectDevice, &value, sizeof(ulong), null, 0, out _, IntPtr.Zero)
            ? 0
            : Marshal.GetLastPInvokeError();
    }

    // ---- Bluetooth audio --------------------------------------------------------------------------

    /// <summary>
    /// The Bluetooth audio drivers' kernel-streaming filters for a device (A2DP for music, Hands-Free for calls): their
    /// paths carry the device's address (A2DP) or their device node shares its container (Hands-Free).
    /// </summary>
    public static List<string> AudioFilters(ulong address, Guid? container)
    {
        string hex = BluetoothAddresses.Hex(address);
        var paths = new List<string>();
        foreach (string path in InterfacePaths(KsCategoryAudio))
        {
            if (!path.Contains("bthenum", StringComparison.OrdinalIgnoreCase) && !path.Contains("bthhfenum", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (path.Contains(hex, StringComparison.OrdinalIgnoreCase) || (container is not null && InterfaceContainer(path) == container))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// Sends the Bluetooth audio driver's one-shot reconnect or disconnect request (KSPROPERTY_ONESHOT_RECONNECT /
    /// _DISCONNECT): the same request Windows' Connect button for headphones and speakers sends. Returns the Win32
    /// error, 0 when the driver accepted it.
    /// </summary>
    public static int SendAudioRequest(string filterPath, bool reconnect)
    {
        IntPtr file = CreateFile(filterPath, GenericRead | GenericWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
        if (file == new IntPtr(-1))
        {
            file = CreateFile(filterPath, GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
        }

        if (file == new IntPtr(-1))
        {
            return Marshal.GetLastPInvokeError();
        }

        try
        {
            var property = new KsProperty { Set = KsPropSetBtAudio, Id = reconnect ? KsOneshotReconnect : KsOneshotDisconnect, Flags = KsPropertyTypeGet };
            return DeviceIoControl(file, IoctlKsProperty, &property, (uint)sizeof(KsProperty), null, 0, out _, IntPtr.Zero)
                ? 0
                : Marshal.GetLastPInvokeError();
        }
        finally
        {
            CloseHandle(file);
        }
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private static List<string> DeviceIds(string? filter, uint flags)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_Size(out uint length, filter, flags) != CrSuccess || length == 0)
            {
                return [];
            }

            var buffer = new char[length];
            fixed (char* p = buffer)
            {
                uint result = CM_Get_Device_ID_List(filter, p, length, flags);
                if (result == CrSuccess)
                {
                    return SplitMultiString(buffer);
                }

                if (result != CrBufferSmall)
                {
                    return [];
                }
            }
        }

        return [];
    }

    private static List<string> InterfacePaths(Guid category)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_Interface_List_Size(out uint length, &category, null, InterfaceListPresent) != CrSuccess || length <= 1)
            {
                return [];
            }

            var buffer = new char[length];
            fixed (char* p = buffer)
            {
                uint result = CM_Get_Device_Interface_List(&category, null, p, length, InterfaceListPresent);
                if (result == CrSuccess)
                {
                    return SplitMultiString(buffer);
                }

                if (result != CrBufferSmall)
                {
                    return [];
                }
            }
        }

        return [];
    }

    private static Guid? InterfaceContainer(string path)
    {
        var key = InstanceId;
        uint size = 0;
        CM_Get_Device_Interface_Property(path, &key, out _, null, ref size, 0);
        if (size == 0 || size > 4096)
        {
            return null;
        }

        var bytes = new byte[size];
        fixed (byte* b = bytes)
        {
            if (CM_Get_Device_Interface_Property(path, &key, out uint type, b, ref size, 0) != CrSuccess || type != DevPropTypeString)
            {
                return null;
            }
        }

        string instance = DecodeString(bytes);
        return CM_Locate_DevNode(out uint node, instance, 0) == CrSuccess ? GuidProperty(node, ContainerId) : null;
    }

    private static List<string> SplitMultiString(char[] buffer) =>
        new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static byte[]? Property(uint node, DevPropKey key, out uint type)
    {
        uint size = 0;
        type = 0;
        uint result = CM_Get_DevNode_Property(node, &key, out _, null, ref size, 0);
        if (result != CrBufferSmall || size == 0 || size > 65536)
        {
            return null;
        }

        var bytes = new byte[size];
        fixed (byte* b = bytes)
        {
            return CM_Get_DevNode_Property(node, &key, out type, b, ref size, 0) == CrSuccess ? bytes : null;
        }
    }

    private static string? StringProperty(uint node, DevPropKey key)
    {
        var bytes = Property(node, key, out uint type);
        if (bytes is null || type != DevPropTypeString)
        {
            return null;
        }

        string text = DecodeString(bytes);
        return text.Length == 0 ? null : text;
    }

    private static Guid? GuidProperty(uint node, DevPropKey key)
    {
        var bytes = Property(node, key, out uint type);
        return bytes is { Length: >= 16 } && type == DevPropTypeGuid ? new Guid(bytes.AsSpan(0, 16)) : null;
    }

    private static int? ByteProperty(uint node, DevPropKey key)
    {
        var bytes = Property(node, key, out uint type);
        return bytes is { Length: >= 1 } && type is DevPropTypeByte or DevPropTypeUInt32 ? bytes[0] : null;
    }

    private static string DecodeString(byte[] bytes) =>
        System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();

    // ---- Imports ----------------------------------------------------------------------------------

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_ID_List_Size(out uint length, string? filter, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_ID_List(string? filter, char* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Locate_DevNode(out uint node, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    private static partial uint CM_Get_DevNode_Property(uint node, DevPropKey* key, out uint type, byte* buffer, ref uint size, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_Interface_List_Size(out uint length, Guid* interfaceClass, string? deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_Interface_List(Guid* interfaceClass, string? deviceId, char* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_PropertyW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Get_Device_Interface_Property(string path, DevPropKey* key, out uint type, byte* buffer, ref uint size, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Request_Device_EjectW")]
    private static partial uint CM_Request_Device_Eject(uint node, out int vetoType, char* vetoName, uint nameLength, uint flags);

    [LibraryImport("BluetoothApis.dll")]
    private static partial IntPtr BluetoothFindFirstRadio(BluetoothFindRadioParams* findParams, out IntPtr radio);

    [LibraryImport("BluetoothApis.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BluetoothFindRadioClose(IntPtr find);

    [LibraryImport("BluetoothApis.dll")]
    private static partial uint BluetoothGetDeviceInfo(IntPtr radio, BluetoothDeviceInfoNative* info);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(IntPtr device, uint code, void* input, uint inputSize, void* output, uint outputSize, out uint returned, IntPtr overlapped);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);
}
