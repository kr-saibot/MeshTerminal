using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MeshTerminal.Client;
using Terminal.Gui;
using Dialog = MeshTerminal.SafeDialog;
using MessageBox = MeshTerminal.SafeMessageBox;

namespace MeshTerminal
{
    internal static partial class Program
    {
        private static Action _refreshChessDialog;
        private static void ShowChessForCurrentChat()
        {
            if (_selected == null || _selected.Kind != ChatKind.Direct) { MessageBox.Query("Chess", "Select a direct chat first.", "OK"); return; }
            ShowChessDialog(_selected.Id);
        }
        private static void ShowChessFromMessage(StoredMeshMessage message)
        {
            if (message.Kind != StoredMessageKind.Direct) { MessageBox.Query("Chess", "Chess games are available in direct chats only.", "OK"); return; }
            var partner = message.Direction == MessageDirection.Outgoing ? message.ToNode : message.FromNode;
            if (partner.HasValue) ShowChessDialog(partner.Value);
        }
        private static void ShowChessDialog(uint partner)
        {
            Func<ChessHistory> read = () => ChessHistory.Read(_store.GetDirectMessages(partner).Select(m => Tuple.Create(m.Text, m.Direction == MessageDirection.Outgoing)));
            Func<string> revision = () => String.Join("|", _store.GetDirectMessages(partner).Where(m => { ChessPosition p; return ChessProtocol.TryParse(m.Text, out p); }).Select(m => m.Id.ToString()));
            var history = read(); var baseline = history.Current.Copy(); var position = baseline.Copy();
            bool ownWhite = history.OwnWhite, newGame = !history.HasGame, moved = false, sending = false, active = true;
            string lastRevision = revision(), notice = "";
            Func<bool> chooseColor = () =>
            {
                int answer = MessageBox.Query("New chess game", "Which side do you want to play?", "White", "Black", "Cancel");
                if (answer != 0 && answer != 1) return false;
                ownWhite = answer == 0; newGame = true; moved = false;
                history = new ChessHistory(); baseline = ChessPosition.Initial(); position = baseline.Copy(); notice = "New game: " + (ownWhite ? "make your first move, then send." : "send the starting board to invite White."); return true;
            };
            if (newGame && !chooseColor()) return;
            var dialog = new Dialog("Chess - " + DisplayNodeName(partner), 86, 28);
            var board = new ChessBoardView { X = 0, Y = 0, DisplayMode = ChessBlockPieces.ReadMode(_settings.ChessDisplayMode, _settings.ChessUseLetters) };
            var viewport = new ScrollView { X = 1, Y = 1, Width = 38, Height = 20, AutoHideScrollBars = true, ShowVerticalScrollIndicator = true, ShowHorizontalScrollIndicator = true };
            viewport.Add(board);
            var modeLabel = new Label("Piece display") { X = 0, Y = 19 };
            var modes = new RadioGroup(new NStack.ustring[] { "Symbols", "Letters (ASCII)", "Block figures", "Blocks with spacing" }) { X = 0, Y = 20, SelectedItem = (int)board.DisplayMode };
            var legend = new Label("") { X = 0, Y = 24, Width = Dim.Fill(2) };
            var info = new TextView { X = 0, Y = 0, Width = Dim.Fill(2), Height = 18, ReadOnly = true, WordWrap = true, CanFocus = false };
            var side = new ScrollView { X = 40, Y = 1, Width = Dim.Fill(1), Height = 24, AutoHideScrollBars = true, ShowVerticalScrollIndicator = true, ContentSize = new Size(34, 25) };
            var gameState = new Label("") { X = 0, Y = 4, Width = Dim.Fill(2), Height = 1 };
            var warningColor = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Black);
            gameState.ColorScheme = new ColorScheme { Normal = warningColor, Focus = warningColor, HotNormal = warningColor, HotFocus = warningColor, Disabled = warningColor };
            side.Add(info, gameState, modeLabel, modes, legend);
            var status = new Label("") { X = 1, Y = Pos.AnchorEnd(5), Width = Dim.Fill(2), Height = 2 };
            var resend = new Button("Resend last move");
            var send = new Button("Send"); var undo = new Button("Undo move"); var start = new Button("New game"); var close = new Button("Close", true);
            Action<Point> reveal = cell =>
            {
                int width = Math.Max(1, viewport.Frame.Width - 1), height = Math.Max(1, viewport.Frame.Height - 1);
                int x = -viewport.ContentOffset.X, y = -viewport.ContentOffset.Y;
                if (cell.X < x) x = cell.X;
                if (cell.Y < y) y = cell.Y;
                if (cell.X + board.CellWidth > x + width) x = cell.X + board.CellWidth - width;
                if (cell.Y + board.CellHeight > y + height) y = cell.Y + board.CellHeight - height;
                viewport.ContentOffset = new Point(-Math.Max(0, x), -Math.Max(0, y));
            };
            board.CursorMoved = reveal;
            bool changingMode = false;
            Action layout = () =>
            {
                bool reduced = false;
                while (board.IsBlockMode && (board.CanvasWidth + 44 > Application.Driver.Cols - 2 || (board.DisplayMode == ChessRenderMode.Blocks ? 50 : 56) > Application.Driver.Rows - 2))
                {
                    board.DisplayMode = board.DisplayMode == ChessRenderMode.BlocksSpaced ? ChessRenderMode.Blocks : ChessRenderMode.Letters;
                    reduced = true;
                }
                if (reduced)
                {
                    changingMode = true;
                    try { modes.SelectedItem = (int)board.DisplayMode; }
                    finally { changingMode = false; }
                    _settings.ChessDisplayMode = board.DisplayMode.ToString();
                    _settings.ChessUseLetters = board.UseLetters;
                }
                bool blocks = board.IsBlockMode;
                int width = Math.Min(blocks ? board.CanvasWidth + 44 : 86, Application.Driver.Cols - 2);
                int height = Math.Min(blocks ? (board.DisplayMode == ChessRenderMode.Blocks ? 50 : 56) : 33, Application.Driver.Rows - 2);
                int right = blocks ? Math.Max(40, width - 38) : 40;
                dialog.Width = width; dialog.Height = height;
                // Running modal dialogs have no layout parent to resolve Pos.Center().
                // Apply the screen coordinates and frame directly after every size change.
                int left = Math.Max(0, (Application.Driver.Cols - width) / 2);
                int top = Math.Max(0, (Application.Driver.Rows - height) / 2);
                dialog.X = left; dialog.Y = top;
                dialog.Frame = new Rect(left, top, width, height);
                viewport.Y = blocks ? 0 : 1;
                viewport.Width = right - (blocks ? 2 : 1); viewport.Height = Math.Max(8, height - (blocks ? 4 : 8));
                viewport.ContentSize = new Size(board.CanvasWidth, board.CanvasHeight);
                viewport.ContentOffset = Point.Empty;
                side.X = right; side.Y = blocks ? 0 : 1; side.Height = viewport.Height;
                int firstButtonY = Math.Max(25, Math.Min(28, height - 13));
                side.ContentSize = new Size(34, blocks ? firstButtonY + 9 : 25);
                status.Y = Pos.AnchorEnd(blocks ? 1 : 5); status.Height = blocks ? 1 : 2;
                var buttons = new[] { send, resend, undo, start, close };
                View target = blocks ? (View)side : dialog;
                int total = buttons.Sum(button => button.Frame.Width) + (buttons.Length - 1) * 2;
                int buttonX = Math.Max(1, (width - 2 - total) / 2);
                for (int index = 0; index < buttons.Length; index++)
                {
                    var button = buttons[index];
                    if (button.SuperView != target) { button.SuperView?.Remove(button); target.Add(button); }
                    button.X = blocks ? 0 : buttonX;
                    button.Y = blocks ? (Pos)(firstButtonY + index * 2) : Pos.AnchorEnd(2);
                    buttonX += button.Frame.Width + 2;
                }
                dialog.SuperView?.LayoutSubviews();
                dialog.LayoutSubviews();
                if (reduced) MessageBox.Query("Chess display", "The selected board does not fit on this screen. A smaller display has been selected.", "OK");
                reveal(board.CursorCell);
                dialog.SetNeedsDisplay();
            };
            Action refresh = () =>
            {
                board.Position = position; board.Flipped = !ownWhite;
                legend.Text = board.UseLetters ? "White: KQRBNP | Black: kqrbnp" : "";
                board.Highlights = position.Changed(moved ? baseline : history.Previous);
                int moves = history.HalfMoves + (moved ? 1 : 0);
                bool check = position.InCheck(position.WhiteToMove), legal = position.HasLegalMove();
                string state = !legal ? (check ? "Checkmate" : "Stalemate") : check ? "Check" : "";
                gameState.Text = state;
                info.Text = "You: " + (ownWhite ? "White" : "Black") + "\nTurn: " + (position.WhiteToMove ? "White" : "Black") + "\nMoves: " + moves + " half-moves\nMove number: " + (moves / 2 + 1) + "\n" + "" + "\n\nLast move:\n" + ConvertEmojiForTerminal(position.Notation) + "\n\nCaptured White:\n" + String.Join(" ", Enumerable.Range(0, 16).Where(i => position.Squares[i] == 0).Select(i => ChessPresentation.Piece(position, i, board.DisplayMode != ChessRenderMode.Symbols))) + "\n\nCaptured Black:\n" + String.Join(" ", Enumerable.Range(16, 16).Where(i => position.Squares[i] == 0).Select(i => ChessPresentation.Piece(position, i, board.DisplayMode != ChessRenderMode.Symbols)));
                status.Text = notice.Length > 0 ? notice : moved ? "Move prepared. Send it or undo it." : position.WhiteToMove == ownWhite ? "Your turn: choose a piece and its destination." : "Waiting for the other player.";
                send.Enabled = !sending && (moved || (newGame && !ownWhite)); undo.Enabled = moved && !sending; start.Enabled = !sending;
                var last = LatestChessMessage(partner);
                resend.Enabled = !sending && !moved && !newGame && history.HasGame && last != null && last.Direction == MessageDirection.Outgoing;
                board.SetNeedsDisplay(); dialog.SetNeedsDisplay();
            };
            modes.SelectedItemChanged += previous =>
            {
                if (changingMode) return;
                board.DisplayMode = (ChessRenderMode)modes.SelectedItem;
                _settings.ChessDisplayMode = board.DisplayMode.ToString();
                _settings.ChessUseLetters = board.UseLetters;
                notice = ""; layout(); refresh();
                _settings.ChessDisplayMode = board.DisplayMode.ToString();
                _settings.ChessUseLetters = board.UseLetters;
                try { MeshtasticSettingsStore.Save("meshtastic-settings.xml", _settings); }
                catch (Exception ex) { MessageBox.ErrorQuery("Chess settings", "Could not save the display preference: " + ex.Message, "OK"); }
                ShowSelectedChat();
            };
            board.SquareActivated = square =>
            {
                if (sending || moved || position.WhiteToMove != ownWhite) return;
                int id = position.PieceAt(square);
                if (id >= 0 && (id < 16) == ownWhite) { board.SelectedSquare = square; notice = "Selected " + ChessPresentation.Piece(position, id, board.DisplayMode != ChessRenderMode.Symbols) + ChessPosition.SquareName(square); refresh(); return; }
                if (board.SelectedSquare == 0) return;
                char promotion = 'Q'; int selected = position.PieceAt(board.SelectedSquare);
                if (selected >= 0 && position.Types[selected] == 'P' && (square <= 8 || square >= 57))
                {
                    int choice = MessageBox.Query("Pawn promotion", "Choose a piece.", "Queen", "Rook", "Bishop", "Knight", "Cancel");
                    if (choice < 0 || choice > 3) return; promotion = "QRBN"[choice];
                }
                ChessPosition next; string error;
                if (!position.TryMove(board.SelectedSquare, square, promotion, out next, out error)) { notice = error; refresh(); return; }
                position = next; moved = true; board.SelectedSquare = 0; notice = ""; refresh();
            };
            undo.Clicked += () => { position = baseline.Copy(); moved = false; board.SelectedSquare = 0; notice = ""; refresh(); };
            start.Clicked += () => { if (MessageBox.Query("New chess game", "Replace the current game with a new game?", "Start new game", "Cancel") == 0 && chooseColor()) { board.SelectedSquare = 0; refresh(); } };
            close.Clicked += () => { if (!sending) Application.RequestStop(); };
            Action checkUpdates = () =>
            {
                if (!active) return;
                string latest = revision(); if (latest == lastRevision) return;
                lastRevision = latest; history = read();
                if (!history.HasGame) return;
                baseline = history.Current.Copy(); position = baseline.Copy(); ownWhite = history.OwnWhite; newGame = false; moved = false; board.SelectedSquare = 0;
                notice = "Game updated from the chat. Any unsent move was discarded."; refresh();
            };
            Action sendCompleted = () =>
            {
                RefreshChats();
                if (!active) return;
                sending = false; close.Enabled = true;
                checkUpdates();
                notice = position.WhiteToMove == ownWhite ? "Message sent. Your turn." : "Message sent. Waiting for the other player.";
                refresh(); board.SetFocus();
            };
            resend.Clicked += () =>
            {
                checkUpdates(); if (!resend.Enabled || sending) return;
                var last = LatestChessMessage(partner);
                if (last == null || last.Direction != MessageDirection.Outgoing) return;
                sending = true; notice = "Resending last move..."; close.Enabled = false; refresh();
                Task.Run(async () =>
                {
                    try
                    {
                        await MessageResend.SendAsync(last, _mesh, _store, false);
                        Ui(sendCompleted);
                    }
                    catch (Exception ex) { Ui(() => { if (active) { sending = false; close.Enabled = true; notice = "Resend failed: " + ex.Message; refresh(); } }); }
                });
            };
            send.Clicked += () =>
            {
                checkUpdates(); if (!send.Enabled || sending) return;
                position.NewGame = newGame;
                if (newGame && !moved) position.Notation = "Start";
                string payload = ChessProtocol.Encode(position);
                if (Encoding.UTF8.GetByteCount(payload) > _mesh.GetMaximumTextPayloadBytes(false, partner)) { MessageBox.ErrorQuery("Chess", "The chess message exceeds this device's message limit.", "OK"); return; }
                sending = true; notice = "Sending..."; close.Enabled = false; refresh();
                Task.Run(async () =>
                {
                    try
                    {
                        uint packet = await _mesh.SendTextWithIdAsync(partner, payload);
                        _store.AddOutgoing(packet, _mesh.Device.MyNode == null ? 0u : _mesh.Device.MyNode.MyNodeNum, partner, payload, null);
                        var delivery = _mesh.GetMessageDelivery(packet); if (delivery != null) _store.UpdateDeliveryStatus(packet, delivery.State, detail: delivery.Detail);
                        Ui(sendCompleted);
                    }
                    catch (Exception ex) { Ui(() => { if (active) { sending = false; close.Enabled = true; notice = "Send failed: " + ex.Message; refresh(); } }); }
                });
            };
            dialog.Add(viewport, side, status);
            dialog.KeyPress += e => { if (sending && e.KeyEvent.Key == Key.Esc) e.Handled = true; };
            var previousRefresh = _refreshChessDialog; _refreshChessDialog = checkUpdates;
            try { layout(); refresh(); Application.Run(dialog, HandleTerminalGuiException); }
            finally { active = false; _refreshChessDialog = previousRefresh; }
        }
    }
}
