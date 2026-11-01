using System;
using System.Collections.Generic;
using System.Linq;

namespace MeshTerminal
{
    internal sealed class ChessHistory
    {
        internal ChessPosition Current = ChessPosition.Initial(), Previous;
        internal bool HasGame, OwnWhite;
        internal int HalfMoves;
        internal static ChessHistory Read(IEnumerable<Tuple<string, bool>> messages)
        {
            var game = new List<Tuple<ChessPosition, bool>>();
            string lastFrame = null;
            foreach (var message in messages.Reverse())
            {
                ChessPosition position;
                if (!ChessProtocol.TryParse(message.Item1, out position)) continue;
                string frame = (message.Item2 ? "out:" : "in:") + ChessProtocol.Encode(position);
                if (frame == lastFrame) continue;
                lastFrame = frame;
                game.Add(Tuple.Create(position, message.Item2));
                if (position.NewGame) break;
            }
            var result = new ChessHistory();
            if (game.Count == 0 || !game[game.Count - 1].Item1.NewGame) return result;
            game.Reverse(); result.HasGame = true;
            // A white creator sends the first white move; a black creator sends the initial board.
            bool creatorWhite = !game[0].Item1.WhiteToMove;
            result.OwnWhite = game[0].Item2 ? creatorWhite : !creatorWhite;
            var previous = ChessPosition.Initial();
            foreach (var entry in game)
            {
                var position = entry.Item1;
                ChessProtocol.ApplyHistory(position, previous);
                if (!position.Squares.SequenceEqual(previous.Squares)) result.HalfMoves++;
                result.Previous = previous;
                result.Current = position;
                previous = position;
            }
            return result;
        }
    }
}
