using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Client;
using Meshtastic.Protobufs;
public static class BleAdminReadProbe
{
    public static async Task Run(string id, bool verifyUnchanged = false)
    {
        using (var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
        using (var client = new MeshtasticClient { MaximumConnectionAttempts = 1 })
        {
            var count = 0;
            client.ConnectionStateChanged += (s,e) => Console.WriteLine("State: " + e.State + " " + e.Error);
            client.NodeDiscovered += (s,e) => { if (Interlocked.Increment(ref count) % 25 == 0) Console.WriteLine("Nodes received: " + count); };
            client.PacketReceived += (s,e) =>
            {
                var p=e.Packet;
                if (p.Decoded == null) return;
                Console.WriteLine("RX from="+p.From+" to="+p.To+" id="+p.Id+" request="+p.Decoded.RequestId+" reply="+p.Decoded.ReplyId+" port="+p.Decoded.Portnum+" payloadBytes="+p.Decoded.Payload.Length);
                if(p.Decoded.Portnum==PortNum.AdminApp) { var a=AdminMessage.Parser.ParseFrom(p.Decoded.Payload); Console.WriteLine("Admin response: "+a.PayloadVariantCase+" channel="+(a.GetChannelResponse==null ? "-" : a.GetChannelResponse.Index.ToString())); }
                if(p.Decoded.Portnum==PortNum.RoutingApp) Console.WriteLine("Routing: "+Routing.Parser.ParseFrom(p.Decoded.Payload).ErrorReason);
            };
            await client.ConnectBluetoothAsync(id,deadline.Token).ConfigureAwait(false);
            Console.WriteLine("Bluetooth connected; loading configuration");
            await client.RequestFullStateAsync(deadline.Token).ConfigureAwait(false);
            Console.WriteLine("Configuration done; device="+client.Device.NodeName+" firmware="+client.Device.Metadata.FirmwareVersion);
            await client.ActivatePacketStreamingAsync(deadline.Token).ConfigureAwait(false);
            var method=typeof(MeshtasticClient).GetMethod("ReadChannelSlotAsync", BindingFlags.NonPublic|BindingFlags.Instance);
            try { var task=(Task<Channel>)method.Invoke(client,new object[]{2,deadline.Token}); var channel = await task.ConfigureAwait(false); Console.WriteLine("Channel read succeeded"); if (verifyUnchanged) { await client.SaveChannelAsync(channel.Clone(), deadline.Token).ConfigureAwait(false); Console.WriteLine("Unchanged channel write and fresh device readback succeeded"); } }
            catch(Exception ex) { Console.WriteLine("Channel operation failed: "+ex.Message); throw; }
            await Task.Delay(1000,deadline.Token).ConfigureAwait(false);
            await client.DisconnectAsync().ConfigureAwait(false);
        }
    }
}
