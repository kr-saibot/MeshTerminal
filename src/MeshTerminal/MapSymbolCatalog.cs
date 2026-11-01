using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MeshTerminal
{
    // Shared by the picker, chat rendering and the host's Unicode allowlist.
    internal static class MapSymbolCatalog
    {
        internal static readonly string[] Symbols = new[]
        {
            "␀", "☺", "☻", "♥", "♦", "♣", "♠", "•", "◘", "○", "◙", "♂", "♀", "♪", "♫", "☼",
            "►", "◄", "↕", "‼", "¶", "§", "▬", "↨", "↑", "↓", "→", "←", "∟", "↔", "▲", "▼", "\u2302"
        }.Concat(Range(0x2295, 0x22A1)).Concat(Range(0x2654, 0x265F)).Concat(Range(0x29E8, 0x29F3)).ToArray();
        private static readonly HashSet<char> Characters = new HashSet<char>(Symbols.Select(s => s[0]));
        // Picker symbols for which Unicode defines emoji/text presentation variants.
        private const string TextVariants = "↔↕‼♂♀♟♠♣♥♦";
        private static IEnumerable<string> Range(int first, int last) { return Enumerable.Range(first, last - first + 1).Select(Char.ConvertFromUtf32); }
        internal static bool IsSymbol(string element)
        {
            return !String.IsNullOrEmpty(element) && Characters.Contains(element[0]) &&
                (element.Length == 1 || (element.Length == 2 && (element[1] == '\uFE0E' || element[1] == '\uFE0F')));
        }
        internal static string Display(string symbol)
        {
            return Display(symbol, Environment.OSVersion.Platform == PlatformID.Win32NT);
        }
        internal static string Display(string symbol, bool windowsTextPresentation)
        {
            if (!IsSymbol(symbol)) return symbol;
            // CursesDriver's zero-width-character path corrupts positioning for VS15.
            return symbol[0].ToString() + (windowsTextPresentation && TextVariants.IndexOf(symbol[0]) >= 0 ? "\uFE0E" : "");
        }
        internal static string ConvertMessage(string text, IDictionary<string, string> replacements, Func<string, bool> containsEmoji)
        {
            var result = new StringBuilder();
            var elements = StringInfo.GetTextElementEnumerator(text ?? "");
            while (elements.MoveNext())
            {
                var element = (string)elements.Current;
                string replacement;
                if (IsSymbol(element)) result.Append(Display(element));
                else if (replacements.TryGetValue(element, out replacement)) result.Append(replacement);
                else if (containsEmoji(element)) result.Append("[:emoji:]");
                else result.Append(element);
            }
            return result.ToString();
        }
    }
}
