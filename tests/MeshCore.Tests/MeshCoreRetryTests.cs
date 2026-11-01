using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeshTerminal.Client;
internal static class MeshCoreRetryTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("MeshCore retry: " + message); }
    internal static async Task RunAsync()
    {
        await Task.WhenAll(CheckSend(false, 5, false, false), CheckSend(true, 0, false, false),
            CheckSend(false, 0, false, false), CheckSend(false, 2, false, false),
            CheckSend(false, 2, true, false), CheckSend(false, 0, false, true));
        var file = Path.Combine(Path.GetTempPath(), "retry-details-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize(); store.AddOutgoing(77, 1, 2, "single message", null);
                store.UpdateDeliveryStatus(77, MessageDeliveryState.QueuedAtDevice, detail: "Attempt 1/5 | Direct");
                store.UpdateDeliveryStatus(77, MessageDeliveryState.QueuedAtDevice, detail: "Attempt 5/5 | Flood");
                store.UpdateDeliveryStatus(77, MessageDeliveryState.Delivered, detail: "Attempt 5/5 | Flood");
                store.UpdateDeliveryStatus(77, MessageDeliveryState.QueuedAtDevice, detail: "Attempt 1/5 | Direct");
            }
            using (var store = MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize(); var rows = store.GetDirectMessages(2);
                Check(rows.Count == 1 && rows[0].DeliveryStatus == "Delivered" && rows[0].DeliveryDetail == "Attempt 5/5 | Flood", "One message and retry metadata survive restart; stale progress cannot overwrite delivery");
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("MeshCore retry tests passed: direct retries, fifth flood attempt, three initial floods, ACK stop/late correlation, cancellation and persistent single-message progress.");
    }
    private static async Task CheckSend(bool initialFlood, int ackAttempt, bool lateFirstAck, bool cancel)
    {
        using (var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
        using (var sendCancellation = new CancellationTokenSource())
        {
            BlePacketStream stream = null;
            var commands = new List<byte[]>(); var progress = new List<string>();
            var finished = new TaskCompletionSource<MessageDeliveryState>(TaskCreationOptions.RunContinuationsAsynchronously);
            int sent = 0, resets = 0; bool flood = initialFlood;
            Func<uint, byte[]> ack = value => new byte[] { 0x82 }.Concat(BitConverter.GetBytes(value)).Concat(BitConverter.GetBytes(123u)).ToArray();
            stream = new BlePacketStream(MeshProtocol.MeshCore, (command, token) =>
            {
                lock (commands) commands.Add((byte[])command.Clone());
                if (command[0] == 13)
                {
                    Interlocked.Increment(ref resets); flood = true;
                    stream.Receive(new byte[] { 0 });
                }
                else if (command[0] == 2)
                {
                    var attempt = Interlocked.Increment(ref sent);
                    stream.Receive(new byte[] { 6, flood ? (byte)1 : (byte)0 }.Concat(BitConverter.GetBytes((uint)(100 + attempt))).Concat(BitConverter.GetBytes(20u)).ToArray());
                    // Unrelated delivery reports must never complete this message.
                    stream.Receive(ack(9000));
                    if (attempt == ackAttempt) stream.Receive(ack(lateFirstAck ? 101u : (uint)(100 + attempt)));
                }
                else throw new Exception("Unexpected retry command");
                return Task.CompletedTask;
            });
            using (stream)
            {
                var session = new MeshCoreSession(stream, () => stream.Dispose(), TimeSpan.FromSeconds(2), frame => { });
                session.DeliveryChanged = (packet, state) => { if (state == MessageDeliveryState.Delivered || state == MessageDeliveryState.Failed) finished.TrySetResult(state); };
                session.DeliveryAttempt = (packet, attempt, maximum, usedFlood) => { lock (progress) progress.Add(attempt + "/" + maximum + "/" + usedFlood); };
                var reader = session.ReadAsync(lifetime.Token);
                try
                {
                    var key = Enumerable.Repeat((byte)42, 32).ToArray();
                    var command = new byte[] { 2, 0, 0, 7, 0, 0, 0 }.Concat(key.Take(6)).Concat(Enumerable.Repeat((byte)120, ackAttempt == 5 ? 158 : 4)).ToArray();
                    await session.SendTextWithRetriesAsync(command, key, 77, sendCancellation.Token);
                    if (cancel) sendCancellation.Cancel();
                    var ended = await Task.WhenAny(finished.Task, Task.Delay(9000, lifetime.Token));
                    Check(ended == finished.Task, "Retry sequence has a bounded completion");
                    var expected = ackAttempt > 0 ? MessageDeliveryState.Delivered : MessageDeliveryState.Failed;
                    Check(await finished.Task == expected, "Final status follows matching ACK or exhausted/cancelled attempts");
                    await Task.Delay(1200, lifetime.Token);
                    int expectedAttempts = cancel ? 1 : ackAttempt > 0 ? ackAttempt : initialFlood ? 3 : 5;
                    Check(sent == expectedAttempts, "No extra sends after delivery, cancellation or exhausted retry budget");
                    Check(resets == (!initialFlood && expectedAttempts == 5 ? 1 : 0), "Path resets only before the final direct-route fallback");
                    byte[][] snapshots; lock (commands) snapshots = commands.ToArray();
                    var texts = snapshots.Where(c => c[0] == 2).ToArray();
                    for (int i = 0; i < texts.Length; i++)
                    {
                        Check(texts[i][2] == i && texts[i].Skip(3).SequenceEqual(command.Skip(3)), "Retries retain timestamp, recipient and text while incrementing the attempt byte");
                    }
                    if (resets == 1)
                    {
                        Check(snapshots[4][0] == 13 && snapshots[4].Skip(1).SequenceEqual(key) && snapshots[5][0] == 2, "Reset uses full destination key immediately before fifth send");
                        lock (progress) Check(progress.Last() == "5/5/True", "Progress shows fifth attempt via flood");
                    }
                    if (initialFlood) { lock (progress) Check(progress.First() == "1/3/True", "Initially unknown path has three flood attempts"); }
                }
                finally
                {
                    lifetime.Cancel(); stream.Dispose();
                    try { await reader; } catch (OperationCanceledException) { } catch (EndOfStreamException) { } catch (ObjectDisposedException) { }
                }
            }
        }
    }
}
