using System;
using System.Collections.Generic;
namespace MeshTerminal
{
    internal enum ChatHeaderStyle { Timestamp, Sender, FailedStatus, SuccessfulStatus }
    internal sealed class ChatHeaderRange
    {
        internal int Start, Length;
        internal ChatHeaderStyle Style;
    }
    internal static class ChatHeaderStyles
    {
        internal static List<ChatHeaderRange> Find(string text, string sender, bool failed, bool successful = false)
        {
            var ranges = new List<ChatHeaderRange>();
            if (String.IsNullOrEmpty(text) || String.IsNullOrEmpty(sender) || text[0] != '[') return ranges;
            int end = text.IndexOf(']'); if (end < 0) return ranges;
            ranges.Add(new ChatHeaderRange { Start = 0, Length = end + 1, Style = ChatHeaderStyle.Timestamp });
            int start = end + 1; while (start < text.Length && text[start] == ' ') start++;
            if (start + sender.Length > text.Length || String.CompareOrdinal(text, start, sender, 0, sender.Length) != 0) return ranges;
            ranges.Add(new ChatHeaderRange { Start = start, Length = sender.Length, Style = ChatHeaderStyle.Sender });
            start += sender.Length;
            if ((!failed && !successful) || start >= text.Length || text[start] != ' ') return ranges;
            start++;
            if (start >= text.Length || text[start] != '[') return ranges;
            end = text.IndexOf(']', start);
            if (end > start) ranges.Add(new ChatHeaderRange { Start = start, Length = end - start + 1, Style = failed ? ChatHeaderStyle.FailedStatus : ChatHeaderStyle.SuccessfulStatus });
            return ranges;
        }
        internal static List<ChatHeaderRange> Clip(List<ChatHeaderRange> ranges, int offset, int length)
        {
            var result = new List<ChatHeaderRange>();
            foreach (var range in ranges)
            {
                int start = Math.Max(offset, range.Start), end = Math.Min(offset + length, range.Start + range.Length);
                if (end > start) result.Add(new ChatHeaderRange { Start = start - offset, Length = end - start, Style = range.Style });
            }
            return result;
        }
    }
}
