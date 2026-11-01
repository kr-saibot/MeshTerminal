using System;
using System.Linq;
using MeshTerminal;
using Terminal.Gui;

internal static class ChessBlockTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("Chess blocks: " + message); }
    internal static void Run()
    {
        var shapes = "KQRBNP".Select(ChessBlockPieces.Rows).ToArray();
        Check(shapes.All(rows => rows.Length == 5 && rows.All(row => row.Length == 7 && row.All(c => " ▄▀█".Contains(c)))), "All six sprites use fixed-width 7x5 block cells");
        Check(shapes.Select(rows => String.Join("\n", rows)).Distinct().Count() == 6, "Every piece has a distinct silhouette");
        Check(ChessBlockPieces.Rows('R')[1] == "███████" && ChessBlockPieces.Rows('Q')[1] != "███████", "Rook has a broad, straight tower below its battlements");
        Check(ChessBlockPieces.Rows('P').SequenceEqual(ChessBlockPieces.Rows('p')), "Both sides share silhouettes and are coloured by renderer");
        Check(ChessBlockPieces.ReadMode(null, true) == ChessRenderMode.Letters && ChessBlockPieces.ReadMode(null, false) == ChessRenderMode.Symbols, "Existing display choices migrate unchanged");
        Check(ChessBlockPieces.ReadMode("Blocks", true) == ChessRenderMode.Blocks && ChessBlockPieces.ReadMode("invalid", false) == ChessRenderMode.Symbols, "Saved mode and invalid preference fallback");
        Application.Init(new FakeDriver());
        try
        {
            var board = new ChessBoardView { Position = ChessPosition.Initial(), DisplayMode = ChessRenderMode.Blocks };
            var scroll = new ScrollView(new Rect(0, 0, 74, 20)) { ContentSize = new Size(board.CanvasWidth, board.CanvasHeight), AutoHideScrollBars = true, ShowVerticalScrollIndicator = true, ShowHorizontalScrollIndicator = true };
            scroll.Add(board); Application.Top.Add(scroll); Application.Top.LayoutSubviews(); board.LayoutSubviews();
            Check(board.Frame.Width == 70 && board.Frame.Height == 44, "Large board has correct dimensions");
            string payload = ChessProtocol.Encode(board.Position);
            board.Redraw(board.Bounds);
            var contents = Application.Driver.Contents;
            Check(contents[ChessBoardView.BoardTop, ChessBoardView.BoardLeft, 0] == '█', "Rook is painted using the block sprite");
            Check(contents[ChessBoardView.BoardTop + 5, ChessBoardView.BoardLeft + 2, 0] == '▄', "Pawn is painted using half blocks");
            Point cell = Point.Empty; board.CursorMoved = value => cell = value;
            board.ProcessKey(new KeyEvent(Key.CursorRight, new KeyModifiers()));
            Check(cell.X == ChessBoardView.BoardLeft + 8 && cell.Y == ChessBoardView.BoardTop + 35, "Arrow navigation reports enlarged cell coordinates for scrolling");
            board.DisplayMode = ChessRenderMode.BlocksSpaced; board.LayoutSubviews();
            Check(board.CanvasWidth == 94 && board.CanvasHeight == 52 && board.CellWidth == 11 && board.CellHeight == 6, "Fourth mode adds three columns and one row to every cell");
            board.Redraw(board.Bounds);
            Check(contents[ChessBoardView.BoardTop, ChessBoardView.BoardLeft, 0] == ' ' && contents[ChessBoardView.BoardTop, ChessBoardView.BoardLeft + 2, 0] == '█', "Spaced sprites have horizontal inset");
            for (int x = 0; x < 11; x++) Check(contents[ChessBoardView.BoardTop + 5, ChessBoardView.BoardLeft + x, 0] == ' ', "Spaced cells have an empty row between figures");
            Check(contents[ChessBoardView.BoardTop + 2, 1, 0] == '8', "Rank labels are shifted down on both sides");
            Check(ChessBlockPieces.ReadMode("BlocksSpaced", false) == ChessRenderMode.BlocksSpaced, "Fourth mode preference is recognised");
            board.DisplayMode = ChessRenderMode.Letters; board.LayoutSubviews();
            Check(board.Frame.Width == 38 && board.Frame.Height == 20 && board.UseLetters, "Original compact letter mode retained");
            board.DisplayMode = ChessRenderMode.Symbols;
            Check(!board.UseLetters && ChessProtocol.Encode(board.Position) == payload, "All mode changes preserve wire state");
        }
        finally { Application.Shutdown(); }
        Console.WriteLine("Chess block tests passed: six fixed-width sprites, rendered board, scroll geometry, existing modes, preference migration and unchanged game state.");
    }
}
