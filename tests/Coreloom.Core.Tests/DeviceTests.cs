using Coreloom.Core.Devices;
using Coreloom.Core.Formatting;

namespace Coreloom.Core.Tests;

public class DeviceKindTests
{
    [Theory]
    [InlineData(0x240418u, DeviceKind.Headphones)] // audio/video, headphones
    [InlineData(0x240404u, DeviceKind.Headset)] // wearable headset
    [InlineData(0x240414u, DeviceKind.Speaker)] // loudspeaker
    [InlineData(0x002508u, DeviceKind.Controller)] // peripheral, gamepad
    [InlineData(0x002540u, DeviceKind.Keyboard)] // peripheral, keyboard
    [InlineData(0x002580u, DeviceKind.Mouse)] // peripheral, pointing device
    [InlineData(0x5A020Cu, DeviceKind.Phone)] // smartphone
    [InlineData(0x10010Cu, DeviceKind.Computer)] // laptop
    [InlineData(0x000704u, DeviceKind.Watch)] // wearable, wristwatch
    [InlineData(0x000000u, DeviceKind.Unknown)]
    public void ClassOfDevice_GivesTheKind(uint cod, DeviceKind expected)
    {
        Assert.Equal(expected, DeviceKinds.FromClassOfDevice(cod));
    }

    [Fact]
    public void Names_RefineVagueClasses()
    {
        Assert.Equal(DeviceKind.Earbuds, DeviceKinds.FromClassOfDevice(0x240404, "Galaxy Buds2 Pro"));
        Assert.Equal(DeviceKind.Headphones, DeviceKinds.FromClassOfDevice(0x240404, "AirPods Max"));
        Assert.Equal(DeviceKind.Controller, DeviceKinds.FromClassOfDevice(0, "Xbox Wireless Controller"));
        Assert.Equal(DeviceKind.Headset, DeviceKinds.FromName("Xbox Wireless Headset"));
        Assert.Equal(DeviceKind.Keyboard, DeviceKinds.FromClassOfDevice(0x002540, "Logitech Mouse")); // a clear class wins
        Assert.Equal(DeviceKind.Mouse, DeviceKinds.FromClassOfDevice(0x0025C0, "MX Master 3S"));
        Assert.Equal(DeviceKind.Unknown, DeviceKinds.FromName("WF-C700N 2"));
    }

    [Theory]
    [InlineData((ushort)0x03C1, DeviceKind.Keyboard)]
    [InlineData((ushort)0x03C2, DeviceKind.Mouse)]
    [InlineData((ushort)0x03C4, DeviceKind.Controller)]
    [InlineData((ushort)0x0941, DeviceKind.Earbuds)]
    [InlineData((ushort)0x0943, DeviceKind.Headphones)]
    [InlineData((ushort)0x00C0, DeviceKind.Watch)]
    [InlineData((ushort)0x0000, DeviceKind.Unknown)]
    public void Appearance_GivesTheKind(ushort appearance, DeviceKind expected)
    {
        Assert.Equal(expected, DeviceKinds.FromAppearance(appearance));
    }

    [Fact]
    public void Kinds_HaveLabelsIconsAndGroups()
    {
        Assert.Equal("Icon.Headphones", DeviceKinds.IconKey(DeviceKind.Earbuds));
        Assert.Equal(DeviceGroup.Audio, DeviceKinds.GroupOf(DeviceKind.Headset));
        Assert.True(DeviceKinds.IsAudio(DeviceKind.Speaker));
        Assert.False(DeviceKinds.IsAudio(DeviceKind.Controller));
        Assert.Equal("Keyboards and mice", DeviceKinds.GroupText(DeviceGroup.Input));
        Assert.All(Enum.GetValues<DeviceKind>(), k => Assert.StartsWith("Icon.", DeviceKinds.IconKey(k)));
    }

    [Fact]
    public void Addresses_AndBattery()
    {
        Assert.Equal("A0:E9:DB:12:34:56", BluetoothAddresses.Format(0xA0E9DB123456));
        Assert.Equal("00001B0F00AA", BluetoothAddresses.Hex(0x1B0F00AA));
        Assert.Equal(ThresholdLevel.Cool, BatteryLevels.Level(80));
        Assert.Equal(ThresholdLevel.Normal, BatteryLevels.Level(35));
        Assert.Equal(ThresholdLevel.Elevated, BatteryLevels.Level(15));
        Assert.Equal(ThresholdLevel.Critical, BatteryLevels.Level(5));
        Assert.Equal(ThresholdLevel.Critical, BatteryLevels.Level(0));
        Assert.True(BatteryLevels.IsLow(15));
    }
}

public class BluetoothDeviceListTests
{
    [Fact]
    public void DualModeDevices_AreListedOnce_ConnectedFirst()
    {
        var container = Guid.NewGuid();
        var list = BluetoothDeviceList.Merge(
        [
            new("le", "WH-1000XM5", 0x112233445566, BluetoothTransport.LowEnergy, DeviceKind.Unknown, true) { ContainerId = container, Battery = 70 },
            new("classic", "WH-1000XM5", 0xAABBCCDDEEFF, BluetoothTransport.Classic, DeviceKind.Headphones, false) { ContainerId = container, LastConnected = new DateTime(2026, 10, 1) },
            new("pad", "Xbox Wireless Controller", 0x1, BluetoothTransport.Classic, DeviceKind.Controller, false),
            new("mouse", "MX Master 3S", 0x2, BluetoothTransport.LowEnergy, DeviceKind.Mouse, false),
        ]);

        Assert.Equal(3, list.Count);
        var headphones = list[0];
        Assert.Equal("classic", headphones.Id); // Classic is what connects audio
        Assert.True(headphones.IsConnected);
        Assert.Equal(70, headphones.Battery);
        Assert.Equal(DeviceKind.Headphones, headphones.Kind);
        Assert.Equal(new DateTime(2026, 10, 1), headphones.LastConnected);
        Assert.Equal<string>(new[] { "classic", "mouse", "pad" }, list.Select(d => d.Id).ToArray());
    }
}

public class WiredDeviceTests
{
    private static readonly Guid Internal = WiredDevices.InternalContainer;

    private static DevNode Node(string id, string? parent, Guid? container, string? cls, string? friendly = null, string? description = null, string? bus = null) =>
        new(id, parent, container, cls) { FriendlyName = friendly, Description = description, BusDescription = bus };

    [Fact]
    public void ContainersBecomeDevices_InternalAndBluetoothLeftOut()
    {
        var mouse = Guid.NewGuid();
        var drive = Guid.NewGuid();
        var monitor = Guid.NewGuid();
        var headphones = Guid.NewGuid();
        var printer = Guid.NewGuid();
        var nodes = new List<DevNode>
        {
            Node(@"PCI\VEN_8086&DEV_7AE0\3", null, Internal, "USB", "USB eXtensible Host Controller"),
            Node(@"USB\ROOT_HUB30\4", @"PCI\VEN_8086&DEV_7AE0\3", Internal, "USB", "USB Root Hub (USB 3.0)"),
            // A gaming mouse that also exposes a keyboard collection for its macro buttons.
            Node(@"USB\VID_046D&PID_C08B\1", @"USB\ROOT_HUB30\4", mouse, "USB", description: "USB Composite Device", bus: "G502 HERO Gaming Mouse") with { Manufacturer = "(Standard USB Host Controller)" },
            Node(@"HID\VID_046D&PID_C08B&MI_00\7", @"USB\VID_046D&PID_C08B\1", mouse, "Mouse", "HID-compliant mouse"),
            Node(@"HID\VID_046D&PID_C08B&MI_01\7", @"USB\VID_046D&PID_C08B\1", mouse, "Keyboard", "HID Keyboard Device"),
            // A flash drive.
            Node(@"USB\VID_0781&PID_5581\4C53", @"USB\ROOT_HUB30\4", drive, "USB", description: "USB Mass Storage Device", bus: "Ultra") with { IsRemovable = true },
            Node(@"USBSTOR\DISK&VEN_SANDISK\4C53&0", @"USB\VID_0781&PID_5581\4C53", drive, "DiskDrive", "SanDisk Ultra USB Device"),
            // A monitor with a broken driver.
            Node(@"DISPLAY\DELA1B2\5&1", @"PCI\VEN_10DE\4", monitor, "Monitor", "DELL U2723QE", "Generic PnP Monitor") with { Problem = 28 },
            // Bluetooth headphones are listed with Bluetooth, not here.
            Node(@"BTHENUM\{0000110B}_VID&000104C\7&A0E9DB123456", null, headphones, "MEDIA", "WH-1000XM5"),
            Node(@"SWD\MMDEVAPI\{0.0.0.00000000}.{abc}", null, headphones, "AudioEndpoint", "Headphones (WH-1000XM5)"),
            // A network printer: no wired bus.
            Node(@"SWD\PRINTENUM\{1234}", null, printer, "PrintQueue", "HP OfficeJet"),
        };

        var devices = WiredDevices.Build(nodes);

        Assert.Equal(3, devices.Count);
        var g502 = devices.Single(d => d.Id == mouse.ToString());
        Assert.Equal("G502 HERO Gaming Mouse", g502.Name);
        Assert.Equal(DeviceKind.Mouse, g502.Kind); // the name decides between the mouse and keyboard collections
        Assert.Null(g502.Manufacturer);
        Assert.Equal("USB", g502.Connection);
        Assert.False(g502.CanEject);

        var stick = devices.Single(d => d.Id == drive.ToString());
        Assert.Equal(DeviceKind.Storage, stick.Kind);
        Assert.Equal("SanDisk Ultra", stick.Name);
        Assert.Equal(@"USB\VID_0781&PID_5581\4C53", stick.EjectInstanceId);

        var dell = devices.Single(d => d.Id == monitor.ToString());
        Assert.Equal(DeviceKind.Display, dell.Kind);
        Assert.Equal("DELL U2723QE", dell.Name);
        Assert.Equal("Display cable", dell.Connection);
        Assert.Equal("Driver not installed", dell.ProblemText);

        Assert.Equal(DeviceGroup.Input, devices[0].Group); // grouped in display order
    }

    [Fact]
    public void HubsOnTheirOwn_AreLeftOut_AndGenericNamesAvoided()
    {
        var hub = Guid.NewGuid();
        var pad = Guid.NewGuid();
        var nodes = new List<DevNode>
        {
            Node(@"USB\VID_05E3&PID_0610\1", null, hub, "USB", "Generic USB Hub"),
            Node(@"USB\VID_045E&PID_0B12\2", null, pad, "USB", description: "USB Composite Device"),
            Node(@"USB\VID_045E&PID_0B12&IG_00\3", @"USB\VID_045E&PID_0B12\2", pad, "XnaComposite", "Xbox Controller"),
        };

        var device = Assert.Single(WiredDevices.Build(nodes));
        Assert.Equal(DeviceKind.Controller, device.Kind);
        Assert.Equal("Xbox Controller", device.Name);
        Assert.True(WiredDevices.IsGeneric("HID-compliant consumer control device"));
        Assert.False(WiredDevices.IsGeneric("USB Receiver"));
    }
}
