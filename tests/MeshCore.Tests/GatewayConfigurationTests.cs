using System;
using System.IO;
using System.Linq;
using MeshTerminal.Client;

internal static class GatewayConfigurationTests
{
    public static void Run()
    {
        Check(ChannelConfiguration.Count(MeshProtocol.Meshtastic, false, 40) == 8, "Meshtastic keeps eight channels");
        Check(ChannelConfiguration.Count(MeshProtocol.MeshCore, false, 8) == 40, "Offline MeshCore configuration exposes more than legacy eight slots");
        Check(ChannelConfiguration.Count(MeshProtocol.MeshCore, true, 64) == 64, "Device-reported extended capacity is respected");
        Check(ChannelConfiguration.Count(MeshProtocol.MeshCore, true, 8) == 8, "Known eight-slot MeshCore device is respected");
        Check(ChannelConfiguration.Count(MeshProtocol.MeshCore, false, 8, 99) == 100, "Previously saved higher channel remains selectable");
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        try
        {
            var settings = new MeshtasticApplicationSettings();
            Check(settings.TelegramGateways.Count == 0, "New configuration has no automatically generated gateways");
            settings.TelegramGateways.Add(new TelegramGatewaySettings { ChannelIndex = 0 });
            settings.TelegramGateways.Add(new TelegramGatewaySettings { Protocol = MeshProtocol.MeshCore, ChannelIndex = 39, Name = "Room 39", BotToken = "keep-secret", ChatId = -1234, Enabled = true, ForwardTelegramToMeshtastic = false });
            settings.TelegramGateways.Add(new TelegramGatewaySettings { Protocol = MeshProtocol.Meshtastic, ChannelIndex = 7, Name = "Disabled server" });
            MeshtasticSettingsStore.Save(file, settings);
            settings = MeshtasticSettingsStore.Load(file);
            Check(settings.TelegramGateways.Count == 2 && settings.TelegramGateways.Any(g => g.Name == "Disabled server" && !g.Enabled), "Legacy empty slots removed; explicitly created disabled servers retained");
            var saved = settings.TelegramGateways.Single(g => g.Protocol == MeshProtocol.MeshCore);
            Check(saved.ChannelIndex == 39 && saved.Name == "Room 39" && saved.BotToken == "keep-secret" && saved.ChatId == -1234 && !saved.ForwardTelegramToMeshtastic, "Gateway settings and extended MeshCore channel survive reload");
            settings.TelegramGateways.Remove(saved); MeshtasticSettingsStore.Save(file, settings);
            settings = MeshtasticSettingsStore.Load(file);
            Check(settings.TelegramGateways.All(g => g.Protocol != MeshProtocol.MeshCore), "Deleted server does not return after restart");
            settings.TelegramGateways.Clear(); MeshtasticSettingsStore.Save(file, settings);
            Check(MeshtasticSettingsStore.Load(file).TelegramGateways.Count == 0, "Deleting all servers persists without slot regeneration");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Gateway configuration tests passed: protocol capacities, named servers, legacy settings, extended channels and persistent deletion.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
