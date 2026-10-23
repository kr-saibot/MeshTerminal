using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MeshTerminal.Client
{
    public sealed class BluetoothDeviceInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public override string ToString() { return (String.IsNullOrWhiteSpace(Name) ? "Bluetooth device" : Name) + " | " + Id; }
    }

    internal static class BleProfile
    {
        internal static Guid Service(MeshProtocol protocol) { return new Guid(protocol == MeshProtocol.MeshCore ? "6e400001-b5a3-f393-e0a9-e50e24dcca9e" : "6ba1b218-15a8-461f-9fa8-5dcae273eafd"); }
        internal static Guid Write(MeshProtocol protocol) { return new Guid(protocol == MeshProtocol.MeshCore ? "6e400002-b5a3-f393-e0a9-e50e24dcca9e" : "f75c76d2-129e-4dad-a1dd-7866124401e7"); }
        internal static Guid Read(MeshProtocol protocol) { return new Guid(protocol == MeshProtocol.MeshCore ? "6e400003-b5a3-f393-e0a9-e50e24dcca9e" : "2c55e69e-4993-11ed-b878-0242ac120002"); }
        internal static Guid Notify(MeshProtocol protocol) { return new Guid(protocol == MeshProtocol.MeshCore ? "6e400003-b5a3-f393-e0a9-e50e24dcca9e" : "ed9da18c-a800-4f66-a670-aa7547e34453"); }
    }

    internal interface IBleConnection : IDisposable
    {
        Action<byte[]> Notification { get; set; }
        Action<Exception> Disconnected { get; set; }
        Task ConnectAsync(string id, MeshProtocol protocol, CancellationToken token);
        Task WriteAsync(byte[] payload, CancellationToken token);
        Task<byte[]> ReadAsync(CancellationToken token);
    }

    internal static class BlePlatform
    {
        internal static IBleConnection Create()
        {
#if BLUETOOTH_WINDOWS
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) throw new PlatformNotSupportedException("Use the Linux build for Bluetooth on Linux.");
            return new WindowsBleConnection();
#else
            return new LinuxBleConnection();
#endif
        }
        internal static Task<IList<BluetoothDeviceInfo>> ScanAsync(MeshProtocol protocol, CancellationToken token)
        {
#if BLUETOOTH_WINDOWS
            return WindowsBleConnection.ScanAsync(protocol, token);
#else
            return LinuxBleConnection.ScanAsync(protocol, token);
#endif
        }
        internal static async Task<T> AwaitAsync<T>(Task<T> operation, CancellationToken token)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(operation, canceled.Task).ConfigureAwait(false) != operation)
                {
                    _ = operation.ContinueWith(task => { var ignored = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    token.ThrowIfCancellationRequested();
                }
                return await operation.ConfigureAwait(false);
            }
        }
        internal static async Task AwaitAsync(Task operation, CancellationToken token)
        {
            await AwaitAsync(operation.ContinueWith(task => { task.GetAwaiter().GetResult(); return true; }, TaskScheduler.Default), token).ConfigureAwait(false);
        }
    }

    // Converts packet-oriented GATT into the framed stream consumed by our
    // existing protocol readers. BLE never transmits the USB/TCP framing bytes.
    internal sealed class BlePacketStream : Stream
    {
        private readonly MeshProtocol _protocol;
        private readonly Func<byte[], CancellationToken, Task> _write;
        private readonly object _gate = new object();
        private readonly Queue<byte[]> _incoming = new Queue<byte[]>();
        private TaskCompletionSource<bool> _ready = NewSignal();
        private byte[] _current; private int _offset;
        private Exception _error;
        private bool _closed;
        private static TaskCompletionSource<bool> NewSignal() { return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); }
        internal BlePacketStream(MeshProtocol protocol, Func<byte[], CancellationToken, Task> write) { _protocol = protocol; _write = write; }
        internal void Receive(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return;
            if (payload.Length > 512) { Fail(new IOException("Bluetooth packet exceeds 512 bytes.")); return; }
            var header = _protocol == MeshProtocol.MeshCore
                ? new byte[] { (byte)'>', (byte)payload.Length, (byte)(payload.Length >> 8) }
                : new byte[] { 0x94, 0xc3, (byte)(payload.Length >> 8), (byte)payload.Length };
            var packet = header.Concat(payload).ToArray();
            lock (_gate) { if (_closed) return; _incoming.Enqueue(packet); _ready.TrySetResult(true); }
        }
        internal void Fail(Exception error) { lock (_gate) { _error = error; _closed = true; _ready.TrySetResult(true); } }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            if (count == 0) return 0;
            while (true)
            {
                Task wait;
                lock (_gate)
                {
                    if (_error != null) throw new IOException("Bluetooth connection closed.", _error);
                    if (_current == null && _incoming.Count > 0) { _current = _incoming.Dequeue(); _offset = 0; }
                    if (_current != null)
                    {
                        var copied = Math.Min(count, _current.Length - _offset);
                        Buffer.BlockCopy(_current, _offset, buffer, offset, copied); _offset += copied;
                        if (_offset == _current.Length) _current = null;
                        return copied;
                    }
                    if (_closed) return 0;
                    if (_ready.Task.IsCompleted) _ready = NewSignal();
                    wait = _ready.Task;
                }
                await BlePlatform.AwaitAsync(wait, token).ConfigureAwait(false);
            }
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_gate) { if (_closed) throw new IOException("Bluetooth connection is closed."); }
            var header = _protocol == MeshProtocol.MeshCore ? 3 : 4;
            if (count < header) throw new InvalidDataException("Incomplete Bluetooth transport frame.");
            var valid = _protocol == MeshProtocol.MeshCore ? buffer[offset] == (byte)'<' : buffer[offset] == 0x94 && buffer[offset + 1] == 0xc3;
            var length = _protocol == MeshProtocol.MeshCore ? buffer[offset + 1] | buffer[offset + 2] << 8 : buffer[offset + 2] << 8 | buffer[offset + 3];
            if (!valid || length != count - header || length < 1 || length > 512) throw new InvalidDataException("Invalid Bluetooth transport frame.");
            var payload = new byte[length]; Buffer.BlockCopy(buffer, offset + header, payload, 0, length);
            return _write(payload, token);
        }
        protected override void Dispose(bool disposing) { lock (_gate) { _closed = true; _ready.TrySetResult(true); } base.Dispose(disposing); }
        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override int Read(byte[] b, int o, int c) { return ReadAsync(b, o, c, CancellationToken.None).GetAwaiter().GetResult(); }
        public override void Write(byte[] b, int o, int c) { WriteAsync(b, o, c, CancellationToken.None).GetAwaiter().GetResult(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); }
        public override void SetLength(long v) { throw new NotSupportedException(); }
    }

    internal sealed class BluetoothTransport : ITransport
    {
        private readonly string _id; private readonly MeshProtocol _protocol;
        private readonly IBleConnection _connection;
        private readonly CancellationTokenSource _closed = new CancellationTokenSource();
        private readonly SemaphoreSlim _readSignal = new SemaphoreSlim(0, 1);
        private readonly SemaphoreSlim _gatt = new SemaphoreSlim(1, 1);
        private BlePacketStream _stream; private int _disposed;
        internal BluetoothTransport(string id, MeshProtocol protocol) : this(id, protocol, BlePlatform.Create()) { }
        internal BluetoothTransport(string id, MeshProtocol protocol, IBleConnection connection) { _id = id; _protocol = protocol; _connection = connection; }
        public Stream Stream { get { return _stream; } }
        public string Kind { get { return "Bluetooth LE"; } }
        public string Endpoint { get { return _id; } }
        public bool UseSynchronousWrites { get { return false; } }
        public void Write(byte[] b, int o, int c) { _stream.Write(b, o, c); }
        private void SignalRead() { if (_readSignal.CurrentCount == 0) try { _readSignal.Release(); } catch (SemaphoreFullException) { } }
        public async Task OpenAsync(CancellationToken token)
        {
            _stream = new BlePacketStream(_protocol, WritePayloadAsync);
            _connection.Notification = bytes => { if (_protocol == MeshProtocol.MeshCore) _stream.Receive(bytes); else SignalRead(); };
            _connection.Disconnected = error => { _stream.Fail(error ?? new IOException("Bluetooth device disconnected.")); Close(); };
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _closed.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                using (deadline.Token.Register(Close))
                    await _connection.ConnectAsync(_id, _protocol, deadline.Token).ConfigureAwait(false);
            }
            if (_protocol == MeshProtocol.Meshtastic) { SignalRead(); _ = DrainAsync(); }
        }
        private async Task WritePayloadAsync(byte[] payload, CancellationToken token)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _closed.Token))
            {
                await _gatt.WaitAsync(linked.Token).ConfigureAwait(false);
                try { await _connection.WriteAsync(payload, linked.Token).ConfigureAwait(false); }
                finally { _gatt.Release(); }
            }
            if (_protocol == MeshProtocol.Meshtastic) SignalRead();
        }
        private async Task DrainAsync()
        {
            try
            {
                while (!_closed.IsCancellationRequested)
                {
                    await _readSignal.WaitAsync(TimeSpan.FromSeconds(2), _closed.Token).ConfigureAwait(false);
                    for (var index = 0; index < 64; index++)
                    {
                        byte[] data;
                        await _gatt.WaitAsync(_closed.Token).ConfigureAwait(false);
                        try { data = await _connection.ReadAsync(_closed.Token).ConfigureAwait(false); }
                        finally { _gatt.Release(); }
                        if (data == null || data.Length == 0) break;
                        _stream.Receive(data);
                        if (index == 63) SignalRead();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _stream.Fail(ex); Close(); }
        }
        public void Close()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _closed.Cancel(); if (_stream != null) _stream.Dispose(); _connection.Dispose();
        }
        public void Dispose() { Close(); }
    }
}