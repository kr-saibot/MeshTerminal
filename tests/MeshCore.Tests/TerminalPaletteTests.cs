using System;
using System.IO;
using System.Xml.Serialization;
using MeshTerminal;
using MeshTerminal.Client;

internal static class TerminalPaletteTests
{
    internal static void Run()
    {
        Check(TerminalPalette.Rgb.Length == 16 && TerminalPalette.Rgb[8] == 0x808080 && TerminalPalette.Rgb[15] == 0xFFFFFF, "Host palette retains DarkGray and White");
        Check(UnixTerminalColors.Normalize("screen", name => name == "screen-256color") == "screen-256color", "screen eight-colour definition is upgraded");
        Check(UnixTerminalColors.Normalize("tmux", name => name == "tmux-256color") == "tmux-256color", "Native tmux 256-colour definition is preferred");
        Check(UnixTerminalColors.Normalize("tmux", name => name == "screen-256color") == "screen-256color", "tmux uses compatible installed fallback");
        Check(UnixTerminalColors.Normalize("screen", name => false) == "screen" && UnixTerminalColors.Normalize("dumb", name => true) == "dumb", "Missing or unrelated terminal definitions are never fabricated");
        var serializer = new XmlSerializer(typeof(MeshtasticApplicationSettings));
        var settings = (MeshtasticApplicationSettings)serializer.Deserialize(new StringReader("<MeshtasticApplicationSettings><Appearance><UsePuTTYPalette>true</UsePuTTYPalette><TextColor>White</TextColor></Appearance></MeshtasticApplicationSettings>"));
        Check(settings.Appearance.TextColor == "White", "Old PuTTY option is safely ignored when loading settings");
        var xml = new StringWriter(); serializer.Serialize(xml, settings);
        Check(!xml.ToString().Contains("UsePuTTYPalette"), "Removed option is not saved again");
        Console.WriteLine("Terminal colour tests passed: unchanged host palette, installed 256-colour definitions and removed PuTTY-option migration.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception("Terminal colours: " + message); }
}
