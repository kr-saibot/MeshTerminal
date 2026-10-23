#if !BLUETOOTH_WINDOWS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;
namespace MeshTerminal.Client
{
    internal sealed class LinuxBleConnection : IBleConnection
    {
        private BlueZDbus _bus;
        private string _device;
        private string _write, _read, _notify;
        private IDisposable _deviceWatch, _valueWatch;
        private string _writeType = "request";
        private int _disposed;
        public Action<byte[]> Notification { get; set; }
        public Action<Exception> Disconnected { get; set; }
        private static string Normalize(string value) { return (value ?? "").Replace(":", "").Replace("-", "").ToUpperInvariant(); }
        internal static async Task<IList<BluetoothDeviceInfo>> ScanAsync(MeshProtocol protocol, CancellationToken token)
        {
            using (var bus = new BlueZDbus(DBusAddress.System))
            {
                await BlePlatform.AwaitAsync(bus.ConnectAsync(), token).ConfigureAwait(false);
                var objects = await BlePlatform.AwaitAsync(bus.GetObjectsAsync(), token).ConfigureAwait(false);
                return objects.Values.Where(v => v.ContainsKey("org.bluez.Device1")).Select(v => v["org.bluez.Device1"])
                    .Where(v => v.ContainsKey("Paired") && v["Paired"].GetBool())
                    .Select(v => new BluetoothDeviceInfo { Id = v["Address"].GetString(), Name = v.ContainsKey("Alias") ? v["Alias"].GetString() : v["Address"].GetString() })
                    .OrderBy(v => v.Name).ToList();
            }
        }
        public async Task ConnectAsync(string id, MeshProtocol protocol, CancellationToken token)
        {
            try
            {
                _bus = new BlueZDbus(DBusAddress.System);
                await BlePlatform.AwaitAsync(_bus.ConnectAsync(), token).ConfigureAwait(false);
                var manager = _bus;
                var objects = await BlePlatform.AwaitAsync(manager.GetObjectsAsync(), token).ConfigureAwait(false);
                var match = objects.FirstOrDefault(o => o.Value.ContainsKey("org.bluez.Device1") && Normalize(o.Value["org.bluez.Device1"]["Address"].GetString()) == Normalize(id));
                if (match.Value == null) throw new IOException("Bluetooth device was not found. Pair it in the Linux Bluetooth settings first.");
                if (!match.Value["org.bluez.Device1"]["Paired"].GetBool()) throw new IOException("Pair the device in the Linux Bluetooth settings first.");
                _device = match.Key;
                await BlePlatform.AwaitAsync(_bus.CallAsync(_device, BlueZDbus.DeviceInterface, "Connect"), token).ConfigureAwait(false);
                while (!await BlePlatform.AwaitAsync(_bus.GetBoolAsync(_device, "ServicesResolved"), token).ConfigureAwait(false)) await Task.Delay(150, token).ConfigureAwait(false);
                objects = await BlePlatform.AwaitAsync(manager.GetObjectsAsync(), token).ConfigureAwait(false);
                var service = objects.FirstOrDefault(o => o.Value.ContainsKey("org.bluez.GattService1") && o.Value["org.bluez.GattService1"].ContainsKey("Device") && o.Value["org.bluez.GattService1"]["Device"].GetObjectPathAsString() == match.Key.ToString() && Guid.Parse(o.Value["org.bluez.GattService1"]["UUID"].GetString()) == BleProfile.Service(protocol));
                if (service.Value == null) throw new IOException("Device does not offer the selected protocol's Bluetooth service. Check the firmware.");
                Func<Guid, string> characteristic = uuid =>
                {
                    var entry = objects.FirstOrDefault(o => o.Value.ContainsKey("org.bluez.GattCharacteristic1") && o.Value["org.bluez.GattCharacteristic1"]["Service"].GetObjectPathAsString() == service.Key.ToString() && Guid.Parse(o.Value["org.bluez.GattCharacteristic1"]["UUID"].GetString()) == uuid);
                    if (entry.Value == null) throw new IOException("Required Bluetooth characteristic is missing.");
                    if (uuid == BleProfile.Write(protocol))
                    {
                        var flags = entry.Value["org.bluez.GattCharacteristic1"]["Flags"].GetArray<string>();
                        _writeType = flags.Contains("write") ? "request" : "command";
                    }
                    return entry.Key;
                };
                _write = characteristic(BleProfile.Write(protocol)); _read = characteristic(BleProfile.Read(protocol)); _notify = characteristic(BleProfile.Notify(protocol));
                _deviceWatch = await BlePlatform.AwaitAsync(_bus.WatchAsync(_device, BlueZDbus.DeviceInterface, changes =>
                {
                    foreach (var change in changes) if (change.Key == "Connected" && !change.Value.GetBool()) Disconnected?.Invoke(new IOException("Bluetooth device disconnected."));
                }, error => Disconnected?.Invoke(error)), token).ConfigureAwait(false);
                _valueWatch = await BlePlatform.AwaitAsync(_bus.WatchAsync(_notify, BlueZDbus.CharacteristicInterface, changes =>
                {
                    foreach (var change in changes) if (change.Key == "Value") Notification?.Invoke(change.Value.GetArray<byte>());
                }, error => Disconnected?.Invoke(error)), token).ConfigureAwait(false);
                await BlePlatform.AwaitAsync(_bus.CallAsync(_notify, BlueZDbus.CharacteristicInterface, "StartNotify"), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }
            catch { Dispose(); throw; }
        }
        public Task WriteAsync(byte[] payload, CancellationToken token) { return BlePlatform.AwaitAsync(_bus.WriteAsync(_write, payload, _writeType), token); }
        public Task<byte[]> ReadAsync(CancellationToken token) { return BlePlatform.AwaitAsync(_bus.ReadAsync(_read), token); }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _valueWatch?.Dispose(); _deviceWatch?.Dispose();
            var bus = _bus; var device = _device;
            if (bus != null) _ = CloseAsync(bus, device);
        }
        private static async Task CloseAsync(BlueZDbus bus, string device)
        {
            try { if (device != null) using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2))) await BlePlatform.AwaitAsync(bus.CallAsync(device, BlueZDbus.DeviceInterface, "Disconnect"), deadline.Token).ConfigureAwait(false); }
            catch { }
            finally { bus.Dispose(); }
        }
    }
}
#endif
