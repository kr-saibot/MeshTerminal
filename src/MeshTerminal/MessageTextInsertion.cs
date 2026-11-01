using System;
using System.Text;
using Terminal.Gui;
namespace MeshTerminal
{
    internal static class MessageTextInsertion
    {
        // Terminal.Gui 1.18 InsertText(string) feeds UTF-16 code units into its
        // rune/wrap model. Rebuild valid Unicode text instead, using the public unwrapped-cursor mapping.
        internal static void Insert(TextView input, string addition)
        {
            if (input == null || String.IsNullOrEmpty(addition)) return;
            addition = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(addition.Replace("\r\n", "\n").Replace("\r", "\n")));
            var cursor = input.CursorPosition;
            Action<Point> capture = point => cursor = point;
            input.UnwrappedCursorPosition += capture;
            try
            {
                input.OnUnwrappedCursorPosition();
                var text = input.Text == null ? "" : input.Text.ToString().Replace("\r\n", "\n").Replace("\r", "\n");
                var lines = text.Split('\n');
                int row = Math.Max(0, Math.Min(cursor.Y, lines.Length - 1));
                int column = Math.Max(0, cursor.X), offset = 0, runes = 0;
                while (offset < lines[row].Length && runes < column)
                {
                    offset += Char.IsHighSurrogate(lines[row][offset]) && offset + 1 < lines[row].Length && Char.IsLowSurrogate(lines[row][offset + 1]) ? 2 : 1;
                    runes++;
                }
                int index = offset;
                for (int i = 0; i < row; i++) index += lines[i].Length + 1;
                var parts = addition.Split('\n');
                int lastRunes = 0;
                var last = parts[parts.Length - 1];
                for (int i = 0; i < last.Length; i++, lastRunes++) if (Char.IsHighSurrogate(last[i]) && i + 1 < last.Length && Char.IsLowSurrogate(last[i + 1])) i++;
                input.Text = text.Insert(index, addition);
                var target = new Point(parts.Length == 1 ? runes + lastRunes : lastRunes, row + parts.Length - 1);
                var visual = target;
                if (input.WordWrap)
                {
                    for (int visualRow = 0; visualRow < input.Lines; visualRow++)
                    {
                        input.CursorPosition = new Point(0, visualRow);
                        input.OnUnwrappedCursorPosition();
                        if (cursor.Y > target.Y || (cursor.Y == target.Y && cursor.X > target.X)) break;
                        if (cursor.Y == target.Y) visual = new Point(target.X - cursor.X, visualRow);
                    }
                }
                input.CursorPosition = visual;
            }
            finally { input.UnwrappedCursorPosition -= capture; }
            input.SetNeedsDisplay();
        }
    }
}
