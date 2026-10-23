using System;
using System.IO;
using System.Linq;
using Google.Protobuf;
using MeshTerminal.Client;
using Meshtastic.Protobufs;

internal static class ChannelDraftTests
{
    public static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        try
        {
            var settings = new MeshtasticApplicationSettings();
            var channel = new Channel { Index = 6, Role = Channel.Types.Role.Primary, Settings = new ChannelSettings { Name = "Private", Psk = ByteString.CopyFrom(new byte[16]), UplinkEnabled = true, ModuleSettings = new ModuleSettings { PositionPrecision = 15, IsMuted = true } } };
            var source = channel.Clone();
            var local = LocalChannel.Create(MeshProtocol.MeshCore, channel);
            settings.LocalChannels.Add(local);
            Check(channel.Equals(source), "Copying to PC does not modify device settings");
            var transfer = local.Read(); transfer.Index = 3; transfer.Role = Channel.Types.Role.Secondary;
            Check(local.Read().Index == 0 && local.Read().Settings.Equals(channel.Settings), "Local channel retains all settings independently of target slot");
            settings.LocalChannels.Add(LocalChannel.Create(MeshProtocol.Meshtastic, channel));
            MeshtasticSettingsStore.Save(path, settings);
            settings = MeshtasticSettingsStore.Load(path);
            Check(settings.LocalChannels.Count == 2 && settings.LocalChannels.Single(e => e.Protocol == MeshProtocol.MeshCore).Read().Settings.Equals(channel.Settings), "Local channels survive restart separately for both protocols");
            var duplicate = channel.Clone(); duplicate.Settings.Name = "pRIVATE";
            Check(LocalChannel.SameName(channel, duplicate), "Overwrite detection ignores letter case");
            duplicate.Settings.Psk = ByteString.CopyFrom(new byte[32]);
            try { LocalChannel.Create(MeshProtocol.MeshCore, duplicate); throw new Exception("Invalid MeshCore key accepted"); } catch (ArgumentException) { }
            var legacy = source.Clone(); legacy.Role = Channel.Types.Role.Disabled;
            settings.ChannelDrafts.Add(new ChannelDraft { DeviceId = "old-device", Protocol = MeshProtocol.MeshCore, Slot = 6, Data = Convert.ToBase64String(legacy.ToByteArray()) });
            LocalChannel.MigrateDrafts(settings);
            Check(settings.ChannelDrafts.Count == 0 && settings.LocalChannels.Count == 2, "Legacy device-specific drafts migrate without duplicate local settings");
            legacy.Settings.Name = "Recovered";
            settings.ChannelDrafts.Add(new ChannelDraft { DeviceId = "other-device", Protocol = MeshProtocol.MeshCore, Slot = 1, Data = Convert.ToBase64String(legacy.ToByteArray()) });
            LocalChannel.MigrateDrafts(settings);
            Check(settings.LocalChannels.Count == 3 && settings.LocalChannels.Any(e => e.Read().Settings.Name == "Recovered"), "Drafts from another device become independent local channels");
            LocalChannel.MigrateDrafts(settings);
            Check(settings.LocalChannels.Count == 3, "Migration is idempotent");
            settings.LocalChannels.RemoveAll(e => e.Id == local.Id);
            Check(settings.LocalChannels.Count == 2 && channel.Equals(source), "Deleting local channel retains device channel and other local entries");
            Console.WriteLine("Channel library tests passed: independent copies, restart, protocol isolation, name conflicts, validation, draft migration and deletion.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
