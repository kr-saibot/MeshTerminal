using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MeshTerminal.Client
{
    // Companion protocol v3, USB/TCP framing. Commands have no request IDs,
    // so keep exactly one outstanding command, including message polling.
    internal sealed class NodeSynchronizationSkippedException : Exception { }
    internal sealed class MeshCoreSession
    {
        private readonly Stream _stream;
        private readonly Action _close;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _contactTimeout;
        private readonly Action<byte[]> _onFrame;
        private readonly SemaphoreSlim _commands = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _remoteRequests = new SemaphoreSlim(1, 1);
        private readonly object _gate = new object();
        private TaskCompletionSource<byte[]> _response;
        private byte[] _expected;
        private Action<byte[]> _accepted;
        private Exception _readerError;
        private CancellationTokenSource _contactDeadline;
        private TaskCompletionSource<byte[]> _pushResponse;
        private Func<byte[], bool> _pushMatches;
        private readonly ConcurrentDictionary<uint, uint> _ackPackets = new ConcurrentDictionary<uint, uint>();
        private readonly CancellationTokenSource _closed = new CancellationTokenSource();
        public volatile bool PollMessages;
        public Action<string, byte[], byte[]> FrameTraffic;
        public Action FrameSent;
        public Action<uint, MessageDeliveryState> DeliveryChanged;

        public MeshCoreSession(Stream stream, Action close, TimeSpan timeout, Action<byte[]> onFrame, TimeSpan? contactTimeout = null)
        {
            _stream = stream; _close = close; _timeout = timeout; _contactTimeout = contactTimeout ?? timeout; _onFrame = onFrame;
        }

        internal bool SkipContactTransfer()
        {
            lock (_gate) return _contactDeadline != null && _response != null &&
                _response.TrySetException(new NodeSynchronizationSkippedException());
        }
        public async Task<byte[]> CommandAsync(byte[] payload, byte[] expected, CancellationToken token, Action<byte[]> accepted = null, bool keepConnectionOnTimeout = false)
        {
            await _commands.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_gate)
                {
                    if (_readerError != null) throw new IOException("MeshCore receive connection closed.", _readerError);
                    _expected = expected; _accepted = accepted; _response = completion;
                }
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    // Contact lists are streamed one BLE notification at a time.
                    // Renew the inactivity interval on contact progress; do not cap total duration.
                    if (payload[0] == 4)
                    {
                        lock (_gate) _contactDeadline = deadline;
                    }
                    deadline.CancelAfter(payload[0] == 4 ? _contactTimeout : _timeout);
                    using (deadline.Token.Register(delegate { completion.TrySetCanceled(); if (!keepConnectionOnTimeout || token.IsCancellationRequested) try { _close(); } catch { } }))
                    {
                        var framed = new byte[payload.Length + 3];
                        framed[0] = (byte)'<'; framed[1] = (byte)payload.Length; framed[2] = (byte)(payload.Length >> 8);
                        Buffer.BlockCopy(payload, 0, framed, 3, payload.Length);
                        // Closing the transport on timeout also interrupts serial writes
                        // on platforms where Stream cancellation is not implemented.
                        await _stream.WriteAsync(framed, 0, framed.Length, deadline.Token).ConfigureAwait(false);
                        await _stream.FlushAsync(deadline.Token).ConfigureAwait(false);
                        if (FrameTraffic != null) FrameTraffic("TX", framed, payload);
                        if (FrameSent != null) FrameSent();
                        byte[] response;
                        try { response = await completion.Task.ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        { throw new TimeoutException("MeshCore did not respond to command 0x" + payload[0].ToString("X2") + " timed out after " + (payload[0] == 4 ? _contactTimeout : _timeout).TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " seconds without a matching response or contact progress." + (keepConnectionOnTimeout ? " The contact transfer can be restarted." : " Connection closed to prevent mismatched responses.")); }
                        if (response[0] == 1 || response[0] == 15)
                            throw new MeshCoreCommandException(payload[0], response.Length > 1 ? (byte?)response[1] : null);
                        return response;
                    }
                }
            }
            finally
            {
                lock (_gate) { _contactDeadline = null; _response = null; _expected = null; _accepted = null; }
                _commands.Release();
            }
        }

        public async Task ReadAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var marker = await ReadExactAsync(1, token).ConfigureAwait(false);
                    if (marker[0] != (byte)'>') continue;
                    var header = await ReadExactAsync(2, token).ConfigureAwait(false);
                    var length = header[0] | (header[1] << 8);
                    if (length == 0 || length > 512) throw new InvalidDataException("Invalid MeshCore frame length: " + length);
                    var frame = await ReadExactAsync(length, token).ConfigureAwait(false);
                    if (FrameTraffic != null) FrameTraffic("RX", marker.Concat(header).Concat(frame).ToArray(), frame);
                    _onFrame(frame);
                    if (frame[0] == 0x82 && frame.Length >= 9)
                    {
                        uint packet;
                        if (_ackPackets.TryRemove(ReadUInt32(frame, 1), out packet) && DeliveryChanged != null)
                            DeliveryChanged(packet, MessageDeliveryState.Delivered);
                    }
                    lock (_gate)
                    {
                        // Only progress belonging to the contact-list command extends its wait.
                        if (_contactDeadline != null && !_response.Task.IsCompleted &&
                            ((frame[0] == 2 && frame.Length >= 5) || (frame[0] == 3 && frame.Length >= 148)))
                        {
                            try { _contactDeadline.CancelAfter(_contactTimeout); } catch (ObjectDisposedException) { }
                        }
                        if (_pushResponse != null && _pushMatches != null && _pushMatches(frame)) _pushResponse.TrySetResult(frame);
                        if (_response != null && !_response.Task.IsCompleted && (frame[0] == 1 || frame[0] == 15 || _expected.Contains(frame[0])))
                        {
                            // Register expected radio ACKs before allowing the reader to
                            // process another push or the next command to start.
                            if (frame[0] != 1 && frame[0] != 15 && _accepted != null) _accepted(frame);
                            _response.TrySetResult(frame);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _closed.Cancel();
                foreach (var pending in _ackPackets.ToArray())
                {
                    uint ignored;
                    if (_ackPackets.TryRemove(pending.Key, out ignored) && DeliveryChanged != null) DeliveryChanged(pending.Value, MessageDeliveryState.Failed);
                }
                lock (_gate) { _readerError = ex; if (_response != null) _response.TrySetException(ex); if (_pushResponse != null) _pushResponse.TrySetException(ex); }
                throw;
            }
        }

        public Task<byte[]> SendTextAsync(byte[] command, bool direct, uint packetId, CancellationToken token)
        {
            return CommandAsync(command, direct ? new byte[] { 6 } : new byte[] { 0 }, token, delegate(byte[] response)
            {
                if (direct)
                {
                    if (response.Length < 10) throw new InvalidDataException("Truncated MeshCore send response.");
                    var ack = ReadUInt32(response, 2);
                    if (ack != 0)
                    {
                        _ackPackets[ack] = packetId;
                        var duration = Math.Min(300000L, Math.Max(1000L, ReadUInt32(response, 6)));
                        _ = ExpireAckAsync(ack, packetId, (int)duration);
                    }
                }
                if (DeliveryChanged != null) DeliveryChanged(packetId, direct ? MessageDeliveryState.QueuedAtDevice : MessageDeliveryState.Sent);
            });
        }

        // Companion firmware has a single pending remote request slot. Hold this
        // gate through the over-the-air response while letting chat/polling run.
        public async Task<byte[]> RemoteRequestAsync(byte[] command, Func<byte[], byte[], bool> matches, CancellationToken token)
        {
            await _remoteRequests.WaitAsync(token).ConfigureAwait(false);
            try
            {
                byte[] sent = null;
                var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_gate) { _pushResponse = completion; _pushMatches = frame => sent != null && matches(sent, frame); }
                var reply = await CommandAsync(command, new byte[] { 6 }, token, frame =>
                {
                    if (frame.Length < 10) throw new InvalidDataException("Truncated MeshCore request response.");
                    sent = frame;
                }).ConfigureAwait(false);
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _closed.Token))
                {
                    deadline.CancelAfter(TimeSpan.FromMilliseconds(Math.Min(300000L, Math.Max(1000L, ReadUInt32(reply, 6)) + 2000L)));
                    using (deadline.Token.Register(() => completion.TrySetCanceled()))
                    {
                        try { return await completion.Task.ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested && !_closed.IsCancellationRequested)
                        { throw new TimeoutException("The remote MeshCore device did not reply. Check reachability, password or telemetry permissions."); }
                    }
                }
            }
            finally { lock (_gate) { _pushResponse = null; _pushMatches = null; } _remoteRequests.Release(); }
        }

        private async Task ExpireAckAsync(uint ack, uint packetId, int milliseconds)
        {
            try
            {
                await Task.Delay(milliseconds, _closed.Token).ConfigureAwait(false);
                uint pending;
                if (_ackPackets.TryRemove(ack, out pending) && DeliveryChanged != null) DeliveryChanged(packetId, MessageDeliveryState.Failed);
            }
            catch (OperationCanceledException) { }
        }

        public async Task DrainMessagesAsync(CancellationToken token)
        {
            // A bounded batch leaves room for UI commands on a busy channel.
            for (var index = 0; index < 64 && PollMessages; index++)
            {
                var response = await CommandAsync(new byte[] { 10 }, new byte[] { 7, 8, 10, 16, 17, 27 }, token).ConfigureAwait(false);
                if (response[0] == 10) break;
            }
        }

        private async Task<byte[]> ReadExactAsync(int count, CancellationToken token)
        {
            var bytes = new byte[count]; var offset = 0;
            while (offset < count)
            {
                var read = await _stream.ReadAsync(bytes, offset, count - offset, token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return bytes;
        }

        internal static uint ReadUInt32(byte[] bytes, int offset)
        {
            if (offset < 0 || bytes.Length - offset < 4) throw new InvalidDataException("Truncated MeshCore integer.");
            return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
        }
    }
}
