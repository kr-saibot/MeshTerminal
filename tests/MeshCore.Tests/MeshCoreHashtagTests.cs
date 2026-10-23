using System;
using System.Linq;
using Google.Protobuf;
using MeshTerminal.Client;
using Meshtastic.Protobufs;

internal static class MeshCoreHashtagTests
{
    public static void Run()
    {
        var key = MeshCoreHashtag.DeriveKey("#test");
        Check(BitConverter.ToString(key).Replace("-", "").ToLowerInvariant() == "9cd8fcf22a47333b591d96a2b848b73f", "Official MeshCore #test test vector");
        Check(!MeshCoreHashtag.DeriveKey("#Test").SequenceEqual(key), "Exact letter case is preserved");
        Check(MeshCoreHashtag.DeriveKey("#münchen").Length == 16, "UTF-8 hashtag supported");
        Check(MeshCoreHashtag.Matches("#test", key) && !MeshCoreHashtag.Matches("#test", new byte[16]), "Automatic keys distinguished from existing manually configured keys");
        Check(!MeshCoreHashtag.Matches("private", key), "Private channels are not converted to hashtags");
        foreach (var invalid in new[] { "", "#", "test", "#a\0b", "#" + new string('ä', 16) })
        {
            try { MeshCoreHashtag.DeriveKey(invalid); throw new Exception("Invalid hashtag accepted"); } catch (ArgumentException) { }
        }
        var channel = new Channel { Role = Channel.Types.Role.Secondary, Settings = new ChannelSettings { Name = "#test", Psk = ByteString.CopyFrom(key) } };
        var local = LocalChannel.Create(MeshProtocol.MeshCore, channel).Read();
        Check(local.Settings.Equals(channel.Settings), "Hashtag key preserved in local library");
        var shared = ChannelSharing.ParseMeshCoreUri(ChannelSharing.CreateUri(MeshProtocol.MeshCore, local));
        Check(shared.Settings.Equals(channel.Settings), "Hashtag key survives QR/link sharing");
        Console.WriteLine("MeshCore hashtag tests passed: official vector, case, UTF-8 limits, manual keys, library and QR/link round trip.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
