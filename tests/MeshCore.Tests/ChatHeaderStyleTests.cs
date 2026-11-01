using System;
using System.IO;
using System.Linq;
using MeshTerminal;
using MeshTerminal.Client;
internal static class ChatHeaderStyleTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("Chat header colours: " + message); }
    internal static void Run()
    {
        const string text = "[12:34] You [Failed | Attempt 5/5 | Flood]: body [Failed] You";
        var ranges = ChatHeaderStyles.Find(text, "You", true);
        Check(ranges.Count == 3 && ranges[0].Style == ChatHeaderStyle.Timestamp && text.Substring(ranges[0].Start, ranges[0].Length) == "[12:34]" && text.Substring(ranges[1].Start, ranges[1].Length) == "You" && text.Substring(ranges[2].Start, ranges[2].Length) == "[Failed | Attempt 5/5 | Flood]", "Only sender and real failed status are coloured");
        int coloured = 0;
        for (int offset = 0; offset < text.Length; offset += 7) coloured += ChatHeaderStyles.Clip(ranges, offset, Math.Min(7, text.Length - offset)).Sum(r => r.Length);
        Check(coloured == ranges.Sum(r => r.Length), "Header styles survive wrapping across multiple rows");
        const string name = "DM2TK: Mobile [4] 日本";
        var incoming = ChatHeaderStyles.Find("[12:34] " + name + ": [Failed] in body", name, false);
        Check(incoming.Count == 2 && incoming[1].Length == name.Length, "Incoming Unicode names with punctuation work without colouring body status-like text");
        Check(ChatHeaderStyles.Find(text, "You", false).Count == 2, "Nonfailed status keeps the message colour");
        foreach (var success in new[] { "Sent", "Delivered" })
        {
            var successful = ChatHeaderStyles.Find("[12:34] You [" + success + "]: body [Sent]", "You", false, true);
            Check(successful.Count == 3 && successful[2].Style == ChatHeaderStyle.SuccessfulStatus, "Successful send status has its own colour without affecting body");
            Check(ChatHeaderStyles.Clip(successful, successful[2].Start + 1, 2).Single().Style == ChatHeaderStyle.SuccessfulStatus, "Wrapped success status retains its colour");
        }
        var file = Path.Combine(Path.GetTempPath(), "chat-colours-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(file, "<MeshtasticApplicationSettings><Appearance /></MeshtasticApplicationSettings>");
            var settings = MeshtasticSettingsStore.Load(file);
            Check(settings.Appearance.FailedMessageStatusColor == "Red" && settings.Appearance.SenderNameColor == "Gray" && settings.Appearance.TimestampColor == "Gray" && settings.Appearance.SuccessfulMessageStatusColor == "Green", "Legacy settings receive requested defaults");
            settings.Appearance.FailedMessageStatusColor = "BrightRed"; settings.Appearance.SenderNameColor = "Blue"; settings.Appearance.TimestampColor = "White"; settings.Appearance.SuccessfulMessageStatusColor = "BrightGreen";
            MeshtasticSettingsStore.Save(file, settings); settings = MeshtasticSettingsStore.Load(file);
            Check(settings.Appearance.FailedMessageStatusColor == "BrightRed" && settings.Appearance.SenderNameColor == "Blue" && settings.Appearance.TimestampColor == "White" && settings.Appearance.SuccessfulMessageStatusColor == "BrightGreen", "Both appearance preferences persist");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Chat header colour tests passed: sender/status spans, wrapped headers, Unicode names, body isolation and saved defaults.");
    }
}
