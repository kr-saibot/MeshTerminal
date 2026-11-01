using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using MeshTerminal;
using Terminal.Gui;

internal static class ChessTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("Chess: " + message); }
    private static int S(string value) { return (value[1] - '1') * 8 + value[0] - 'a' + 1; }
    private static ChessPosition Move(ChessPosition p, string from, string to, char promotion = 'Q')
    {
        ChessPosition next; string error;
        Check(p.TryMove(S(from), S(to), promotion, out next, out error), from + "-" + to + " " + error);
        next.NewGame = false; return next;
    }
    public static void Run()
    {
        var initial = ChessPosition.Initial();
        Check(initial.Squares.Distinct().Count() == 32 && initial.Squares[4] == 5 && initial.Squares[20] == 61, "Stable piece IDs / square numbering");
        Check(initial.Symbol(4) == "♔" && initial.Symbol(20) == "♚" && initial.Symbol(31) == "♟", "Piece symbols");
        Check(String.Concat(Enumerable.Range(0, 32).Select(i => ChessPresentation.Piece(initial, i, true))) == "RNBQKBNRPPPPPPPPrnbqkbnrpppppppp", "ASCII pieces use uppercase White and lowercase Black");
        Check(ChessPresentation.Notation("♔♕♖♗♘♙♚♛♜♝♞♟", true) == "KQRBNPkqrbnp", "All twelve symbols have distinct ASCII equivalents");
        Check(ChessPresentation.Notation("♟\uFE0Ee7-e8=♛+", true) == "pe7-e8=q+", "ASCII notation preserves move, promotion and check without variation selectors");
        Check(ChessPresentation.Notation("O-O-O", true) == "O-O-O" && ChessPresentation.Notation("♙e2-e4", false) == "♙e2-e4", "Castling and symbol mode retain notation");
        for (int id = 0; id < 32; id++)
        {
            var unixPiece = ChessPresentation.Piece(initial, id, false, false);
            Check(unixPiece.Length == 1 && unixPiece[0] >= '\u2654' && unixPiece[0] <= '\u265E', "Unix figures are one BMP text character, without emoji pawn or variation selectors");
        }
        Check(ChessPresentation.Piece(initial, 24, false, false) == "♙" && ChessPresentation.Piece(initial, 24, false, true) == "♟\uFE0E", "Platform-specific pawn rendering preserves Windows text presentation");
        string encoded = ChessProtocol.Encode(initial);
        Check(encoded.Length == ChessProtocol.Prefix.Length + 69 && Encoding.UTF8.GetByteCount(encoded) == encoded.Length, "64 hex position characters + 4 hex flag characters + delimiter");
        ChessPosition parsed;
        Check(ChessProtocol.TryParse(encoded, out parsed) && parsed.Squares.SequenceEqual(initial.Squares) && parsed.WhiteToMove && parsed.NewGame, "Initial board round trip");
        foreach (string invalid in new[] { "///CHESS:", encoded.Replace("010203", "FF0203"), encoded.Replace("010203", "020203"), encoded.Substring(0, ChessProtocol.Prefix.Length + 64) + "0200:", encoded + "\n", encoded.Replace("///CHESS:", "///CHESSx") })
            Check(!ChessProtocol.TryParse(invalid, out parsed), "Malformed input rejected");
        ChessPosition result; string error;
        Check(!initial.TryMove(S("e2"), S("e5"), 'Q', out result, out error), "Illegal pawn move");
        Check(!initial.TryMove(S("e7"), S("e5"), 'Q', out result, out error), "Turn enforcement");
        var first = Move(initial, "e2", "e4"); first.NewGame = true;
        Check(first.Notation == "♙e2-e4" && !first.WhiteToMove && Encoding.UTF8.GetByteCount(ChessProtocol.Encode(first)) < 120, "Readable compact move");
        Check(ChessProtocol.Encode(first).EndsWith(":White pawn e2-e4") && ChessProtocol.Encode(first).All(c => c < 128), "Actual transmitted notation uses ASCII names independently of board mode");
        Check(ChessProtocol.NotationAsText("♟\uFE0Ee7-e5") == "Black pawn e7-e5", "Black pawn text omits variation selectors");
        Check(ChessProtocol.NotationAsText("O-O+") == "O-O+", "Castling text is preserved");
        Check(first.Changed(initial).SetEquals(new[] { S("e2"), S("e4") }), "Changed-square highlighting");
        var second = Move(first, "e7", "e5");
        var history = ChessHistory.Read(new[] { Tuple.Create("Hello", false), Tuple.Create(ChessProtocol.Encode(first), true), Tuple.Create(ChessProtocol.Encode(second), false) });
        Check(history.HasGame && history.OwnWhite && history.HalfMoves == 2 && history.Current.Squares.SequenceEqual(second.Squares) && history.Previous.Squares.SequenceEqual(first.Squares), "Outgoing and incoming history, turns, move count, previous board");
        var repeated = ChessHistory.Read(new[] { Tuple.Create(ChessProtocol.Encode(first), true), Tuple.Create(ChessProtocol.Encode(second), false), Tuple.Create("normal chat", true), Tuple.Create(ChessProtocol.Encode(second), false) });
        Check(repeated.HalfMoves == 2 && repeated.Previous.Squares.SequenceEqual(first.Squares) && repeated.Current.Changed(repeated.Previous).SetEquals(second.Changed(first)), "Resent last move does not increment count or erase highlights");
        Check(repeated.Current.EnPassantSquare == S("e6") && repeated.Current.EnPassantPawn == 28, "Retry preserves en-passant rights");
        var repeatedFirst = ChessHistory.Read(new[] { Tuple.Create(ChessProtocol.Encode(first), true), Tuple.Create(ChessProtocol.Encode(first), true) });
        Check(repeatedFirst.HalfMoves == 1 && repeatedFirst.OwnWhite, "Resending game-start move preserves boundary and color");
        var opponent = ChessHistory.Read(new[] { Tuple.Create(ChessProtocol.Encode(first), false), Tuple.Create(ChessProtocol.Encode(second), true) });
        Check(!opponent.OwnWhite, "White initiator assigns Black to receiver");
        var blackInvitation = ChessHistory.Read(new[] { Tuple.Create(encoded, true) });
        Check(!blackInvitation.OwnWhite && blackInvitation.HalfMoves == 0, "Black initiator sends start board without a move");
        var newGame = ChessHistory.Read(new[] { Tuple.Create(ChessProtocol.Encode(first), true), Tuple.Create(ChessProtocol.Encode(second), false), Tuple.Create(encoded, false) });
        Check(newGame.HasGame && newGame.OwnWhite && newGame.HalfMoves == 0, "Latest new-game boundary resets count and colors");
        Check(!ChessHistory.Read(new[] { Tuple.Create(ChessProtocol.Encode(second), false) }).HasGame, "Missing game start uses initial board");
        var capture = Move(Move(Move(initial, "e2", "e4"), "d7", "d5"), "e4", "d5");
        Check(capture.Squares[27] == 0 && capture.Notation == "♙e4xd5", "Capture stores zero");
        var ep = Move(Move(Move(Move(Move(initial, "e2", "e4"), "a7", "a6"), "e4", "e5"), "d7", "d5"), "e5", "d6");
        Check(ep.Squares[27] == 0 && ep.Squares[12] == S("d6"), "En passant");
        var castle = initial;
        foreach (var step in new[] { "e2e4", "a7a6", "g1f3", "a6a5", "f1e2", "b7b6", "e1g1" }) castle = Move(castle, step.Substring(0, 2), step.Substring(2));
        Check(castle.Squares[4] == S("g1") && castle.Squares[7] == S("f1") && castle.Notation == "O-O", "Castling moves rook and king");
        var mate = Move(Move(Move(Move(initial, "f2", "f3"), "e7", "e5"), "g2", "g4"), "d8", "h4");
        Check(mate.InCheck(true) && !mate.HasLegalMove(), "Check and checkmate");
        var promotionStart = new ChessPosition(); promotionStart.Squares[4] = S("e1"); promotionStart.Squares[20] = S("e8"); promotionStart.Squares[8] = S("a7");
        var promotion = Move(promotionStart, "a7", "a8", 'N');
        Check(ChessProtocol.Encode(promotion).EndsWith(":White pawn a7-a8=White knight"), "Promotion notation transmits named piece type");
        Check(promotion.Types[8] == 'N' && promotion.Notation.EndsWith("=♘"), "Underpromotion notation");
        Check(ChessProtocol.TryParse(ChessProtocol.Encode(promotion), out parsed), "Promotion encoded"); ChessProtocol.ApplyHistory(parsed, promotionStart);
        Check(parsed.Types[8] == 'N', "Promoted type restored from notation and stable piece identity");
        Check(ChessProtocol.TryParse(ChessProtocol.Encode(promotion), out parsed) && parsed.Types[8] == 'N', "Promotion type available without notation or history");
        for (int id = 0; id < 32; id++) if ((id >= 8 && id < 16) || id >= 24)
            foreach (char type in "QRBN") foreach (bool capturedPawn in new[] { false, true })
            {
                var promoted = initial.Copy(); promoted.Types[id] = type; promoted.Notation = "";
                if (capturedPawn) promoted.Squares[id] = 0;
                string frame = ChessProtocol.Encode(promoted);
                Check(frame.Length == encoded.Length + 1 && ChessProtocol.TryParse(frame, out parsed) && parsed.Types[id] == type && parsed.Squares[id] == promoted.Squares[id], "Three hex characters preserve every promoted/captured pawn and following pieces");
                ChessProtocol.ApplyHistory(parsed, initial);
                Check(parsed.Types[id] == type, "History must not overwrite encoded promotion type");
                int at = ChessProtocol.Prefix.Length + id * 2;
                Check(!ChessProtocol.TryParse(frame.Remove(at + 2, 1), out result), "Missing promotion digit rejected");
                Check(!ChessProtocol.TryParse(frame.Substring(0, at + 2) + "5" + frame.Substring(at + 3), out result), "Invalid promotion type rejected");
            }
        var allPromoted = initial.Copy();
        for (int id = 8; id < 32; id++) if (id < 16 || id >= 24) { allPromoted.Types[id] = "QRBN"[id % 4]; allPromoted.Squares[id] = 0; }
        Check(ChessProtocol.Encode(allPromoted).Length == encoded.Length + 16 && ChessProtocol.TryParse(ChessProtocol.Encode(allPromoted), out parsed) && parsed.Types.SequenceEqual(allPromoted.Types), "Sixteen promotions and flags round trip");
        string legacyPromotion = ChessProtocol.Prefix + String.Concat(promotion.Squares.Select(square => square.ToString("X2"))) + "0001:" + promotion.Notation;
        Check(ChessProtocol.TryParse(legacyPromotion, out parsed), "Legacy promotion frames still parse"); ChessProtocol.ApplyHistory(parsed, promotionStart);
        Check(parsed.Types[8] == 'N', "Legacy promotion notation remains readable");
        Check(MessageContentParser.Find(ChessProtocol.Encode(first)).Single().Chess != null, "Message links menu recognizes chess");
        Application.Init(new FakeDriver());
        try
        {
            var board = new ChessBoardView { Position = first, Highlights = first.Changed(initial) };
            Application.Top.Add(board); Application.Top.LayoutSubviews(); board.LayoutSubviews(); board.Redraw(board.Bounds);
            Check(board.Frame.Width == 38 && board.Frame.Height == 20, "Board has matching left/right and top/bottom margins");
            Check(board.DisplaySquare(0, 0) == S("a8") && board.DisplaySquare(7, 7) == S("h1"), "White board orientation");
            string frameBefore = ChessProtocol.Encode(board.Position);
            board.UseLetters = true; board.Redraw(board.Bounds);
            var contents = Application.Driver.Contents;
            Check(contents[ChessBoardView.BoardTop + 2, ChessBoardView.BoardLeft + 1, 0] == 'p', "Black pawn actually draws as lowercase ASCII");
            Check(contents[ChessBoardView.BoardTop + 14, ChessBoardView.BoardLeft + 17, 0] == 'K', "White king actually draws as uppercase ASCII");
            Check(ChessProtocol.Encode(board.Position) == frameBefore, "Display switching cannot change transmitted state");
            board.UseLetters = false; board.WindowsTextPresentation = false; board.Redraw(board.Bounds);
            Check(contents[ChessBoardView.BoardTop + 2, ChessBoardView.BoardLeft + 1, 0] == '♙', "Unix black pawn draws as a single text glyph");
            for (int row = 0; row < 8; row++)
            {
                Check(contents[ChessBoardView.BoardTop + row * 2 + 1, 36, 0] == '8' - row, "Unix figures do not shift rank labels");
                for (int file = 0; file < 8; file++)
                    Check(contents[ChessBoardView.BoardTop + row * 2 + 1, ChessBoardView.BoardLeft + file * 4 + 3, 0] == ' ', "Every lower square row retains its four-column grid");
            }
            board.Flipped = true; Check(board.DisplaySquare(0, 0) == S("h1") && board.DisplaySquare(7, 7) == S("a8"), "Black board orientation");
            int clicked = 0; board.SquareActivated = square => clicked = square;
            board.ProcessKey(new KeyEvent(Key.Enter, new KeyModifiers())); Check(clicked == 1, "Keyboard square activation");
        }
        finally { Application.Shutdown(); }
        Console.WriteLine("Chess tests passed: compact protocol, invalid frames, history/colors, captures, legal moves, checkmate, castling, en passant, promotion, message recognition and board rendering.");
    }
}
