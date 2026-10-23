using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Client;
internal static class MeshCoreContactTimeoutTests
{
    internal static async Task RunAsync()
    {
        await CheckAsync(true);
        await CheckAsync(false);
        Console.WriteLine("MeshCore contact timeout tests passed: active BLE transfers continue; unrelated frames do not mask a stalled transfer.");
    }
    private static async Task CheckAsync(bool progress)
    {
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var written = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var stream = new BlePacketStream(MeshProtocol.MeshCore, (bytes, token) => { written.TrySetResult(true); return Task.CompletedTask; }))
            {
                var session = new MeshCoreSession(stream, () => stream.Dispose(), TimeSpan.FromMilliseconds(400), bytes => { });
                var reader = session.ReadAsync(cancellation.Token);
                var command = session.CommandAsync(new byte[] { 4 }, new byte[] { 4 }, cancellation.Token);
                await written.Task;
                var producer = Task.Run(async () =>
                {
                    stream.Receive(new byte[] { 2, 10, 0, 0, 0 });
                    for (var index = 0; index < 12; index++)
                    {
                        await Task.Delay(80, cancellation.Token);
                        var packet = new byte[148]; packet[0] = progress ? (byte)3 : (byte)0x80;
                        stream.Receive(packet);
                    }
                    if (progress) stream.Receive(new byte[] { 4, 0, 0, 0, 0 });
                });
                try
                {
                    if (progress) { var result = await command; if (result[0] != 4) throw new Exception("Contact transfer did not finish"); }
                    else { try { await command; throw new Exception("Unrelated notifications extended the contact timeout"); } catch (TimeoutException) { } }
                    await producer;
                }
                finally
                {
                    cancellation.Cancel(); stream.Dispose();
                    try { await reader; } catch (OperationCanceledException) { } catch (EndOfStreamException) { }
                }
            }
        }
    }
}
