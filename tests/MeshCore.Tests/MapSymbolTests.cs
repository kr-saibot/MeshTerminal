using System;
using System.Collections.Generic;
using System.Linq;
using MeshTerminal;
using MeshTerminal.Host;

internal static class MapSymbolTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("Map symbols: " + message); }
    internal static void Run()
    {
        // ConPTY uses BS for a one-column cursor move. Replacing it with a space
        // advances the cursor instead and makes Left/Backspace visibly jump right.
        var cursorOutput = "\b\u001b[3;7H\u001b[?25h\r\n\t".ToCharArray();
        Span<char> cursorSpan = cursorOutput.AsSpan();
        TerminalOutputSanitizer.Sanitize(ref cursorSpan);
        Check(new string(cursorOutput) == "\b\u001b[3;7H\u001b[?25h\r\n\t", "Host preserves ConPTY cursor movement and terminal controls");
        var replacements = new Dictionary<string, string> { { "♥", "[:heart:]" }, { "♟", "[:pawn:]" }, { "😀", "[:smile:]" } };
        Func<string, bool> emoji = element => element.Any(c => c > 255);
        foreach (var symbol in MapSymbolCatalog.Symbols)
        {
            Check(MapSymbolCatalog.ConvertMessage(symbol, replacements, emoji) == MapSymbolCatalog.Display(symbol), "Every picker symbol survives emoji conversion, including configured replacements");
            var chars = MapSymbolCatalog.Display(symbol).ToCharArray();
            Span<char> span = chars.AsSpan(); TerminalOutputSanitizer.Sanitize(ref span);
            Check(new string(chars) == MapSymbolCatalog.Display(symbol), "Host preserves symbol and text presentation selector");
            Check(MapSymbolCatalog.ConvertMessage(symbol + "\uFE0F", replacements, emoji) == MapSymbolCatalog.Display(symbol), "Emoji presentation selector is normalized to text for picker symbols");
        }
        Check(MapSymbolCatalog.Display("♟", true) == "♟\uFE0E", "Black pawn explicitly requests monochrome text presentation");
        foreach (var symbol in MapSymbolCatalog.Symbols)
            Check(MapSymbolCatalog.Display(symbol + "\uFE0E", false).Length == 1, "Unix symbol rendering must not emit zero-width presentation selectors");
        Check(MapSymbolCatalog.ConvertMessage("a😀b", replacements, emoji) == "a[:smile:]b", "Regular emojis retain existing replacement behavior");
        Check(MapSymbolCatalog.ConvertMessage("😀", new Dictionary<string, string>(), emoji) == "[:emoji:]", "Unknown non-picker emojis retain existing fallback");
        Check(MapSymbolCatalog.ConvertMessage("///CHESS:test:♟e7-e5", replacements, emoji).EndsWith(MapSymbolCatalog.Display("♟") + "e7-e5"), "Chess notation remains text instead of an unknown emoji token");
        Console.WriteLine("Map symbol tests passed: all picker symbols, chat conversion, emoji variants, monochrome pawn, host allowlist and regular emoji behavior.");
    }
}
