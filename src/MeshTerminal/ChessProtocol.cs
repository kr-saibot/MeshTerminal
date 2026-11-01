using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MeshTerminal
{
    // IDs 1..8: white Ra1 Nb1 Bc1 Qd1 Ke1 Bf1 Ng1 Rh1; 9..16: white a2..h2.
    // IDs 17..24: black Ra8 Nb8 Bc8 Qd8 Ke8 Bf8 Ng8 Rh8; 25..32: black a7..h7.
    internal sealed class ChessPosition
    {
        internal int[] Squares = new int[32];
        internal char[] Types = "RNBQKBNRPPPPPPPPRNBQKBNRPPPPPPPP".ToCharArray();
        internal bool[] Moved = new bool[32];
        internal bool WhiteToMove = true, NewGame;
        internal bool HasEncodedPromotions;
        internal int EnPassantSquare, EnPassantPawn = -1;
        internal string Notation = "";
        internal ChessPosition Copy() { return new ChessPosition { Squares = (int[])Squares.Clone(), Types = (char[])Types.Clone(), Moved = (bool[])Moved.Clone(), WhiteToMove = WhiteToMove, NewGame = NewGame, HasEncodedPromotions = HasEncodedPromotions, Notation = Notation, EnPassantSquare = EnPassantSquare, EnPassantPawn = EnPassantPawn }; }
        internal static ChessPosition Initial()
        {
            var p = new ChessPosition { NewGame = true };
            for (int i = 0; i < 8; i++) { p.Squares[i] = i + 1; p.Squares[i + 8] = i + 9; p.Squares[i + 16] = i + 57; p.Squares[i + 24] = i + 49; }
            return p;
        }
        internal int PieceAt(int square) { return Array.IndexOf(Squares, square); }
        internal string Symbol(int id)
        {
            int index = "KQRBNP".IndexOf(Types[id]);
            return Char.ConvertFromUtf32(0x2654 + (id < 16 ? 0 : 6) + index);
        }
        internal static string SquareName(int square) { return ((char)('a' + (square - 1) % 8)).ToString() + ((square - 1) / 8 + 1).ToString(CultureInfo.InvariantCulture); }
        internal HashSet<int> Changed(ChessPosition previous)
        {
            var result = new HashSet<int>();
            if (previous != null) for (int i = 0; i < 32; i++) if (Squares[i] != previous.Squares[i]) { if (Squares[i] > 0) result.Add(Squares[i]); if (previous.Squares[i] > 0) result.Add(previous.Squares[i]); }
            return result;
        }
        internal bool InCheck(bool white)
        {
            int king = Squares[white ? 4 : 20];
            return king == 0 || Attacked(king, !white);
        }
        private bool Attacked(int square, bool byWhite)
        {
            for (int i = byWhite ? 0 : 16; i < (byWhite ? 16 : 32); i++)
                if (Squares[i] != 0 && CanReach(i, square, true)) return true;
            return false;
        }
        private bool CanReach(int id, int target, bool attack)
        {
            int source = Squares[id], dx = (target - 1) % 8 - (source - 1) % 8, dy = (target - 1) / 8 - (source - 1) / 8;
            int ax = Math.Abs(dx), ay = Math.Abs(dy), occupant = PieceAt(target);
            if (source == target || target < 1 || target > 64) return false;
            switch (Types[id])
            {
                case 'P':
                    int direction = id < 16 ? 1 : -1;
                    if (attack) return ax == 1 && dy == direction;
                    if (ax == 1 && dy == direction) return occupant >= 0 || (target == EnPassantSquare && EnPassantPawn >= 0 && Squares[EnPassantPawn] > 0);
                    if (dx != 0 || occupant >= 0) return false;
                    if (dy == direction) return true;
                    return dy == 2 * direction && (source - 1) / 8 == (id < 16 ? 1 : 6) && PieceAt(source + 8 * direction) < 0;
                case 'N': return ax * ay == 2;
                case 'K': return Math.Max(ax, ay) == 1;
                case 'B': if (ax != ay) return false; break;
                case 'R': if (dx != 0 && dy != 0) return false; break;
                case 'Q': if (ax != ay && dx != 0 && dy != 0) return false; break;
                default: return false;
            }
            int step = Math.Sign(dx) + 8 * Math.Sign(dy);
            for (int square = source + step; square != target; square += step) if (PieceAt(square) >= 0) return false;
            return true;
        }
        internal bool TryMove(int source, int target, char promotion, out ChessPosition next, out string error)
        {
            next = null; error = "Illegal move.";
            if (source < 1 || source > 64 || target < 1 || target > 64 || source == target) return false;
            int id = PieceAt(source), captured = PieceAt(target);
            if (id < 0 || (id < 16) != WhiteToMove) { error = "Select a piece of the side whose turn it is."; return false; }
            if (captured >= 0 && ((captured < 16) == (id < 16) || Types[captured] == 'K')) return false;
            bool castle = Types[id] == 'K' && Math.Abs(target - source) == 2 && source == (id < 16 ? 5 : 61);
            int rook = -1;
            if (castle)
            {
                rook = id < 16 ? (target > source ? 7 : 0) : (target > source ? 23 : 16);
                if (Moved[id] || Moved[rook] || Squares[rook] != (id < 16 ? (target > source ? 8 : 1) : (target > source ? 64 : 57)) || InCheck(WhiteToMove)) return false;
                int step = Math.Sign(target - source);
                for (int s = source + step; s != Squares[rook]; s += step) if (PieceAt(s) >= 0) return false;
                var crossing = Copy(); crossing.Squares[id] = source + step;
                if (crossing.InCheck(WhiteToMove)) return false;
            }
            else if (!CanReach(id, target, false)) return false;
            var p = Copy(); p.EnPassantSquare = 0; p.EnPassantPawn = -1;
            if (Types[id] == 'P' && target == EnPassantSquare && captured < 0) { captured = EnPassantPawn; if (captured >= 0) p.Squares[captured] = 0; }
            if (captured >= 0) p.Squares[captured] = 0;
            p.Squares[id] = target; p.Moved[id] = true;
            if (castle) { p.Squares[rook] = target + (target > source ? -1 : 1); p.Moved[rook] = true; }
            if (Types[id] == 'P' && Math.Abs(target - source) == 16) { p.EnPassantSquare = (source + target) / 2; p.EnPassantPawn = id; }
            bool promote = Types[id] == 'P' && (target <= 8 || target >= 57);
            if (promote) { if ("QRBN".IndexOf(promotion) < 0) return false; p.Types[id] = promotion; }
            if (p.InCheck(WhiteToMove)) { error = "This move would leave your king in check."; return false; }
            p.Notation = castle ? (target > source ? "O-O" : "O-O-O") : Symbol(id) + SquareName(source) + (captured >= 0 ? "x" : "-") + SquareName(target) + (promote ? "=" + p.Symbol(id) : "");
            p.WhiteToMove = !WhiteToMove;
            if (p.InCheck(p.WhiteToMove)) p.Notation += "+";
            next = p; error = null; return true;
        }
        internal bool HasLegalMove()
        {
            for (int id = WhiteToMove ? 0 : 16; id < (WhiteToMove ? 16 : 32); id++) if (Squares[id] != 0)
                for (int target = 1; target <= 64; target++) { ChessPosition next; string error; if (TryMove(Squares[id], target, 'Q', out next, out error)) return true; }
            return false;
        }
    }
    internal static class ChessProtocol
    {
        internal const string Prefix = "///CHESS:";
        internal static string NotationAsText(string notation)
        {
            var result = new StringBuilder();
            var names = new[] { "king", "queen", "rook", "bishop", "knight", "pawn" };
            bool piece = false;
            foreach (char value in notation ?? "")
            {
                if (piece && (value == '\uFE0E' || value == '\uFE0F')) continue;
                piece = value >= '\u2654' && value <= '\u265F';
                if (piece)
                {
                    int index = value - '\u2654';
                    result.Append(index < 6 ? "White " : "Black ").Append(names[index % 6]);
                    result.Append(' ');
                }
                else result.Append(value);
            }
            return result.ToString().TrimEnd();
        }
        private static bool IsPawnId(int id) { return (id >= 8 && id < 16) || (id >= 24 && id < 32); }
        // Bit 7 marks a promoted pawn; one following hex digit gives Q=1, R=2, B=3, N=4.
        internal static string Encode(ChessPosition position)
        {
            var result = new StringBuilder(Prefix);
            for (int id = 0; id < 32; id++)
            {
                int type = IsPawnId(id) ? "QRBN".IndexOf(position.Types[id]) : -1;
                result.Append((position.Squares[id] | (type >= 0 ? 0x80 : 0)).ToString("X2", CultureInfo.InvariantCulture));
                if (type >= 0) result.Append((type + 1).ToString("X", CultureInfo.InvariantCulture));
            }
            return result.Append(position.NewGame ? "01" : "00").Append(position.WhiteToMove ? "00" : "01").Append(':').Append(NotationAsText(position.Notation)).ToString();
        }
        internal static bool TryParse(string text, out ChessPosition position)
        {
            position = null;
            if (text == null || !text.StartsWith(Prefix, StringComparison.Ordinal) || text.Length < Prefix.Length + 69 || text.Length > 256) return false;
            var p = new ChessPosition();
            int offset = Prefix.Length;
            for (int id = 0; id < 32; id++)
            {
                byte value;
                if (offset + 2 > text.Length || !Byte.TryParse(text.Substring(offset, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)) return false;
                offset += 2;
                int square = value & 0x7F;
                if (square > 64) return false;
                p.Squares[id] = square;
                if ((value & 0x80) != 0)
                {
                    if (!IsPawnId(id) || offset >= text.Length || text[offset] < '1' || text[offset] > '4') return false;
                    p.Types[id] = "QRBN"[text[offset++] - '1'];
                    p.HasEncodedPromotions = true;
                }
            }
            if (offset + 5 > text.Length || text[offset + 4] != ':') return false;
            string flags = text.Substring(offset, 4);
            if (flags != "0000" && flags != "0001" && flags != "0100" && flags != "0101") return false;
            if (p.Squares.Where(square => square > 0).Distinct().Count() != p.Squares.Count(square => square > 0) || p.Squares[4] == 0 || p.Squares[20] == 0) return false;
            p.NewGame = flags[1] == '1'; p.WhiteToMove = flags[3] == '0'; p.Notation = text.Substring(offset + 5);
            if (p.Notation.Any(Char.IsControl)) return false;
            position = p; return true;
        }
        internal static void ApplyHistory(ChessPosition current, ChessPosition previous)
        {
            if (previous == null) previous = ChessPosition.Initial();
            if (!current.HasEncodedPromotions) current.Types = (char[])previous.Types.Clone();
            current.Moved = (bool[])previous.Moved.Clone();
            current.EnPassantSquare = 0; current.EnPassantPawn = -1;
            for (int i = 0; i < 32; i++) if (current.Squares[i] != previous.Squares[i])
            {
                current.Moved[i] = true;
                if (previous.Types[i] == 'P' && current.Squares[i] > 0)
                {
                    if (Math.Abs(current.Squares[i] - previous.Squares[i]) == 16) { current.EnPassantSquare = (current.Squares[i] + previous.Squares[i]) / 2; current.EnPassantPawn = i; }
                    if (!current.HasEncodedPromotions && (current.Squares[i] <= 8 || current.Squares[i] >= 57))
                    {
                        int at = current.Notation.IndexOf('=');
                        char symbol = at >= 0 && at + 1 < current.Notation.Length ? current.Notation[at + 1] : '\0';
                        int type = "♕♖♗♘♛♜♝♞".IndexOf(symbol);
                        current.Types[i] = type < 0 ? 'Q' : "QRBNQRBN"[type];
                    }
                }
            }
        }
    }
}
