using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Meshtastic.Protobufs;

namespace MeshTerminal.Client
{
    public static class ChannelSharing
    {
        public static Channel ParseMeshCoreUri(string text)
        {
            Uri uri;
            if (!Uri.TryCreate((text ?? "").Trim(), UriKind.Absolute, out uri) ||
                !String.Equals(uri.Scheme, "meshcore", StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(uri.Host, "channel", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath != "/add" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Fragment.Length != 0)
                throw new FormatException("Expected a meshcore://channel/add link.");
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in uri.Query.TrimStart('?').Split('&'))
            {
                var pair = part.Split(new[] { '=' }, 2);
                if (pair.Length != 2) throw new FormatException("Invalid channel link parameters.");
                var key = Uri.UnescapeDataString(pair[0].Replace("+", " "));
                if (values.ContainsKey(key)) throw new FormatException("Duplicate channel link parameter.");
                values.Add(key, Uri.UnescapeDataString(pair[1].Replace("+", " ")));
            }
            string name, secret;
            if (!values.TryGetValue("name", out name) || String.IsNullOrWhiteSpace(name) || name.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(name) > 31)
                throw new FormatException("Channel name must contain 1-31 UTF-8 bytes and no NUL.");
            if (!values.TryGetValue("secret", out secret) || !Regex.IsMatch(secret, @"\A[0-9a-fA-F]{32}\z"))
                throw new FormatException("Channel secret must contain exactly 32 hexadecimal characters (16 bytes).");
            var bytes = Enumerable.Range(0, 16).Select(i => Convert.ToByte(secret.Substring(i * 2, 2), 16)).ToArray();
            return new Channel { Role = Channel.Types.Role.Secondary, Settings = new ChannelSettings { Name = name, Psk = ByteString.CopyFrom(bytes) } };
        }
        public static string CreateUri(MeshProtocol protocol, Channel channel)
        {
            if (channel == null || channel.Settings == null || channel.Role == Channel.Types.Role.Disabled)
                throw new ArgumentException("Enable the channel before sharing it.");
            var settings = channel.Settings;
            if (settings.Name.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(settings.Name) > (protocol == MeshProtocol.MeshCore ? 31 : 11))
                throw new ArgumentException("Invalid channel name.");
            if (protocol == MeshProtocol.MeshCore)
            {
                if (String.IsNullOrWhiteSpace(settings.Name) || settings.Psk.Length != 16) throw new ArgumentException("MeshCore requires a name and a 16-byte key.");
                return "meshcore://channel/add?name=" + Uri.EscapeDataString(settings.Name) + "&secret=" + BitConverter.ToString(settings.Psk.ToByteArray()).Replace("-", "").ToLowerInvariant();
            }
            if (protocol != MeshProtocol.Meshtastic) throw new ArgumentOutOfRangeException("protocol");
            if (settings.Psk.Length != 0 && settings.Psk.Length != 1 && settings.Psk.Length != 16 && settings.Psk.Length != 32) throw new ArgumentException("Invalid Meshtastic key length.");
            // Add-only shares intentionally omit LoRa config and contain only this channel.
            var set = new ChannelSet(); set.Settings.Add(settings.Clone());
            return "https://meshtastic.org/e/?add=true#" + Convert.ToBase64String(set.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }

    public sealed partial class MeshtasticClient
    {
        public async Task SendAdvertAsync(bool flood = true, CancellationToken token = default(CancellationToken))
        {
            if (!IsConnected || Device.MyNode == null) throw new InvalidOperationException("Connect and synchronize the device first.");
            if (Protocol == MeshProtocol.MeshCore)
            {
                await _meshCore.CommandAsync(new byte[] { 7, (byte)(flood ? 1 : 0) }, new byte[] { 0 }, token).ConfigureAwait(false);
                return;
            }
            var local = Device.LocalNode;
            if (local == null || local.Value.User == null) throw new InvalidOperationException("Local node information has not been received yet.");
            var primary = System.Linq.Enumerable.FirstOrDefault(Channels, c => c.Role == Channel.Types.Role.Primary);
            if (primary == null) throw new InvalidOperationException("No primary channel is configured.");
            // Meshtastic's equivalent is a NodeInfo broadcast using the device's current identity.
            await SendAsync(new ToRadio { Packet = new MeshPacket { Id = NextPacketId(), To = BroadcastNode, Channel = (uint)primary.Index,
                Decoded = new Data { Portnum = PortNum.NodeinfoApp, Payload = local.Value.User.ToByteString() } } }, token).ConfigureAwait(false);
        }
    }
}
