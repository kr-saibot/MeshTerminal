using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace MeshTerminal.Client
{
    internal sealed partial class MeshCoreSession
    {
        private sealed class RetryingSend
        {
            internal readonly object Gate = new object();
            internal readonly TaskCompletionSource<bool> Finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly HashSet<uint> Acks = new HashSet<uint>();
            internal uint Packet;
            internal byte[] Command, Key;
            internal int Attempt, Maximum;
            internal bool Flood, Complete;
        }
        private readonly ConcurrentDictionary<uint, RetryingSend> _retryAcks = new ConcurrentDictionary<uint, RetryingSend>();
        private readonly ConcurrentDictionary<uint, RetryingSend> _retrySends = new ConcurrentDictionary<uint, RetryingSend>();
        internal Action<uint, int, int, bool> DeliveryAttempt;
        private void FinishRetry(RetryingSend pending, MessageDeliveryState state)
        {
            lock (pending.Gate)
            {
                if (pending.Complete) return;
                pending.Complete = true;
                pending.Finished.TrySetResult(state == MessageDeliveryState.Delivered);
                RetryingSend ignored;
                foreach (var ack in pending.Acks) _retryAcks.TryRemove(ack, out ignored);
                _retrySends.TryRemove(pending.Packet, out ignored);
                DeliveryChanged?.Invoke(pending.Packet, state);
            }
        }
        private void ConfirmRetryAck(uint ack)
        {
            RetryingSend pending;
            if (_retryAcks.TryGetValue(ack, out pending)) FinishRetry(pending, MessageDeliveryState.Delivered);
        }
        private void StopRetrySends()
        {
            foreach (var pending in _retrySends.Values.ToArray()) FinishRetry(pending, MessageDeliveryState.Failed);
        }
        private async Task<byte[]> SendRetryAttemptAsync(RetryingSend pending, CancellationToken token)
        {
            var command = (byte[])pending.Command.Clone(); command[2] = (byte)(pending.Attempt - 1);
            return await CommandAsync(command, new byte[] { 6 }, token, response =>
            {
                if (response.Length < 10 || ReadUInt32(response, 2) == 0) throw new InvalidDataException("MeshCore did not provide a delivery acknowledgement identifier.");
                lock (pending.Gate)
                {
                    if (pending.Complete) return;
                    pending.Flood = response[1] == 1;
                    if (pending.Attempt == 1) pending.Maximum = pending.Flood ? 3 : 5;
                    var ack = ReadUInt32(response, 2);
                    pending.Acks.Add(ack); _retryAcks[ack] = pending;
                    DeliveryAttempt?.Invoke(pending.Packet, pending.Attempt, pending.Maximum, pending.Flood);
                    DeliveryChanged?.Invoke(pending.Packet, MessageDeliveryState.QueuedAtDevice);
                }
            }, shouldSend: () => { lock (pending.Gate) return !pending.Complete; }).ConfigureAwait(false);
        }
        internal async Task<byte[]> SendTextWithRetriesAsync(byte[] command, byte[] key, uint packet, CancellationToken token)
        {
            if (command == null || command.Length < 14 || command[0] != 2 || command[1] != 0 || key == null || key.Length != 32) throw new ArgumentException("Invalid MeshCore direct message.");
            if (command.Length - 13 > 158) throw new ArgumentException("MeshCore direct messages are limited to 158 UTF-8 bytes to allow all retry attempts.");
            var pending = new RetryingSend { Packet = packet, Command = (byte[])command.Clone(), Key = (byte[])key.Clone(), Attempt = 1 };
            _retrySends[packet] = pending;
            try
            {
                var reply = await SendRetryAttemptAsync(pending, token).ConfigureAwait(false);
                _ = ContinueRetriesAsync(pending, reply, token);
                return reply;
            }
            catch { FinishRetry(pending, MessageDeliveryState.Failed); throw; }
        }
        private async Task ContinueRetriesAsync(RetryingSend pending, byte[] reply, CancellationToken token)
        {
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token, _closed.Token))
            using (stop.Token.Register(() => FinishRetry(pending, MessageDeliveryState.Failed)))
            {
                try
                {
                    while (reply != null)
                    {
                        int delay = (int)Math.Min(300000L, Math.Max(1000L, ReadUInt32(reply, 6)));
                        // Only an actual ACK from any attempt completes the logical message.
                        if (await Task.WhenAny(pending.Finished.Task, Task.Delay(delay, stop.Token)).ConfigureAwait(false) == pending.Finished.Task) return;
                        stop.Token.ThrowIfCancellationRequested();
                        lock (pending.Gate)
                        {
                            if (pending.Complete) return;
                            if (pending.Attempt >= pending.Maximum) { FinishRetry(pending, MessageDeliveryState.Failed); return; }
                            pending.Attempt++;
                        }
                        if (pending.Maximum == 5 && pending.Attempt == 5)
                        {
                            await CommandAsync(new byte[] { 13 }.Concat(pending.Key).ToArray(), new byte[] { 0 }, stop.Token,
                                shouldSend: () => { lock (pending.Gate) return !pending.Complete; }).ConfigureAwait(false);
                        }
                        reply = await SendRetryAttemptAsync(pending, stop.Token).ConfigureAwait(false);
                    }
                }
                catch { FinishRetry(pending, MessageDeliveryState.Failed); }
            }
        }
    }
}
