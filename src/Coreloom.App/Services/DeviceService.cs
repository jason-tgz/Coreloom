using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Coreloom.App.Interop;
using Coreloom.Core.Devices;
using Coreloom.Core.Diagnostics;
using Coreloom.ViewModels.Services;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace Coreloom.App.Services;

/// <summary>
/// The Devices page on Windows. Paired Bluetooth devices, their connection state and type come from Windows' Bluetooth
/// APIs (WinRT), with live connection events. Connecting headphones and speakers sends the audio driver the same
/// one-shot reconnect request as Windows' own Connect button, which also brings back headphones that switched to a
/// phone; other devices are paged and Windows' profile drivers take over. Disconnecting uses the audio driver's request
/// or drops the link at the radio. Wired devices come from the Plug and Play device tree.
/// </summary>
internal sealed class WindowsDeviceService : IDeviceService, IDisposable
{
    private const string LogCategory = "Devices";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan GattBatteryInterval = TimeSpan.FromMinutes(2);

    private readonly Dictionary<string, BluetoothDevice> _classic = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BluetoothLEDevice> _lowEnergy = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GattSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int? Level, DateTime At)> _gattBattery = new(StringComparer.Ordinal);
    private Radio? _radio;
    private DateTime _radioLookedAt = DateTime.MinValue;
    private bool _disposed;

    public event EventHandler? Changed;

    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    // ---- Radio ------------------------------------------------------------------------------------

    public async Task<BluetoothRadioInfo> RadioAsync(CancellationToken cancellationToken)
    {
        var radio = await BluetoothRadioAsync(cancellationToken);
        if (radio is null)
        {
            return BluetoothRadioInfo.None;
        }

        try
        {
            var state = radio.State;
            return new BluetoothRadioInfo(true, state == RadioState.On, state is RadioState.On or RadioState.Off);
        }
        catch (Exception e) when (IsWindowsFailure(e))
        {
            _radio = null; // the adapter went away; look again next time
            _radioLookedAt = DateTime.MinValue;
            return BluetoothRadioInfo.None;
        }
    }

    public async Task<bool> SetRadioAsync(bool on, CancellationToken cancellationToken)
    {
        var radio = await BluetoothRadioAsync(cancellationToken);
        if (radio is null)
        {
            return false;
        }

        try
        {
            var status = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off).AsTask(cancellationToken);
            Log.Info(LogCategory, $"Bluetooth turned {(on ? "on" : "off")}: {status}.");
            return status == RadioAccessStatus.Allowed;
        }
        catch (Exception e) when (IsWindowsFailure(e))
        {
            Log.Warn(LogCategory, "Switching Bluetooth failed.", e);
            return false;
        }
    }

    private async Task<Radio?> BluetoothRadioAsync(CancellationToken cancellationToken)
    {
        if (_radio is not null || _disposed || DateTime.UtcNow - _radioLookedAt < TimeSpan.FromSeconds(30))
        {
            return _radio; // no adapter: look again every half minute (a dongle may be plugged in)
        }

        _radioLookedAt = DateTime.UtcNow;
        try
        {
            await Radio.RequestAccessAsync().AsTask(cancellationToken);
            var radios = await Radio.GetRadiosAsync().AsTask(cancellationToken);
            _radio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (_radio is not null)
            {
                _radio.StateChanged += OnRadioStateChanged;
            }
        }
        catch (Exception e) when (IsWindowsFailure(e))
        {
            Log.Warn(LogCategory, "Couldn't read the Bluetooth adapter.", e);
        }

        return _radio;
    }

    private void OnRadioStateChanged(Radio sender, object args) => Changed?.Invoke(this, EventArgs.Empty);

    // ---- Bluetooth devices ------------------------------------------------------------------------

    public async Task<IReadOnlyList<BluetoothDeviceInfo>> BluetoothDevicesAsync(CancellationToken cancellationToken)
    {
        var nodesTask = Task.Run(DeviceNative.BluetoothNodes, cancellationToken);
        var classic = await FindAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), cancellationToken);
        var lowEnergy = await FindAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), cancellationToken);
        var nodes = await nodesTask;
        var list = new List<BluetoothDeviceInfo>();

        IntPtr radio = DeviceNative.OpenRadio();
        try
        {
            foreach (var info in classic)
            {
                if (await ClassicAsync(info.Id) is not { } device)
                {
                    continue;
                }

                try
                {
                    ulong address = device.BluetoothAddress;
                    string name = Named(device.Name, info.Name);
                    bool connected = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                    var container = DeviceNative.ContainerFor(nodes, address);
                    list.Add(new BluetoothDeviceInfo(info.Id, name, address, BluetoothTransport.Classic, DeviceKinds.FromClassOfDevice(device.ClassOfDevice.RawValue, name), connected)
                    {
                        ContainerId = container,
                        Battery = DeviceNative.BatteryFor(nodes, container, address),
                        LastConnected = connected ? null : DeviceNative.LastUsed(radio, address),
                    });
                }
                catch (Exception e) when (IsWindowsFailure(e))
                {
                    Forget(info.Id); // unpaired meanwhile
                }
            }
        }
        finally
        {
            if (radio != IntPtr.Zero)
            {
                DeviceNative.CloseHandle(radio);
            }
        }

        foreach (var info in lowEnergy)
        {
            if (await LowEnergyAsync(info.Id) is not { } device)
            {
                continue;
            }

            try
            {
                ulong address = device.BluetoothAddress;
                string name = Named(device.Name, info.Name);
                bool connected = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                var container = DeviceNative.ContainerFor(nodes, address);
                int? battery = DeviceNative.BatteryFor(nodes, container, address) ?? (connected ? await GattBatteryAsync(info.Id, device, cancellationToken) : null);
                list.Add(new BluetoothDeviceInfo(info.Id, name, address, BluetoothTransport.LowEnergy, DeviceKinds.FromAppearance(device.Appearance.RawValue, name), connected)
                {
                    ContainerId = container,
                    Battery = battery,
                });
            }
            catch (Exception e) when (IsWindowsFailure(e))
            {
                Forget(info.Id);
            }
        }

        // Devices no longer paired: let go of them.
        var current = classic.Select(i => i.Id).Concat(lowEnergy.Select(i => i.Id)).ToHashSet(StringComparer.Ordinal);
        foreach (string gone in _classic.Keys.Concat(_lowEnergy.Keys).Where(id => !current.Contains(id)).ToList())
        {
            Forget(gone);
        }

        return BluetoothDeviceList.Merge(list);
    }

    public async Task<DeviceActionResult> ConnectAsync(BluetoothDeviceInfo info, CancellationToken cancellationToken)
    {
        if (await RadioAsync(cancellationToken) is { IsPresent: true, IsOn: false })
        {
            return new DeviceActionResult(false, "Turn Bluetooth on first.");
        }

        Log.Info(LogCategory, $"Connecting {info.Name} ({info.Transport}, {info.Kind}).");
        if (info.Transport == BluetoothTransport.Classic)
        {
            var filters = await Task.Run(() => DeviceNative.AudioFilters(info.Address, info.ContainerId), cancellationToken);
            int accepted = await Task.Run(() => filters.Count(path => DeviceNative.SendAudioRequest(path, reconnect: true) == 0), cancellationToken);
            if (accepted == 0 && await ClassicAsync(info.Id) is { } device)
            {
                // Not an audio device (or its driver refused): page it; Windows' profile drivers connect once it answers.
                await IgnoreFailures(device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken), TimeSpan.FromSeconds(10));
            }

            if (await WaitForAsync(info.Id, connected: true, TimeSpan.FromSeconds(accepted > 0 ? 15 : 8), cancellationToken))
            {
                Log.Info(LogCategory, $"{info.Name} connected.");
                return new DeviceActionResult(true, $"{info.Name} is connected.");
            }

            Log.Info(LogCategory, $"{info.Name} didn't connect ({filters.Count} audio filters, {accepted} accepted).");
            return new DeviceActionResult(false, info.IsAudio || accepted > 0
                ? $"{info.Name} didn't answer. Make sure it's on and nearby. If it's playing on another device, pause or disconnect it there, then try again."
                : $"{info.Name} didn't answer. Turn it on or press a button on it; most keyboards, mice and controllers reconnect by themselves.");
        }

        if (await LowEnergyAsync(info.Id) is { } le)
        {
            try
            {
                if (!_sessions.ContainsKey(info.Id))
                {
                    var session = await GattSession.FromDeviceIdAsync(le.BluetoothDeviceId).AsTask(cancellationToken);
                    session.MaintainConnection = true; // keeps the link up instead of letting it idle away
                    _sessions[info.Id] = session;
                }

                var services = await WithTimeout(le.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken), TimeSpan.FromSeconds(10));
                if (services is not null)
                {
                    foreach (var service in services.Services)
                    {
                        service.Dispose();
                    }
                }
            }
            catch (Exception e) when (IsWindowsFailure(e))
            {
                Log.Warn(LogCategory, $"Connecting {info.Name} failed.", e);
            }

            if (await WaitForAsync(info.Id, connected: true, TimeSpan.FromSeconds(10), cancellationToken))
            {
                return new DeviceActionResult(true, $"{info.Name} is connected.");
            }
        }

        return new DeviceActionResult(false, $"{info.Name} didn't answer. Turn it on or press a button on it so it reconnects.");
    }

    public async Task<DeviceActionResult> DisconnectAsync(BluetoothDeviceInfo info, CancellationToken cancellationToken)
    {
        Log.Info(LogCategory, $"Disconnecting {info.Name} ({info.Transport}, {info.Kind}).");
        if (_sessions.Remove(info.Id, out var session))
        {
            session.MaintainConnection = false;
            session.Dispose();
        }

        if (info.Transport == BluetoothTransport.Classic)
        {
            var filters = await Task.Run(() => DeviceNative.AudioFilters(info.Address, info.ContainerId), cancellationToken);
            int accepted = await Task.Run(() => filters.Count(path => DeviceNative.SendAudioRequest(path, reconnect: false) == 0), cancellationToken);
            if (accepted > 0 && await WaitForAsync(info.Id, connected: false, TimeSpan.FromSeconds(8), cancellationToken))
            {
                return new DeviceActionResult(true, $"{info.Name} is disconnected.");
            }
        }

        int error = await Task.Run(() =>
        {
            IntPtr radio = DeviceNative.OpenRadio();
            try
            {
                return DeviceNative.Disconnect(radio, info.Address);
            }
            finally
            {
                if (radio != IntPtr.Zero)
                {
                    DeviceNative.CloseHandle(radio);
                }
            }
        }, cancellationToken);

        if (await WaitForAsync(info.Id, connected: false, TimeSpan.FromSeconds(6), cancellationToken))
        {
            return new DeviceActionResult(true, $"{info.Name} is disconnected.");
        }

        Log.Info(LogCategory, $"{info.Name} stayed connected (disconnect error {error}).");
        return new DeviceActionResult(false, $"Windows kept {info.Name} connected. Turn it off, or remove it in Bluetooth settings if it should stay away.");
    }

    public void OpenBluetoothSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Log.Warn(LogCategory, "Couldn't open Bluetooth settings.", e);
        }
    }

    // ---- Wired devices ----------------------------------------------------------------------------

    public Task<IReadOnlyList<WiredDevice>> WiredDevicesAsync(CancellationToken cancellationToken) =>
        Task.Run(() => WiredDevices.Build(DeviceNative.PresentNodes()), cancellationToken);

    public Task<DeviceActionResult> EjectAsync(WiredDevice device, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (device.EjectInstanceId is not { } id)
        {
            return new DeviceActionResult(false, "This device can't be ejected.");
        }

        string? problem = DeviceNative.Eject(id);
        Log.Info(LogCategory, problem is null ? $"Ejected {device.Name}." : $"Ejecting {device.Name} was refused: {problem}");
        return problem is null
            ? new DeviceActionResult(true, $"{device.Name} can be unplugged now.")
            : new DeviceActionResult(false, problem);
    }, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_radio is not null)
        {
            _radio.StateChanged -= OnRadioStateChanged;
        }

        foreach (string id in _classic.Keys.Concat(_lowEnergy.Keys).ToList())
        {
            Forget(id);
        }
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private static async Task<IReadOnlyList<DeviceInformation>> FindAsync(string selector, CancellationToken cancellationToken)
    {
        try
        {
            return await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
        }
        catch (Exception e) when (IsWindowsFailure(e))
        {
            Log.WarnThrottled("devices-find", TimeSpan.FromMinutes(5), LogCategory, "Couldn't list Bluetooth devices.", e);
            return [];
        }
    }

    private async Task<BluetoothDevice?> ClassicAsync(string id)
    {
        if (_classic.TryGetValue(id, out var known))
        {
            return known;
        }

        try
        {
            var device = await BluetoothDevice.FromIdAsync(id).AsTask();
            if (device is not null)
            {
                device.ConnectionStatusChanged += OnClassicStatusChanged;
                _classic[id] = device;
            }

            return device;
        }
        catch (Exception e) when (IsWindowsFailure(e))
        {
            Log.WarnThrottled("devices-classic-" + id, TimeSpan.FromMinutes(10), LogCategory, "Couldn't open a Bluetooth device.", e);
            return null;
        }
    }

    private async Task<BluetoothLEDevice?> LowEnergyAsync(string id)
    {
        if (_lowEnergy.TryGetValue(id, out var known))
        {
            return known;
        }

        try
        {
            var device = await BluetoothLEDevice.FromIdAsync(id).AsTask();
            if (device is not null)
            {
                device.ConnectionStatusChanged += OnLowEnergyStatusChanged;
                _lowEnergy[id] = device;
            }

            return device;
        }
        catch (Exception e) when (IsWindowsFailure(e))
        {
            Log.WarnThrottled("devices-le-" + id, TimeSpan.FromMinutes(10), LogCategory, "Couldn't open a Bluetooth LE device.", e);
            return null;
        }
    }

    private void OnClassicStatusChanged(BluetoothDevice sender, object args) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnLowEnergyStatusChanged(BluetoothLEDevice sender, object args) => Changed?.Invoke(this, EventArgs.Empty);

    private void Forget(string id)
    {
        if (_classic.Remove(id, out var classic))
        {
            classic.ConnectionStatusChanged -= OnClassicStatusChanged;
            classic.Dispose();
        }

        if (_lowEnergy.Remove(id, out var lowEnergy))
        {
            lowEnergy.ConnectionStatusChanged -= OnLowEnergyStatusChanged;
            lowEnergy.Dispose();
        }

        if (_sessions.Remove(id, out var session))
        {
            session.Dispose();
        }

        _gattBattery.Remove(id);
    }

    /// <summary>Is the device (Classic or LE) connected right now, as Windows sees it?</summary>
    private bool IsConnected(string id)
    {
        try
        {
            return _classic.TryGetValue(id, out var classic) ? classic.ConnectionStatus == BluetoothConnectionStatus.Connected
                : _lowEnergy.TryGetValue(id, out var le) && le.ConnectionStatus == BluetoothConnectionStatus.Connected;
        }
        catch (Exception e) when (IsWindowsFailure(e))
        {
            return false;
        }
    }

    private async Task<bool> WaitForAsync(string id, bool connected, TimeSpan limit, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < limit)
        {
            if (IsConnected(id) == connected)
            {
                return true;
            }

            await Task.Delay(300, cancellationToken);
        }

        return IsConnected(id) == connected;
    }

    /// <summary>Reads the GATT Battery Service of a connected LE device (at most every two minutes).</summary>
    private async Task<int?> GattBatteryAsync(string id, BluetoothLEDevice device, CancellationToken cancellationToken)
    {
        if (_gattBattery.TryGetValue(id, out var known) && DateTime.UtcNow - known.At < GattBatteryInterval)
        {
            return known.Level;
        }

        int? level = null;
        try
        {
            var services = await WithTimeout(device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached).AsTask(cancellationToken), OperationTimeout);
            if (services is { Status: GattCommunicationStatus.Success })
            {
                foreach (var service in services.Services)
                {
                    using (service)
                    {
                        if (level is not null)
                        {
                            continue;
                        }

                        var characteristics = await WithTimeout(service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached).AsTask(cancellationToken), OperationTimeout);
                        if (characteristics is { Status: GattCommunicationStatus.Success } && characteristics.Characteristics.Count > 0)
                        {
                            var read = await WithTimeout(characteristics.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken), OperationTimeout);
                            if (read is { Status: GattCommunicationStatus.Success } && read.Value is { Length: > 0 } buffer)
                            {
                                using var reader = DataReader.FromBuffer(buffer);
                                int value = reader.ReadByte();
                                level = value is >= 0 and <= 100 ? value : null;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e) when (IsWindowsFailure(e))
        {
            // no battery service, or the device is busy: no reading this time
        }

        _gattBattery[id] = (level, DateTime.UtcNow);
        return level;
    }

    private static string Named(string? deviceName, string? listedName) =>
        !string.IsNullOrWhiteSpace(deviceName) ? deviceName.Trim()
        : !string.IsNullOrWhiteSpace(listedName) ? listedName.Trim()
        : "Bluetooth device";

    /// <summary>The result, or null if Windows didn't answer in time (the operation is left to finish on its own).</summary>
    private static async Task<T?> WithTimeout<T>(Task<T> task, TimeSpan limit)
        where T : class
    {
        var finished = await Task.WhenAny(task, Task.Delay(limit));
        if (finished != task)
        {
            _ = task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default); // observe a late failure
            return null;
        }

        return await task;
    }

    private static async Task IgnoreFailures<T>(Task<T> task, TimeSpan limit)
        where T : class
    {
        try
        {
            await WithTimeout(task, limit);
        }
        catch (Exception e) when (IsWindowsFailure(e))
        {
            // paging failed: the wait below decides
        }
    }

    private static bool IsWindowsFailure(Exception e) =>
        e is COMException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException
            or OperationCanceledException or ObjectDisposedException or DllNotFoundException or EntryPointNotFoundException;
}
