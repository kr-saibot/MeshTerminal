using System;
using System.Collections.Generic;
using Terminal.Gui;

namespace MeshTerminal
{
    internal sealed class ChessBoardView : View
    {
        internal ChessPosition Position;
        internal HashSet<int> Highlights = new HashSet<int>();
        internal bool Flipped;
        private ChessRenderMode _displayMode;
        internal ChessRenderMode DisplayMode
        {
            get { return _displayMode; }
            set { _displayMode = value; Width = CanvasWidth; Height = CanvasHeight; SetNeedsDisplay(); }
        }
        internal bool UseLetters { get { return DisplayMode == ChessRenderMode.Letters; } set { DisplayMode = value ? ChessRenderMode.Letters : ChessRenderMode.Symbols; } }
        internal bool IsBlockMode { get { return DisplayMode == ChessRenderMode.Blocks || DisplayMode == ChessRenderMode.BlocksSpaced; } }
        internal int CellWidth { get { return DisplayMode == ChessRenderMode.BlocksSpaced ? 11 : IsBlockMode ? 8 : 4; } }
        internal int CellHeight { get { return DisplayMode == ChessRenderMode.BlocksSpaced ? 6 : IsBlockMode ? 5 : 2; } }
        internal int CanvasWidth { get { return BoardLeft * 2 + CellWidth * 8; } }
        internal int CanvasHeight { get { return BoardTop * 2 + CellHeight * 8; } }
        internal Action<Point> CursorMoved;
        internal Point CursorCell
        {
            get
            {
                int x = (_cursor - 1) % 8, y = (_cursor - 1) / 8;
                return new Point(BoardLeft + (Flipped ? 7 - x : x) * CellWidth, BoardTop + (Flipped ? y : 7 - y) * CellHeight);
            }
        }
        internal bool WindowsTextPresentation = Environment.OSVersion.Platform == PlatformID.Win32NT;
        internal Action<int> SquareActivated;
        internal int SelectedSquare = 0;
        internal const int BoardLeft = 3, BoardTop = 2;
        private int _cursor = 1;
        internal ChessBoardView() { CanFocus = true; Width = 38; Height = 20; }
        internal int DisplaySquare(int x, int y) { return Flipped ? (y * 8 + 8 - x) : ((7 - y) * 8 + x + 1); }
        public override void Redraw(Rect bounds)
        {
            if (Position == null) return;
            for (int y = 0; y < CanvasHeight; y++) { Move(0, y); Driver.SetAttribute(Driver.MakeAttribute(Color.Gray, Color.Black)); Driver.AddStr(new string(' ', CanvasWidth)); }
            for (int x = 0; x < 8; x++) { Move(BoardLeft + CellWidth / 2 - 1 + x * CellWidth, 0); Driver.AddRune((char)('a' + (Flipped ? 7 - x : x))); Move(BoardLeft + CellWidth / 2 - 1 + x * CellWidth, CanvasHeight - 1); Driver.AddRune((char)('a' + (Flipped ? 7 - x : x))); }
            for (int y = 0; y < 8; y++)
            {
                Driver.SetAttribute(Driver.MakeAttribute(Color.Gray, Color.Black)); Move(1, BoardTop + y * CellHeight + (IsBlockMode ? 2 : 1)); Driver.AddStr((Flipped ? y + 1 : 8 - y).ToString());
                Move(CanvasWidth - 2, BoardTop + y * CellHeight + (IsBlockMode ? 2 : 1)); Driver.AddStr((Flipped ? y + 1 : 8 - y).ToString());
                for (int x = 0; x < 8; x++)
                {
                    int square = DisplaySquare(x, y), id = Position.PieceAt(square);
                    var background = Highlights.Contains(square) ? Color.Brown : ((x + y) % 2 == 0 ? Color.Gray : Color.DarkGray);
                    if (square == SelectedSquare) background = Color.Blue;
                    for (int row = 0; row < CellHeight; row++)
                    {
                        Move(BoardLeft + x * CellWidth, BoardTop + y * CellHeight + row);
                        Driver.SetAttribute(Driver.MakeAttribute(Color.Black, background)); Driver.AddStr(new string(' ', CellWidth));
                    }
                    Driver.SetAttribute(Driver.MakeAttribute(id >= 0 && id < 16 ? Color.White : Color.Black, background));
                    if (id >= 0 && IsBlockMode)
                    {
                        var rows = ChessBlockPieces.Rows(Position.Types[id]);
                        for (int row = 0; row < rows.Length; row++)
                        {
                            Move(BoardLeft + x * CellWidth + (CellWidth - ChessBlockPieces.Width) / 2, BoardTop + y * CellHeight + row);
                            Driver.AddStr(rows[row]);
                        }
                    }
                    else if (!IsBlockMode)
                    {
                        Move(BoardLeft + 1 + x * CellWidth, BoardTop + y * CellHeight);
                        Driver.AddStr(id < 0 ? " " : ChessPresentation.Piece(Position, id, UseLetters, WindowsTextPresentation));
                    }
                    if (HasFocus && square == _cursor)
                    {
                        Driver.SetAttribute(Driver.MakeAttribute(Color.BrightYellow, background));
                        Move(BoardLeft + x * CellWidth + CellWidth - 1, BoardTop + y * CellHeight + CellHeight - 1);
                        Driver.AddRune('<');
                    }
                }
            }
        }
        public override bool ProcessKey(KeyEvent e)
        {
            int x = (_cursor - 1) % 8, y = (_cursor - 1) / 8, direction = Flipped ? -1 : 1;
            switch (e.Key)
            {
                case Key.CursorLeft: x -= direction; break;
                case Key.CursorRight: x += direction; break;
                case Key.CursorUp: y += direction; break;
                case Key.CursorDown: y -= direction; break;
                case Key.Enter: case (Key)' ': SquareActivated?.Invoke(_cursor); return true;
                default: return base.ProcessKey(e);
            }
            _cursor = Math.Max(0, Math.Min(7, y)) * 8 + Math.Max(0, Math.Min(7, x)) + 1; CursorMoved?.Invoke(CursorCell); SetNeedsDisplay(); return true;
        }
        public override bool MouseEvent(MouseEvent e)
        {
            if (!e.Flags.HasFlag(MouseFlags.Button1Clicked)) return base.MouseEvent(e);
            int x = (e.X - BoardLeft) / CellWidth, y = (e.Y - BoardTop) / CellHeight;
            if (e.X < BoardLeft || e.Y < BoardTop || x > 7 || y > 7) return false;
            _cursor = DisplaySquare(x, y); SetFocus(); SquareActivated?.Invoke(_cursor); SetNeedsDisplay(); return true;
        }
    }
}
