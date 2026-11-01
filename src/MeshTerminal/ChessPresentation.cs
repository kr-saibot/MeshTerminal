using System;
using System.Text;

namespace MeshTerminal
{
    internal static class ChessPresentation
    {
        internal static string Piece(ChessPosition position, int id, bool letters)
        {
            return Piece(position, id, letters, Environment.OSVersion.Platform == PlatformID.Win32NT);
        }
        internal static string Piece(ChessPosition position, int id, bool letters, bool windowsTextPresentation)
        {
            if (letters) return (id < 16 ? position.Types[id] : Char.ToLowerInvariant(position.Types[id])).ToString();
            var symbol = position.Symbol(id);
            // U+265F has emoji presentation and may occupy two cells. On Unix draw
            // the text pawn outline with the black side's foreground instead.
            if (!windowsTextPresentation && symbol == "♟") symbol = "♙";
            return MapSymbolCatalog.Display(symbol, windowsTextPresentation);
        }
        internal static string Notation(string text, bool letters)
        {
            if (!letters || String.IsNullOrEmpty(text)) return text ?? "";
            var result = new StringBuilder();
            bool piece = false;
            foreach (char value in text)
            {
                if (piece && (value == '\uFE0E' || value == '\uFE0F')) continue;
                piece = value >= '\u2654' && value <= '\u265F';
                if (piece)
                {
                    int index = value - '\u2654';
                    char letter = "KQRBNP"[index % 6];
                    result.Append(index < 6 ? letter : Char.ToLowerInvariant(letter));
                }
                else result.Append(value);
            }
            return result.ToString();
        }
    }
}
