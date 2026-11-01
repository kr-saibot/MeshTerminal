using System;
using System.Collections.Generic;

namespace MeshTerminal
{
    internal enum ChessRenderMode { Symbols, Letters, Blocks, BlocksSpaced }
    internal static class ChessBlockPieces
    {
        internal const int Width = 7, Height = 5;
        private static readonly Dictionary<char, string[]> Pixels = new Dictionary<char, string[]>
        {
            ['K'] = new[] { "0001000", "0011100", "0001000", "0011100", "0011100", "0011100", "0001000", "0011100", "0111110", "1111111" },
            ['Q'] = new[] { "1001001", "1101011", "1111111", "0111110", "0111110", "0011100", "0011100", "0011100", "0111110", "1111111" },
            ['R'] = new[] { "1101011", "1101011", "1111111", "1111111", "0111110", "0110110", "0110110", "0111110", "1111111", "1111111" },
            ['B'] = new[] { "0001000", "0011100", "0111010", "0110110", "0011100", "0001000", "0011100", "0011100", "0111110", "1111111" },
            ['N'] = new[] { "0010010", "0011110", "0111110", "1111010", "1100110", "0001110", "0011100", "0111100", "0111110", "1111111" },
            ['P'] = new[] { "0000000", "0011100", "0111110", "0111110", "0011100", "0001000", "0011100", "0011100", "0111110", "1111111" }
        };
        internal static string[] Rows(char type)
        {
            var pixels = Pixels[Char.ToUpperInvariant(type)];
            var rows = new string[Height];
            for (int y = 0; y < Height; y++)
            {
                var row = new char[Width];
                for (int x = 0; x < Width; x++) row[x] = " ▄▀█"[(pixels[y * 2][x] - '0') * 2 + pixels[y * 2 + 1][x] - '0'];
                rows[y] = new string(row);
            }
            return rows;
        }
        internal static ChessRenderMode ReadMode(string value, bool legacyLetters)
        {
            ChessRenderMode mode;
            return Enum.TryParse(value, true, out mode) && Enum.IsDefined(typeof(ChessRenderMode), mode)
                ? mode : legacyLetters ? ChessRenderMode.Letters : ChessRenderMode.Symbols;
        }
    }
}
