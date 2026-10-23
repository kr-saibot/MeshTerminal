using System;
using Meshtastic.Client;
using Meshtastic.Protobufs;
internal static class TelegramSenderTests
{
    public static void Run()
    {
        var message = new MeshMessage(new MeshPacket { From = 706 }, "Hello") { SenderName = "DM2TK Mobile" };
        Check(MeshtasticTelegramGatewayManager.FormatTelegramMessage(message, new MeshNode[0]) == "[DM2TK Mobile]\nHello", "MeshCore message sender without contact");
        var node = new MeshNode(new NodeInfo { Num = 706, User = new User { Id = "!000002c2", LongName = "Long name", ShortName = "LN" } });
        Check(MeshtasticTelegramGatewayManager.FormatTelegramMessage(message, new[] { node }) == "[DM2TK Mobile]\nHello", "Message name takes precedence");
        message.SenderName = null;
        Check(MeshtasticTelegramGatewayManager.FormatTelegramMessage(message, new[] { node }) == "[Long name]\nHello", "Meshtastic contact name without ID");
        node.Value.User.LongName = "";
        Check(MeshtasticTelegramGatewayManager.FormatTelegramMessage(message, new[] { node }) == "[LN]\nHello", "Short name fallback");
        Check(MeshtasticTelegramGatewayManager.FormatTelegramMessage(message, new MeshNode[0]) == "[!000002c2]\nHello", "Unknown sender ID fallback");
        Console.WriteLine("Telegram sender tests passed: message name, contact names and unknown sender fallback.");
    }
    private static void Check(bool value, string description) { if (!value) throw new Exception(description); }
}
