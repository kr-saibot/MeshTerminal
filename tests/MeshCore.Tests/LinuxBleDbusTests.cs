using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Client;
using Tmds.DBus;

[DBusInterface("org.freedesktop.DBus.ObjectManager")]
public interface ITestBleManager : IDBusObject
{
    Task<IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>>> GetManagedObjectsAsync();
}
[DBusInterface("org.bluez.GattCharacteristic1")]
public interface ITestBleCharacteristic : IDBusObject
{
    Task<byte[]> ReadValueAsync(IDictionary<string, object> options);
    Task WriteValueAsync(byte[] data, IDictionary<string, object> options);
    Task StartNotifyAsync();
    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
}
internal static class LinuxBleDbusTests
{
    internal static async Task RunAsync()
    {
        var options = new ServerConnectionOptions();
        using (var server = new Connection(options))
        {
            await server.ConnectAsync();
            var manager = new Manager();
            var characteristic = new Characteristic();
            await server.RegisterObjectsAsync(new IDBusObject[] { manager, characteristic });
            var address = await options.StartAsync("tcp:host=127.0.0.1,port=0");
            using (var bus = new BlueZDbus(new PeerOptions(address), null, true))
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                await BlePlatform.AwaitAsync(bus.ConnectAsync(), deadline.Token);
                var objects = await BlePlatform.AwaitAsync(bus.GetObjectsAsync(), deadline.Token);
                var device = objects[Manager.DevicePath]["org.bluez.Device1"];
                Check(device["Paired"].GetBool(), "BlueZ paired boolean");
                Check(device["Alias"].GetString() == "Test radio", "BlueZ device name");
                Check(objects[Manager.ServicePath]["org.bluez.GattService1"]["Device"].GetObjectPathAsString() == Manager.DevicePath, "BlueZ service object path");
                Check(objects[Characteristic.Path]["org.bluez.GattCharacteristic1"]["Flags"].GetArray<string>().SequenceEqual(new[] { "write", "notify" }), "BlueZ flags array");
                var payload = new byte[] { 1, 2, 0, 255 };
                await BlePlatform.AwaitAsync(bus.WriteAsync(Characteristic.Path, payload, "request"), deadline.Token);
                Check(characteristic.Data.SequenceEqual(payload) && characteristic.WriteType == "request", "GATT byte array and write options");
                Check((await BlePlatform.AwaitAsync(bus.ReadAsync(Characteristic.Path), deadline.Token)).SequenceEqual(payload), "GATT read response");
                characteristic.Data = new byte[0];
                Check((await BlePlatform.AwaitAsync(bus.ReadAsync(Characteristic.Path), deadline.Token)).Length == 0, "GATT empty response");
                var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (await BlePlatform.AwaitAsync(bus.WatchAsync(Characteristic.Path, BlueZDbus.CharacteristicInterface,
                    values => received.TrySetResult(values["Value"].GetArray<byte>()), error => received.TrySetException(error)), deadline.Token))
                {
                    await BlePlatform.AwaitAsync(bus.CallAsync(Characteristic.Path, BlueZDbus.CharacteristicInterface, "StartNotify"), deadline.Token);
                    Check(characteristic.Notifying, "GATT StartNotify");
                    characteristic.Emit(payload);
                    Check((await BlePlatform.AwaitAsync(received.Task, deadline.Token)).SequenceEqual(payload), "BlueZ PropertiesChanged decoded without emitted delegates");
                }
            }
        }
        Console.WriteLine("Linux BlueZ D-Bus wire tests passed (discovery, read/write, notifications).");
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    private sealed class PeerOptions : Tmds.DBus.Protocol.DBusConnectionOptions
    {
        private readonly string _address;
        internal PeerOptions(string address) : base(address) { _address = address; }
        protected override ValueTask<SetupResult> SetupAsync(CancellationToken token)
        {
            return new ValueTask<SetupResult>(new SetupResult(_address) { UserId = "0", MachineId = Guid.Empty.ToString("N") });
        }
    }
    private sealed class Manager : ITestBleManager
    {
        internal const string DevicePath = "/org/bluez/hci0/dev_00_00_00_00_00_00";
        internal const string ServicePath = DevicePath + "/service0001";
        public ObjectPath ObjectPath { get { return new ObjectPath("/"); } }
        public Task<IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>>> GetManagedObjectsAsync()
        {
            IDictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>> objects =
                new Dictionary<ObjectPath, IDictionary<string, IDictionary<string, object>>> {
                    { new ObjectPath(DevicePath), new Dictionary<string, IDictionary<string, object>> {
                        { "org.bluez.Device1", new Dictionary<string, object> { { "Address", "00:00:00:00:00:00" }, { "Alias", "Test radio" }, { "Paired", true } } } } },
                    { new ObjectPath(ServicePath), new Dictionary<string, IDictionary<string, object>> {
                        { "org.bluez.GattService1", new Dictionary<string, object> { { "Device", new ObjectPath(DevicePath) }, { "UUID", BleProfile.Service(MeshProtocol.MeshCore).ToString() } } } } },
                    { new ObjectPath(Characteristic.Path), new Dictionary<string, IDictionary<string, object>> {
                        { "org.bluez.GattCharacteristic1", new Dictionary<string, object> { { "Flags", new[] { "write", "notify" } }, { "Service", new ObjectPath(ServicePath) } } } } }
                };
            return Task.FromResult(objects);
        }
    }
    private sealed class Characteristic : ITestBleCharacteristic
    {
        internal const string Path = Manager.ServicePath + "/char0002";
        internal byte[] Data = new byte[0];
        internal string WriteType;
        internal bool Notifying;
        private event Action<PropertyChanges> Changed;
        public ObjectPath ObjectPath { get { return new ObjectPath(Path); } }
        public Task<byte[]> ReadValueAsync(IDictionary<string, object> options) { return Task.FromResult(Data); }
        public Task WriteValueAsync(byte[] data, IDictionary<string, object> options) { Data = data; WriteType = (string)options["type"]; return Task.CompletedTask; }
        public Task StartNotifyAsync() { Notifying = true; return Task.CompletedTask; }
        public Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler)
        {
            Changed += handler;
            return Task.FromResult<IDisposable>(new Subscription(() => Changed -= handler));
        }
        internal void Emit(byte[] payload)
        {
            Changed?.Invoke(new PropertyChanges(new[] { new KeyValuePair<string, object>("Value", payload) }, new string[0]));
        }
    }
    private sealed class Subscription : IDisposable
    {
        private readonly Action _close;
        internal Subscription(Action close) { _close = close; }
        public void Dispose() { _close(); }
    }
}