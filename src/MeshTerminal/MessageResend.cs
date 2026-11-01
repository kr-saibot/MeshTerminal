using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeshTerminal.Client;
using Meshtastic.Protobufs;

namespace MeshTerminal
{
    internal static class MessageResend
    {
        internal static bool CanRetry(StoredMeshMessage message)
        {
            return message != null && message.Direction == MessageDirection.Outgoing &&
                String.Equals(message.DeliveryStatus, "Failed", StringComparison.OrdinalIgnoreCase);
        }
        internal static async Task<uint> SendAsync(StoredMeshMessage message, MeshtasticClient client, MeshtasticMessageStore store,
            bool failedOnly = true, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (message == null || message.Direction != MessageDirection.Outgoing || (failedOnly && !CanRetry(message)))
                throw new InvalidOperationException("Only your own failed messages can be retried here.");
            if (!client.IsConnected) throw new InvalidOperationException("Connect to a device first.");
            bool channel = message.Kind == StoredMessageKind.Channel;
            if (String.IsNullOrWhiteSpace(message.Text)) throw new InvalidOperationException("This message has no text to resend.");
            if (channel && (!message.ChannelIndex.HasValue || !client.Channels.Any(c => c.Index == message.ChannelIndex.Value && c.Role != Channel.Types.Role.Disabled)))
                throw new InvalidOperationException("The original channel is not configured on this device.");
            if (!channel && (!message.ToNode.HasValue || message.ToNode.Value == UInt32.MaxValue))
                throw new InvalidOperationException("The original recipient is unavailable.");
            if (Encoding.UTF8.GetByteCount(message.Text) > client.GetMaximumTextPayloadBytes(channel, channel ? (uint?)null : message.ToNode))
                throw new InvalidOperationException("This message exceeds the current device's message limit.");
            uint packet = channel ? await client.SendTextToChannelWithIdAsync(message.ChannelIndex.Value, message.Text, cancellationToken: cancellationToken)
                : await client.SendTextWithIdAsync(message.ToNode.Value, message.Text, cancellationToken: cancellationToken);
            store.AddOutgoing(packet, client.Device.MyNode == null ? 0u : client.Device.MyNode.MyNodeNum,
                channel ? UInt32.MaxValue : message.ToNode.Value, message.Text, channel ? message.ChannelIndex : null,
                channel ? message.ChannelName : null, message.Latitude, message.Longitude);
            var delivery = client.GetMessageDelivery(packet);
            if (delivery != null) store.UpdateDeliveryStatus(packet, delivery.State, detail: delivery.Detail);
            return packet;
        }
    }
}
