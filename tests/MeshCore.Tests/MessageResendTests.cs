using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeshTerminal;
using MeshTerminal.Client;
using Meshtastic.Protobufs;

internal static partial class Program
{
    private static async Task TestMessageResendAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), "message-resend-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var radio = new FakeRadio())
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
            using (var client = new MeshtasticClient { Protocol = MeshProtocol.MeshCore, MaximumConnectionAttempts = 1 })
            using (var store = MeshtasticMessageStore.CreateSqlite(path))
            {
                store.Initialize();
                await client.ConnectTcpAsync("127.0.0.1", radio.Port, deadline.Token);
                await client.RequestFullStateAsync(deadline.Token);
                uint recipient = client.Nodes.Single(n => n.LongName == "Alice").Number;
                store.AddOutgoing(900001, 99, recipient, "Retry Grüße ♟", null, latitude: 48.1, longitude: 12.2);
                store.UpdateDeliveryStatus(900001, MessageDeliveryState.Failed, "timeout");
                var original = store.GetDirectMessages(recipient).Single();
                Check(MessageResend.CanRetry(original), "Failed outgoing message can be retried");
                uint packet = await MessageResend.SendAsync(original, client, store, cancellationToken: deadline.Token);
                var attempts = store.GetDirectMessages(recipient);
                Check(attempts.Count == 2 && attempts[0].Id == original.Id && attempts[0].DeliveryStatus == "Failed" && attempts[0].DeliveryError == "timeout", "Original failed attempt and error remain unchanged");
                Check(attempts[1].Id != original.Id && attempts[1].PacketId == packet && attempts[1].ToNode == recipient && attempts[1].Text == original.Text && attempts[1].Latitude == 48.1 && attempts[1].Longitude == 12.2, "New attempt preserves text, original recipient and location");
                Check(radio.DirectText == original.Text && radio.DirectPrefix.SequenceEqual(FakeRadio.AliceKey.Take(6)), "Retry transmits to original device contact");
                Check(!MessageResend.CanRetry(attempts[1]), "Already-submitted/delivered messages do not offer failed-message retry");
                store.AddOutgoing(900002, 99, UInt32.MaxValue, "Retry channel", 0, "Public");
                store.UpdateDeliveryStatus(900002, MessageDeliveryState.Failed);
                var channel = store.GetChannelMessages(0).Single();
                await MessageResend.SendAsync(channel, client, store, cancellationToken: deadline.Token);
                var channelAttempts = store.GetChannelMessages(0);
                Check(radio.ChannelText == channel.Text && channelAttempts.Count == 2 && channelAttempts[1].Kind == StoredMessageKind.Channel && channelAttempts[1].ChannelIndex == 0 && channelAttempts[1].ChannelName == "Public", "Channel retry retains original route and channel name");
                store.AddOutgoing(900003, 99, UInt32.MaxValue, "reject", 0); store.UpdateDeliveryStatus(900003, MessageDeliveryState.Failed);
                var rejected = store.GetChannelMessages(0).Last(); int count = store.GetChannelMessages().Count;
                try { await MessageResend.SendAsync(rejected, client, store, cancellationToken: deadline.Token); throw new Exception("Rejected retry was accepted"); } catch (IOException) { }
                Check(store.GetChannelMessages().Count == count && store.GetChannelMessages(0).Last().DeliveryStatus == "Failed", "Device rejection does not create a false successful retry");
                store.AddIncoming(new MeshMessage(new MeshPacket { From = recipient, To = 99, RxTime = 1700000000 }, "incoming") { Protocol = MeshProtocol.MeshCore });
                var incoming = store.GetDirectMessages(recipient).Last();
                Check(!MessageResend.CanRetry(incoming), "Incoming messages cannot be retried as your own");
                try { await MessageResend.SendAsync(incoming, client, store, cancellationToken: deadline.Token); throw new Exception("Incoming message was retried"); } catch (InvalidOperationException) { }
                string chess = ChessProtocol.Encode(ChessPosition.Initial());
                store.AddOutgoing(900004, 99, recipient, chess, null);
                var invitation = store.GetDirectMessages(recipient).Last();
                await MessageResend.SendAsync(invitation, client, store, false, deadline.Token);
                Check(radio.DirectText == chess && store.GetDirectMessages(recipient).Last().Text == chess, "Chess resend preserves exact flags and payload even without Failed status");
                store.AddOutgoing(900005, 99, UInt32.MaxValue, "missing channel", 39); store.UpdateDeliveryStatus(900005, MessageDeliveryState.Failed);
                try { await MessageResend.SendAsync(store.GetChannelMessages(39).Single(), client, store, cancellationToken: deadline.Token); throw new Exception("Unavailable channel was accepted"); } catch (InvalidOperationException) { }
                await client.DisconnectAsync();
                try { await MessageResend.SendAsync(original, client, store, cancellationToken: deadline.Token); throw new Exception("Disconnected retry was accepted"); } catch (InvalidOperationException) { }
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine("Message resend tests passed: direct/channel routes, unchanged payload, independent attempts/status, rejection, unavailable channel, disconnected state and exact chess retransmission.");
    }
}
