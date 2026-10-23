using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MeshTerminal.Client
{
    /// <summary>Converts complete emoji sequences and their delimited names in one pass.</summary>
    public sealed class EmojiNameFormatter
    {
        private readonly Dictionary<string, string> _names;
        private readonly Dictionary<string, string> _emojis;
        private readonly Regex _namePattern;
        private readonly Regex _emojiPattern;
        public EmojiNameFormatter(IEnumerable<KeyValuePair<string, string>> names)
        {
            _names = names.Where(n => !String.IsNullOrEmpty(n.Key) && !String.IsNullOrEmpty(n.Value))
                .ToDictionary(n => n.Key, n => n.Value, StringComparer.Ordinal);
            _emojis = _names.GroupBy(n => n.Value).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
            _emojiPattern = Pattern(_names.Keys);
            _namePattern = Pattern(_emojis.Keys);
        }
        private static Regex Pattern(IEnumerable<string> keys)
        {
            var values = keys.OrderByDescending(k => k.Length).Select(Regex.Escape).ToArray();
            return values.Length == 0 ? null : new Regex(String.Join("|", values), RegexOptions.CultureInvariant);
        }
        public string ToNames(string text) { return Convert(text, _emojiPattern, _names); }
        public string ToEmojis(string text) { return Convert(text, _namePattern, _emojis); }
        private static string Convert(string text, Regex pattern, Dictionary<string, string> replacements)
        {
            if (String.IsNullOrEmpty(text) || pattern == null) return text;
            return pattern.Replace(text, match => replacements[match.Value]);
        }
    }
}