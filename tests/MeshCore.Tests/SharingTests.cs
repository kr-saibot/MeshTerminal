using System;
using System.IO;
using System.Linq;
using System.Drawing;
using Google.Protobuf;
using Meshtastic.Client;
using Meshtastic.Protobufs;
using ConsoleClient;
using ZXing;

internal static class SharingTests
{
    public static void Run()
    {
        var core = new Channel { Index = 3, Role = Channel.Types.Role.Secondary, Settings = new ChannelSettings { Name = "#Köln &?", Psk = ByteString.CopyFrom(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray()) } };
        var coreUri = ChannelSharing.CreateUri(MeshProtocol.MeshCore, core);
        Check(coreUri == "meshcore://channel/add?name=%23K%C3%B6ln%20%26%3F&secret=000102030405060708090a0b0c0d0e0f", "MeshCore official URI encoding");
        var imported = ChannelSharing.ParseMeshCoreUri(coreUri);
        Check(imported.Settings.Name == core.Settings.Name && imported.Settings.Psk.Equals(core.Settings.Psk), "MeshCore channel URI import roundtrip preserves encoded names and key");
        Check(ChannelSharing.ParseMeshCoreUri("meshcore://channel/add?secret=1df7a0b39568c99c1d872e4333e9bbbe&name=JMBR").Settings.Name == "JMBR", "Channel import accepts user example and arbitrary parameter order");
        foreach (var bad in new[] {
            "meshcore://channel/add?name=Test&secret=xyz",
            "meshcore://channel/add?name=Test&secret=0011",
            "meshcore://channel/add?name=&secret=000102030405060708090a0b0c0d0e0f",
            "meshcore://channel/add?name=Test&name=Other&secret=000102030405060708090a0b0c0d0e0f",
            "meshcore://wrong/add?name=Test&secret=000102030405060708090a0b0c0d0e0f",
            "meshcore://channel/add?name=%00&secret=000102030405060708090a0b0c0d0e0f" })
        {
            try { ChannelSharing.ParseMeshCoreUri(bad); throw new Exception("Invalid channel link accepted"); }
            catch (System.FormatException) { }
        }
        var mesh = new Channel { Index = 5, Role = Channel.Types.Role.Secondary, Settings = new ChannelSettings { Name = "QR Test", Psk = ByteString.CopyFrom(new byte[32]), UplinkEnabled = true, ModuleSettings = new ModuleSettings { PositionPrecision = 16 } } };
        var meshUri = ChannelSharing.CreateUri(MeshProtocol.Meshtastic, mesh);
        Check(meshUri.StartsWith("https://meshtastic.org/e/?add=true#"), "Meshtastic add-only URI");
        var payload = meshUri.Split('#')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        var set = ChannelSet.Parser.ParseFrom(Convert.FromBase64String(payload));
        Check(set.Settings.Count == 1 && set.Settings[0].Equals(mesh.Settings) && set.LoraConfig == null, "One channel, complete settings, no LoRa overwrite");
        CheckQr(coreUri, "meshcore"); CheckQr(meshUri, "meshtastic");
        mesh.Role = Channel.Types.Role.Disabled;
        try { ChannelSharing.CreateUri(MeshProtocol.Meshtastic, mesh); throw new Exception("Disabled QR accepted"); } catch (ArgumentException) { }
        core.Settings.Psk = ByteString.CopyFrom(new byte[32]);
        try { ChannelSharing.CreateUri(MeshProtocol.MeshCore, core); throw new Exception("Invalid MeshCore QR key accepted"); } catch (ArgumentException) { }
        var contactKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var contactName = "Tobias & Café + / ?";
        var contactUri = ContactSharing.CreateMeshCoreUri(contactName, contactKey);
        var parameters = System.Web.HttpUtility.ParseQueryString(new Uri(contactUri).Query);
        Check(parameters["name"] == contactName && parameters["public_key"] == String.Concat(contactKey.Select(value => value.ToString("x2"))) && parameters["type"] == "1", "MeshCore contact QR schema and escaped name");
        CheckQr(contactUri, "MeshCore contact");
        var user = new User { Id = "!12345678", LongName = contactName, ShortName = "TC", PublicKey = ByteString.CopyFrom(contactKey) };
        var ownContactUri = ContactSharing.CreateMeshtasticUri(0x12345678, user);
        var encodedContact = ownContactUri.Split('#')[1].Replace('-', '+').Replace('_', '/');
        encodedContact = encodedContact.PadRight((encodedContact.Length + 3) / 4 * 4, '=');
        var sharedContact = SharedContact.Parser.ParseFrom(Convert.FromBase64String(encodedContact));
        Check(ownContactUri.StartsWith("https://meshtastic.org/v/#") && sharedContact.NodeNum == 0x12345678 &&
            sharedContact.User.Equals(user) && !sharedContact.ShouldIgnore && !sharedContact.ManuallyVerified, "Official Meshtastic contact QR payload");
        CheckQr(ownContactUri, "Meshtastic contact");
        try { ContactSharing.CreateMeshCoreUri("Invalid", new byte[31]); throw new Exception("Invalid contact key accepted"); } catch (ArgumentException) { }
        Console.WriteLine("QR tests passed: official share payloads, PNG decoding and terminal half-block decoding for both protocols.");
    }

    private static void CheckQr(string uri, string name)
    {
        using (var data = ChannelQrCode.Create(uri))
        {
            var rows = ChannelQrCode.HalfBlockRows(data.ModuleMatrix);
            var width = data.ModuleMatrix.Count * 6;
            var rgb = new byte[width * width * 3];
            for (var y = 0; y < width; y++)
                for (var x = 0; x < width; x++)
                {
                    var row = rows[(y / 6) / 2]; var c = row[x / 6];
                    var black = (y / 6) % 2 == 0 ? c == '▀' || c == '█' : c == '▄' || c == '█';
                    var at = (y * width + x) * 3; rgb[at] = rgb[at + 1] = rgb[at + 2] = black ? (byte)0 : (byte)255;
                }
            var decoded = new BarcodeReaderGeneric().Decode(new RGBLuminanceSource(rgb, width, width, RGBLuminanceSource.BitmapFormat.RGB24));
            Check(decoded != null && decoded.Text == uri, "Terminal QR decodes to exact URI");
            var png = ChannelQrCode.Png(data);
            using (var stream = new MemoryStream(png))
            using (var bitmap = new Bitmap(stream))
            {
                var result = new BarcodeReader().Decode(bitmap);
                Check(result != null && result.Text == uri, "Exported PNG decodes to exact URI");
            }
            // Synthetic fixtures only; no device secrets are written by these tests.
            File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + "-qr-test.png"), png);
        }
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); }
}
