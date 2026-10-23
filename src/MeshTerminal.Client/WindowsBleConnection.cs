#if BLUETOOTH_WINDOWS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InTheHand.Bluetooth;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
using NativeGatt = Windows.Devices.Bluetooth.GenericAttributeProfile;
namespace MeshTerminal.Client
{
    internal sealed class WindowsBleConnection : IBleConnection
    {
        private BluetoothDevice _device;
        private GattCharacteristic _write, _read, _notify;
        private int _disposed;
        public Action<byte[]> Notification { get; set; }
        public Action<Exception> Disconnected { get; set; }
        internal static async Task<IList<BluetoothDeviceInfo>> ScanAsync(MeshProtocol protocol, CancellationToken token)
        {
            if (!await BlePlatform.AwaitAsync(Bluetooth.GetAvailabilityAsync(), token).ConfigureAwait(false)) throw new IOException("Bluetooth is unavailable. Enable the Windows Bluetooth adapter.");
            var pending = Bluetooth.GetPairedDevicesAsync();
            IReadOnlyCollection<BluetoothDevice> devices;
            try { devices = await BlePlatform.AwaitAsync(pending, token).ConfigureAwait(false); }
            catch { _ = pending.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) foreach (var d in t.Result) d.Dispose(); }, TaskScheduler.Default); throw; }
            try { return devices.Select(d => new BluetoothDeviceInfo { Id = d.Id, Name = d.Name }).OrderBy(d => d.Name).ToList(); }
            finally { foreach (var d in devices) d.Dispose(); }
        }
        public async Task ConnectAsync(string id, MeshProtocol protocol, CancellationToken token)
        {
            try
            {
                var pending = BluetoothDevice.FromIdAsync(id.Replace(":", "").Replace("-", ""));
                BluetoothDevice device;
                try { device = await BlePlatform.AwaitAsync(pending, token).ConfigureAwait(false); }
                catch { _ = pending.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion && t.Result != null) t.Result.Dispose(); }, TaskScheduler.Default); throw; }
                if (device == null) throw new IOException("Bluetooth device was not found.");
                _device = device;
                if (Volatile.Read(ref _disposed) != 0) { device.Dispose(); throw new OperationCanceledException(token); }
                await BlePlatform.AwaitAsync(device.Gatt.ConnectAsync(), token).ConfigureAwait(false);
                var service = await BlePlatform.AwaitAsync(device.Gatt.GetPrimaryServiceAsync(BleProfile.Service(protocol)), token).ConfigureAwait(false);
                if (service == null) throw new IOException("Device does not offer the selected protocol's Bluetooth service. Check the firmware.");
                _write = await BlePlatform.AwaitAsync(service.GetCharacteristicAsync(BleProfile.Write(protocol)), token).ConfigureAwait(false);
                _read = await BlePlatform.AwaitAsync(service.GetCharacteristicAsync(BleProfile.Read(protocol)), token).ConfigureAwait(false);
                _notify = await BlePlatform.AwaitAsync(service.GetCharacteristicAsync(BleProfile.Notify(protocol)), token).ConfigureAwait(false);
                if (_write == null || _read == null || _notify == null) throw new IOException("Required Bluetooth characteristics are missing.");
                device.GattServerDisconnected += OnDisconnected;
                _notify.CharacteristicValueChanged += OnNotification;
                await BlePlatform.AwaitAsync(_notify.StartNotificationsAsync(), token).ConfigureAwait(false);
            }
            catch { Dispose(); throw; }
        }
        private void OnDisconnected(object sender, EventArgs args) { Disconnected?.Invoke(new IOException("Bluetooth device disconnected.")); }
        private void OnNotification(object sender, GattCharacteristicValueChangedEventArgs args)
        {
            if (args.Error != null) Disconnected?.Invoke(args.Error); else Notification?.Invoke(args.Value);
        }
        public async Task WriteAsync(byte[] payload, CancellationToken token)
        {
            NativeGatt.GattCharacteristic native = _write;
            var option = (_write.Properties & GattCharacteristicProperties.Write) != 0 ? NativeGatt.GattWriteOption.WriteWithResponse : NativeGatt.GattWriteOption.WriteWithoutResponse;
            var result = await BlePlatform.AwaitAsync(native.WriteValueWithResultAsync(payload.AsBuffer(), option).AsTask(), token).ConfigureAwait(false);
            if (result.Status != NativeGatt.GattCommunicationStatus.Success)
                throw new IOException("Windows BLE write failed: " + result.Status + (result.ProtocolError.HasValue ? "; ATT error 0x" + result.ProtocolError.Value.ToString("X2") : "") + "; payload " + payload.Length + " bytes; mode " + option + ".");
        }
        public async Task<byte[]> ReadAsync(CancellationToken token)
        {
            NativeGatt.GattCharacteristic native = _read;
            var result = await BlePlatform.AwaitAsync(native.ReadValueAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached).AsTask(), token).ConfigureAwait(false);
            if (result.Status != NativeGatt.GattCommunicationStatus.Success) return new byte[0];
            return result.Value == null || result.Value.Length == 0 ? new byte[0] : result.Value.ToArray();
        }        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_notify != null) _notify.CharacteristicValueChanged -= OnNotification;
            var device = _device;
            if (device != null) { device.GattServerDisconnected -= OnDisconnected; try { device.Gatt.Disconnect(); } catch { } device.Dispose(); }
        }
    }
}
#endif
