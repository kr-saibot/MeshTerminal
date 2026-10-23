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
        Check(MessageContentParser.Find(null).Count == 0, "Empty message");
        Console.WriteLine("Message content tests passed: web URLs, punctuation, coordinates and invalid ranges.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}