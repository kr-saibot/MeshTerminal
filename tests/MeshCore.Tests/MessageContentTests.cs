using System;
using System.Linq;
using MeshTerminal;
internal static class MessageContentTests
{
    internal static void Run()
    {
        var items = MessageContentParser.Find("Link (https://example.org/path?q=a&b=2). GPS 48.12345, 9.12345 and -33,12345; 151,12345");
        Check(items.Count == 3 && items[0].Url == "https://example.org/path?q=a&b=2", "URLs preserve query and trim surrounding punctuation");
        Check(items[1].Latitude == 48.12345 && items[1].Longitude == 9.12345 && items[2].Latitude == -33.12345 && items[2].Longitude == 151.12345, "Decimal coordinates, German commas and southern hemisphere");
        Check(MessageContentParser.Find("www.example.org").Single().Url == "https://www.example.org", "www URLs");
        Check(MessageContentParser.Find("91.123, 8.123; 48.123, 181.123; file:///tmp/test javascript:alert(1)").Count == 0, "Invalid coordinates and non-web schemes ignored");
        Check(MessageContentParser.Find("https://example.org/a_(b)").Single().Url.EndsWith("(b)"), "Balanced URL parentheses retained");
        var channelLink = "meshcore://channel/add?name=JMBR&secret=1df7a0b39568c99c1d872e4333e9bbbe";
        var channelTarget = MessageContentParser.Find("Join (\"" + channelLink + "\").").Single();
        Check(channelTarget.IsMeshCoreChannel && channelTarget.Url == channelLink, "MeshCore channel links are recognized with surrounding punctuation");
        Check(MessageContentParser.Find("meshcore://unknown/path").Count == 0, "Unrecognized MeshCore URI actions are not exposed");
        var nodes = new[]
        {
            new MeshTerminal.Client.StoredMeshNode { NodeNumber = 1, LongName = "DM2TK Mobil" },
            new MeshTerminal.Client.StoredMeshNode { NodeNumber = 2, LongName = "DM2TK Mobil 4" },
            new MeshTerminal.Client.StoredMeshNode { NodeNumber = 3, LongName = "DM2TK Mobil 4" },
            new MeshTerminal.Client.StoredMeshNode { NodeNumber = 4, LongName = "Base+(North)" }
        };
        var mention = MessageContentParser.Find("Hello dm2tk mobil 4, see Base+(North)!", nodes);
        Check(mention.Count == 2 && mention[0].NodeNumbers.SequenceEqual(new uint[] { 2, 3 }) && mention[1].NodeNumbers.Single() == 4, "Node names: longest match, case, duplicate selection and regex characters");
        Check(MessageContentParser.Find("XDM2TK Mobil 4suffix", nodes).All(t => t.NodeNumbers == null || !t.NodeNumbers.Contains(2)), "No partial node name matches");
        Check(MessageContentParser.Find("https://example.org/DM2TK Mobil 4", nodes).All(t => t.NodeNumbers == null), "No node matches overlapping a link");
        var north = new MessageContentTarget { Latitude = 1, Longitude = 0 };
        Check(north.PositionRelation(0, 0).Contains("111.2 km") && north.PositionRelation(0, 0).Contains("0° N"), "GPS distance and bearing are from own position to target");
        Check(north.PositionRelation(null, 0).Contains("unavailable"), "Missing own GPS is explicitly unavailable");
        Check(north.PositionRelation(1, 0).Contains("same position"), "Same point has no artificial direction");
        Check(MessageContentParser.Find(null, nodes).Count == 0, "Empty message");
        Console.WriteLine("Message content tests passed: web URLs, punctuation, coordinates and invalid ranges.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}