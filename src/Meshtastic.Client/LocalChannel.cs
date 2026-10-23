using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Google.Protobuf;
using Meshtastic.Protobufs;

namespace Meshtastic.Client
{
    public sealed class LocalChannel
    {
        public string Id { get; set; }
        public MeshProtocol Protocol { get; set; }
        public string Data { get; set; }

        public Channel Read()
        {
            var channel = Channel.Parser.ParseFrom(Convert.FromBase64String(Data));
            channel.Index = 0;
            channel.Role = Channel.Types.Role.Secondary;
            if (channel.Settings == null) channel.Settings = new ChannelSettings();
            return channel;
        }

        public static LocalChannel Create(MeshProtocol protocol, Channel channel, string id = null)
        {
            Validate(protocol, channel);
            var copy = channel.Clone(); copy.Index = 0; copy.Role = Channel.Types.Role.Secondary;
            return new LocalChannel { Id = id ?? Guid.NewGuid().ToString("N"), Protocol = protocol, Data = Convert.ToBase64String(copy.ToByteArray()) };
        }

        public static void Validate(MeshProtocol protocol, Channel channel)
        {
            if (channel == null || channel.Settings == null) throw new ArgumentException("Channel settings are missing.");
            var name = channel.Settings.Name;
            var core = protocol == MeshProtocol.MeshCore;
            if ((core && String.IsNullOrWhiteSpace(name)) || name.IndexOf('\0') >= 0 || Encoding.UTF8.GetByteCount(name) > (core ? 31 : 11))
                throw new ArgumentException(core ? "MeshCore channel names need 1-31 UTF-8 bytes." : "Meshtastic channel names must fit in 11 UTF-8 bytes.");
            var length = channel.Settings.Psk.Length;
            if (core ? length != 16 : length != 0 && length != 1 && length != 16 && length != 32)
                throw new ArgumentException(core ? "MeshCore channels require a 16-byte key." : "Meshtastic keys require 0, 1, 16 or 32 bytes.");
            if (channel.Settings.ModuleSettings != null && channel.Settings.ModuleSettings.PositionPrecision > 32)
                throw new ArgumentException("Position precision must be between 0 and 32.");
        }

        public static bool SameName(Channel a, Channel b)
        {
            return String.Equals(a.Settings == null ? "" : a.Settings.Name, b.Settings == null ? "" : b.Settings.Name, StringComparison.OrdinalIgnoreCase);
        }

        public static void MigrateDrafts(MeshtasticApplicationSettings settings)
        {
            if (settings.LocalChannels == null) settings.LocalChannels = new List<LocalChannel>();
            if (settings.ChannelDrafts == null) return;
            foreach (var draft in settings.ChannelDrafts.ToList())
            {
                try
                {
                    var channel = Channel.Parser.ParseFrom(Convert.FromBase64String(draft.Data));
                    channel.Index = 0; channel.Role = Channel.Types.Role.Secondary;
                    var entry = new LocalChannel { Id = Guid.NewGuid().ToString("N"), Protocol = draft.Protocol, Data = Convert.ToBase64String(channel.ToByteArray()) };
                    if (!settings.LocalChannels.Any(existing => existing.Protocol == entry.Protocol && existing.Data == entry.Data)) settings.LocalChannels.Add(entry);
                    settings.ChannelDrafts.Remove(draft);
                }
                catch (FormatException) { }
                catch (InvalidProtocolBufferException) { }
                catch (ArgumentException) { }
            }
        }
    }
}
