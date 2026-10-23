#if !BLUETOOTH_WINDOWS
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;
namespace MeshTerminal.Client
{
    // Explicit readers avoid the emitted PropertyChanges delegate that crashes
    // Mono 6.12 in Tmds.DBus issue #155/#214.
    internal sealed class BlueZDbus : IDisposable
    {
        internal const string DeviceInterface = "org.bluez.Device1";
        internal const string CharacteristicInterface = "org.bluez.GattCharacteristic1";
        private readonly DBusConnection _connection;
        private readonly string _destination;
        private readonly bool _peer;
        internal BlueZDbus(string address, string destination = "org.bluez", bool peer = false)
            : this(new DBusConnectionOptions(address), destination, peer) { }
        internal BlueZDbus(DBusConnectionOptions options, string destination, bool peer)
        {
            _connection = new DBusConnection(options);
            _destination = destination;
            _peer = peer;
        }
        internal Task ConnectAsync() { return _connection.ConnectAsync().AsTask(); }
        public void Dispose() { _connection.Dispose(); }
        internal Task<Dictionary<string, Dictionary<string, Dictionary<string, VariantValue>>>> GetObjectsAsync()
        {
            return _connection.CallMethodAsync(CreateCall("/", "org.freedesktop.DBus.ObjectManager", "GetManagedObjects"), ReadObjects);
        }
        private static Dictionary<string, Dictionary<string, Dictionary<string, VariantValue>>> ReadObjects(Message message, object state)
        {
            var reader = message.GetBodyReader();
            var result = new Dictionary<string, Dictionary<string, Dictionary<string, VariantValue>>>();
            var objects = reader.ReadArrayStart(DBusType.Struct);
            while (reader.HasNext(objects))
            {
                reader.AlignStruct();
                var path = reader.ReadObjectPathAsString();
                var interfaces = new Dictionary<string, Dictionary<string, VariantValue>>();
                var entries = reader.ReadArrayStart(DBusType.Struct);
                while (reader.HasNext(entries))
                {
                    reader.AlignStruct();
                    var name = reader.ReadString();
                    interfaces.Add(name, ReadProperties(ref reader));
                }
                result.Add(path, interfaces);
            }
            return result;
        }
        private static Dictionary<string, VariantValue> ReadProperties(ref Reader reader)
        {
            var result = new Dictionary<string, VariantValue>();
            var entries = reader.ReadArrayStart(DBusType.Struct);
            while (reader.HasNext(entries))
            {
                reader.AlignStruct();
                var name = reader.ReadString();
                result.Add(name, reader.ReadVariantValue());
            }
            return result;
        }
        private static Dictionary<string, VariantValue> ReadChanges(Message message, object state)
        {
            var reader = message.GetBodyReader();
            reader.ReadString();
            var result = ReadProperties(ref reader);
            reader.ReadArrayOfString();
            return result;
        }
        internal Task<bool> GetBoolAsync(string path, string property)
        {
            return _connection.CallMethodAsync(CreatePropertyGet(path, property),
                (message, state) => message.GetBodyReader().ReadVariantValue().GetBool());
        }
        private MessageBuffer CreatePropertyGet(string path, string property)
        {
            var writer = _connection.GetMessageWriter();
            try
            {
                writer.WriteMethodCallHeader(destination: _destination, path: path, @interface: "org.freedesktop.DBus.Properties", member: "Get", signature: "ss");
                writer.WriteString(DeviceInterface);
                writer.WriteString(property);
                return writer.CreateMessage();
            }
            finally { writer.Dispose(); }
        }
        internal Task CallAsync(string path, string iface, string member)
        {
            return _connection.CallMethodAsync(CreateCall(path, iface, member));
        }
        private MessageBuffer CreateCall(string path, string iface, string member)
        {
            var writer = _connection.GetMessageWriter();
            try
            {
                writer.WriteMethodCallHeader(destination: _destination, path: path, @interface: iface, member: member);
                return writer.CreateMessage();
            }
            finally { writer.Dispose(); }
        }
        internal Task WriteAsync(string path, byte[] payload, string type)
        {
            return _connection.CallMethodAsync(CreateWrite(path, payload, type));
        }
        private MessageBuffer CreateWrite(string path, byte[] payload, string type)
        {
            var writer = _connection.GetMessageWriter();
            try
            {
                writer.WriteMethodCallHeader(destination: _destination, path: path, @interface: CharacteristicInterface, member: "WriteValue", signature: "aya{sv}");
                writer.WriteArray(payload);
                var options = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("type");
                writer.WriteVariant(VariantValue.String(type));
                writer.WriteDictionaryEnd(options);
                return writer.CreateMessage();
            }
            finally { writer.Dispose(); }
        }
        internal Task<byte[]> ReadAsync(string path)
        {
            return _connection.CallMethodAsync(CreateRead(path), (message, state) => message.GetBodyReader().ReadArrayOfByte());
        }
        private MessageBuffer CreateRead(string path)
        {
            var writer = _connection.GetMessageWriter();
            try
            {
                writer.WriteMethodCallHeader(destination: _destination, path: path, @interface: CharacteristicInterface, member: "ReadValue", signature: "a{sv}");
                var options = writer.WriteDictionaryStart();
                writer.WriteDictionaryEnd(options);
                return writer.CreateMessage();
            }
            finally { writer.Dispose(); }
        }
        internal Task<IDisposable> WatchAsync(string path, string iface, Action<Dictionary<string, VariantValue>> handler, Action<Exception> onError)
        {
            var rule = new MatchRule { Type = MessageType.Signal, Sender = _destination, Path = path,
                Interface = "org.freedesktop.DBus.Properties", Member = "PropertiesChanged", Arg0 = iface };
            var flags = ObserverFlags.EmitOnConnectionFailed | ObserverFlags.EmitOnReaderFailed;
            if (_peer) flags |= ObserverFlags.NoSubscribe;
            return _connection.AddMatchAsync(rule, ReadChanges, notification =>
            {
                if (notification.HasValue) handler(notification.Value);
                else if (notification.IsCompletion && notification.Type != NotificationType.ObserverDisposed)
                    onError(notification.Exception);
            }, emitOnCapturedContext: false, flags: flags).AsTask();
        }
    }
}
#endif