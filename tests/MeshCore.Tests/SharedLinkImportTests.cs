using System;
using System.Linq;
using Google.Protobuf;
using Meshtastic.Client;
using Meshtastic.Protobufs;
using ConsoleClient;
internal static class SharedLinkImportTests
{
    public static void Run()
    {
        var settings = new ChannelSettings { Name = "Import", Psk = ByteString.CopyFrom(new byte[32]) };
        var channel = new Channel { Role = Channel.Types.Role.Secondary, Settings = settings };
        Check(SharedLinkImport.MeshtasticChannels(ChannelSharing.CreateUri(MeshProtocol.Meshtastic, channel)).Single().Settings.Equals(settings), "Meshtastic single channel roundtrip");
        var multi = new ChannelSet(); multi.Settings.Add(settings); multi.Settings.Add(new ChannelSettings { Name = "Second", Psk = ByteString.CopyFrom(new byte[] { 1 }) });
        var link = "https://meshtastic.org/d/#" + Convert.ToBase64String(multi.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Check(SharedLinkImport.MeshtasticChannels(link).Count == 2 && MessageContentParser.Find(link).Single().IsMeshtasticChannel, "Meshtastic multi-channel link selection and detection");
        var user = new User { LongName = "Imported", ShortName = "IM", PublicKey = ByteString.CopyFrom(Enumerable.Repeat((byte)77, 32).ToArray()) };
        link = ContactSharing.CreateMeshtasticUri(1234, user);
        var contact = SharedLinkImport.MeshtasticContact(link);
        Check(contact.MeshtasticContact.NodeNum == 1234 && contact.MeshtasticContact.User.Equals(user) && MessageContentParser.Find(link).Single().IsContact, "Meshtastic contact roundtrip and detection");
        var flagged = new SharedContact { NodeNum = 1234, User = user, ManuallyVerified = true, ShouldIgnore = true };
        link = "https://meshtastic.org/v/#" + Convert.ToBase64String(flagged.ToByteArray());
        contact = SharedLinkImport.MeshtasticContact(link);
        Check(!contact.MeshtasticContact.ManuallyVerified && !contact.MeshtasticContact.ShouldIgnore, "Contact links do not grant trust or block contacts");
        link = ContactSharing.CreateMeshCoreUri("Test & Radio", user.PublicKey.ToByteArray(), 3);
        var core = SharedLinkImport.MeshCoreContact(link);
        Check(core.Name == "Test & Radio" && core.Type == 3 && core.PublicKey.Length == 64 && MessageContentParser.Find(link).Single().IsContact, "MeshCore contact query roundtrip and detection");
        var card = new byte[105]; card[0] = 17; user.PublicKey.ToByteArray().CopyTo(card, 2);
        link = "meshcore://" + String.Concat(card.Select(b => b.ToString("x2")));
        Check(SharedLinkImport.MeshCoreContact(link).MeshCoreCard.SequenceEqual(card) && MessageContentParser.Find(link).Single().IsContact, "Native MeshCore contact card parsed and detected");
        foreach (var invalid in new[] { "https://evil.invalid/v/#AAAA", "https://meshtastic.org/v/#!!!", "meshcore://contact/add?name=Bad&public_key=123" })
        {
            try { if (invalid.StartsWith("meshcore:")) SharedLinkImport.MeshCoreContact(invalid); else SharedLinkImport.MeshtasticContact(invalid); throw new Exception("Invalid share accepted"); }
            catch (FormatException) { }
        }
        Console.WriteLine("Share import tests passed: both channel formats, both contact formats, native contact cards, link detection, validation and trust flags.");
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); }
}