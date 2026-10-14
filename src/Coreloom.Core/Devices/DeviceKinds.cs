using System.Globalization;
using Coreloom.Core.Formatting;

namespace Coreloom.Core.Devices;

/// <summary>What a device is, for its icon, its label and the group it is listed in.</summary>
public enum DeviceKind
{
    Unknown,
    Headphones,
    Earbuds,
    Headset,
    Speaker,
    Microphone,
    Audio,
    CarAudio,
    Keyboard,
    Mouse,
    KeyboardAndMouse,
    Controller,
    Receiver,
    Input,
    Pen,
    Remote,
    Phone,
    Computer,
    Tablet,
    Watch,
    Display,
    Camera,
    Storage,
    Printer,
    Network,
    CardReader,
    Hub,
}

/// <summary>Groups of the wired devices list, in display order.</summary>
public enum DeviceGroup
{
    Input,
    Controllers,
    Audio,
    Displays,
    Cameras,
    Storage,
    Printers,
    Network,
    Phones,
    Other,
}

/// <summary>Labels, icons and groups for <see cref="DeviceKind"/>, and how to tell a device's kind from its name.</summary>
public static class DeviceKinds
{
    public static string Text(DeviceKind kind) => kind switch
    {
        DeviceKind.Headphones => "Headphones",
        DeviceKind.Earbuds => "Earbuds",
        DeviceKind.Headset => "Headset",
        DeviceKind.Speaker => "Speaker",
        DeviceKind.Microphone => "Microphone",
        DeviceKind.Audio => "Audio device",
        DeviceKind.CarAudio => "Car audio",
        DeviceKind.Keyboard => "Keyboard",
        DeviceKind.Mouse => "Mouse",
        DeviceKind.KeyboardAndMouse => "Keyboard and mouse",
        DeviceKind.Controller => "Controller",
        DeviceKind.Receiver => "Wireless receiver",
        DeviceKind.Input => "Input device",
        DeviceKind.Pen => "Pen",
        DeviceKind.Remote => "Remote",
        DeviceKind.Phone => "Phone",
        DeviceKind.Computer => "Computer",
        DeviceKind.Tablet => "Tablet",
        DeviceKind.Watch => "Watch",
        DeviceKind.Display => "Display",
        DeviceKind.Camera => "Camera",
        DeviceKind.Storage => "Storage",
        DeviceKind.Printer => "Printer",
        DeviceKind.Network => "Network adapter",
        DeviceKind.CardReader => "Card reader",
        DeviceKind.Hub => "USB hub",
        _ => "Device",
    };

    /// <summary>Icon resource key ("Icon.Headphones"…).</summary>
    public static string IconKey(DeviceKind kind) => kind switch
    {
        DeviceKind.Headphones or DeviceKind.Earbuds or DeviceKind.Headset => "Icon.Headphones",
        DeviceKind.Speaker or DeviceKind.CarAudio or DeviceKind.Audio => "Icon.Speaker",
        DeviceKind.Microphone => "Icon.Microphone",
        DeviceKind.Keyboard or DeviceKind.KeyboardAndMouse or DeviceKind.Input => "Icon.Keyboard",
        DeviceKind.Mouse => "Icon.Mouse",
        DeviceKind.Controller => "Icon.Controller",
        DeviceKind.Receiver or DeviceKind.Remote => "Icon.Wireless",
        DeviceKind.Pen => "Icon.Edit",
        DeviceKind.Phone => "Icon.Phone",
        DeviceKind.Computer or DeviceKind.Tablet => "Icon.Laptop",
        DeviceKind.Watch => "Icon.Watch",
        DeviceKind.Display => "Icon.Display",
        DeviceKind.Camera => "Icon.Camera",
        DeviceKind.Storage or DeviceKind.CardReader => "Icon.UsbDrive",
        DeviceKind.Printer => "Icon.Printer",
        DeviceKind.Network => "Icon.Network",
        DeviceKind.Hub => "Icon.Plug",
        _ => "Icon.Devices",
    };

    public static DeviceGroup GroupOf(DeviceKind kind) => kind switch
    {
        DeviceKind.Keyboard or DeviceKind.Mouse or DeviceKind.KeyboardAndMouse or DeviceKind.Receiver or DeviceKind.Input or DeviceKind.Pen or DeviceKind.Remote => DeviceGroup.Input,
        DeviceKind.Controller => DeviceGroup.Controllers,
        DeviceKind.Headphones or DeviceKind.Earbuds or DeviceKind.Headset or DeviceKind.Speaker or DeviceKind.Microphone or DeviceKind.Audio or DeviceKind.CarAudio => DeviceGroup.Audio,
        DeviceKind.Display => DeviceGroup.Displays,
        DeviceKind.Camera => DeviceGroup.Cameras,
        DeviceKind.Storage or DeviceKind.CardReader => DeviceGroup.Storage,
        DeviceKind.Printer => DeviceGroup.Printers,
        DeviceKind.Network => DeviceGroup.Network,
        DeviceKind.Phone or DeviceKind.Tablet or DeviceKind.Watch => DeviceGroup.Phones,
        _ => DeviceGroup.Other,
    };

    public static string GroupText(DeviceGroup group) => group switch
    {
        DeviceGroup.Input => "Keyboards and mice",
        DeviceGroup.Controllers => "Controllers",
        DeviceGroup.Audio => "Audio",
        DeviceGroup.Displays => "Displays",
        DeviceGroup.Cameras => "Cameras",
        DeviceGroup.Storage => "Storage",
        DeviceGroup.Printers => "Printers and scanners",
        DeviceGroup.Network => "Network",
        DeviceGroup.Phones => "Phones and tablets",
        _ => "Other devices",
    };

    public static string GroupIconKey(DeviceGroup group) => group switch
    {
        DeviceGroup.Input => "Icon.Keyboard",
        DeviceGroup.Controllers => "Icon.Controller",
        DeviceGroup.Audio => "Icon.Headphones",
        DeviceGroup.Displays => "Icon.Display",
        DeviceGroup.Cameras => "Icon.Camera",
        DeviceGroup.Storage => "Icon.UsbDrive",
        DeviceGroup.Printers => "Icon.Printer",
        DeviceGroup.Network => "Icon.Network",
        DeviceGroup.Phones => "Icon.Phone",
        _ => "Icon.Devices",
    };

    /// <summary>Is it something you listen with or to (connects through the audio stack)?</summary>
    public static bool IsAudio(DeviceKind kind) => GroupOf(kind) == DeviceGroup.Audio;

    // Most specific first: "airpods max" are headphones, "galaxy buds" earbuds rather than a phone.
    private static readonly (string Word, DeviceKind Kind)[] NameHints =
    [
        ("airpods max", DeviceKind.Headphones),
        ("airpods", DeviceKind.Earbuds),
        ("earbud", DeviceKind.Earbuds),
        ("buds", DeviceKind.Earbuds),
        ("earphone", DeviceKind.Earbuds),
        ("in-ear", DeviceKind.Earbuds),
        ("headset", DeviceKind.Headset),
        ("hands-free", DeviceKind.Headset),
        ("handsfree", DeviceKind.Headset),
        ("headphone", DeviceKind.Headphones),
        ("wh-1000", DeviceKind.Headphones),
        ("quietcomfort", DeviceKind.Headphones),
        ("soundbar", DeviceKind.Speaker),
        ("speaker", DeviceKind.Speaker),
        ("soundlink", DeviceKind.Speaker),
        ("microphone", DeviceKind.Microphone),
        ("receiver", DeviceKind.Receiver),
        ("dongle", DeviceKind.Receiver),
        ("unifying", DeviceKind.Receiver),
        ("lightspeed", DeviceKind.Receiver),
        ("controller", DeviceKind.Controller),
        ("gamepad", DeviceKind.Controller),
        ("joystick", DeviceKind.Controller),
        ("dualsense", DeviceKind.Controller),
        ("dualshock", DeviceKind.Controller),
        ("joy-con", DeviceKind.Controller),
        ("xbox", DeviceKind.Controller),
        ("8bitdo", DeviceKind.Controller),
        ("keyboard", DeviceKind.Keyboard),
        ("mx keys", DeviceKind.Keyboard),
        ("mouse", DeviceKind.Mouse),
        ("mx master", DeviceKind.Mouse),
        ("mx anywhere", DeviceKind.Mouse),
        ("trackball", DeviceKind.Mouse),
        ("trackpad", DeviceKind.Mouse),
        ("touchpad", DeviceKind.Mouse),
        ("stylus", DeviceKind.Pen),
        ("webcam", DeviceKind.Camera),
        ("camera", DeviceKind.Camera),
        ("printer", DeviceKind.Printer),
        ("scanner", DeviceKind.Printer),
        ("watch", DeviceKind.Watch),
        ("fitbit", DeviceKind.Watch),
        ("iphone", DeviceKind.Phone),
        ("phone", DeviceKind.Phone),
        ("pixel", DeviceKind.Phone),
        ("galaxy s", DeviceKind.Phone),
        ("oneplus", DeviceKind.Phone),
        ("ipad", DeviceKind.Tablet),
        ("tablet", DeviceKind.Tablet),
        ("monitor", DeviceKind.Display),
        ("flash drive", DeviceKind.Storage),
        ("card reader", DeviceKind.CardReader),
        ("ethernet", DeviceKind.Network),
    ];

    /// <summary>A kind suggested by the device's name ("WH-1000XM5" → headphones), or Unknown.</summary>
    public static DeviceKind FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return DeviceKind.Unknown;
        }

        string text = name.ToLower(CultureInfo.InvariantCulture);
        foreach (var (word, kind) in NameHints)
        {
            if (text.Contains(word, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return DeviceKind.Unknown;
    }

    /// <summary>
    /// A Bluetooth Classic device's kind from its Class of Device (major class in bits 8–12, minor in bits 2–7), refined
    /// by its name where the class is vague.
    /// </summary>
    public static DeviceKind FromClassOfDevice(uint classOfDevice, string? name = null)
    {
        int major = (int)((classOfDevice >> 8) & 0x1F);
        int minor = (int)((classOfDevice >> 2) & 0x3F);
        var kind = major switch
        {
            1 => minor == 7 ? DeviceKind.Tablet : DeviceKind.Computer,
            2 => DeviceKind.Phone,
            3 => DeviceKind.Network,
            4 => minor switch
            {
                1 or 2 => DeviceKind.Headset,
                4 => DeviceKind.Microphone,
                5 or 7 or 10 => DeviceKind.Speaker,
                6 => DeviceKind.Headphones,
                8 => DeviceKind.CarAudio,
                12 or 13 or 16 => DeviceKind.Camera,
                14 or 15 => DeviceKind.Display,
                18 => DeviceKind.Controller,
                _ => DeviceKind.Audio,
            },
            5 => (minor & 0x0F) switch
            {
                1 or 2 => DeviceKind.Controller,
                3 => DeviceKind.Remote,
                5 or 7 => DeviceKind.Pen,
                6 => DeviceKind.CardReader,
                _ => (minor >> 4) switch
                {
                    1 => DeviceKind.Keyboard,
                    2 => DeviceKind.Mouse,
                    3 => DeviceKind.KeyboardAndMouse,
                    _ => DeviceKind.Input,
                },
            },
            6 => (minor & 0x20) != 0 || (minor & 0x10) != 0 ? DeviceKind.Printer
                : (minor & 0x08) != 0 ? DeviceKind.Camera
                : (minor & 0x04) != 0 ? DeviceKind.Display
                : DeviceKind.Printer,
            7 => DeviceKind.Watch,
            8 => minor == 4 ? DeviceKind.Controller : DeviceKind.Unknown,
            _ => DeviceKind.Unknown,
        };
        return Refine(kind, name);
    }

    /// <summary>A Bluetooth LE device's kind from its GAP Appearance (category in the top 10 bits), refined by its name.</summary>
    public static DeviceKind FromAppearance(ushort appearance, string? name = null)
    {
        int category = appearance >> 6;
        int sub = appearance & 0x3F;
        var kind = category switch
        {
            1 => DeviceKind.Phone,
            2 => DeviceKind.Computer,
            3 => DeviceKind.Watch,
            5 => DeviceKind.Display,
            6 => DeviceKind.Remote,
            15 => sub switch
            {
                1 => DeviceKind.Keyboard,
                2 => DeviceKind.Mouse,
                3 or 4 => DeviceKind.Controller,
                5 or 7 => DeviceKind.Pen,
                6 => DeviceKind.CardReader,
                10 => DeviceKind.Remote,
                _ => DeviceKind.Input,
            },
            33 => DeviceKind.Speaker,
            37 => sub switch
            {
                1 => DeviceKind.Earbuds,
                2 => DeviceKind.Headset,
                _ => DeviceKind.Headphones,
            },
            42 => DeviceKind.Headphones, // hearing aid
            _ => DeviceKind.Unknown,
        };
        return Refine(kind, name);
    }

    /// <summary>The class says little ("audio", "input", nothing): let a clear name decide.</summary>
    public static DeviceKind Refine(DeviceKind kind, string? name)
    {
        var hinted = FromName(name);
        if (hinted == DeviceKind.Unknown)
        {
            return kind;
        }

        return kind switch
        {
            DeviceKind.Unknown or DeviceKind.Input or DeviceKind.Audio => hinted,
            DeviceKind.Headset or DeviceKind.Headphones when hinted is DeviceKind.Earbuds or DeviceKind.Headphones or DeviceKind.Headset => hinted,
            DeviceKind.KeyboardAndMouse when hinted is DeviceKind.Keyboard or DeviceKind.Mouse => hinted,
            _ => kind,
        };
    }
}

/// <summary>Bluetooth addresses as text.</summary>
public static class BluetoothAddresses
{
    /// <summary>"A0:E9:DB:12:34:56".</summary>
    public static string Format(ulong address)
    {
        string hex = Hex(address);
        return string.Join(':', Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    /// <summary>"A0E9DB123456", as it appears in device instance ids and paths.</summary>
    public static string Hex(ulong address) => (address & 0xFFFFFFFFFFFF).ToString("X12", CultureInfo.InvariantCulture);
}

/// <summary>Battery levels on the four-color scale: plenty is green, low orange, nearly empty red.</summary>
public static class BatteryLevels
{
    public static ThresholdLevel Level(int percent) => Benchmarks.SpeedRating.HigherIsBetter(Math.Max(percent, 0.01), 50, 20, 10);

    public static bool IsLow(int percent) => percent < 20;
}
