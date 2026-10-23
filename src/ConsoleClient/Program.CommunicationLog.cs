using MessageBox = ConsoleClient.SafeMessageBox;
using Dialog = ConsoleClient.SafeDialog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Terminal.Gui;

namespace ConsoleClient
{
    internal sealed class CommunicationLogView : View
    {
        private sealed class Row { internal string Raw, Decoded; internal bool Tx; }
        private readonly List<Row> _rows = new List<Row>();
        private readonly CommunicationLogBuffer _buffer;
        private int _offset, _width = -1;
        private long _version = -1;
        private bool _follow = true, _updatingBar;
        private ScrollBarView _scrollBar;
        internal Color BackgroundColor = Color.Black;
        internal CommunicationLogView(CommunicationLogBuffer buffer) { _buffer = buffer; CanFocus = true; }
        internal void Refresh() { if (_buffer.Version != _version) SetNeedsDisplay(); }
        internal void AttachScrollBar()
        {
            _scrollBar = new ScrollBarView(this, true, false) { AutoHideScrollBars = false };
            _scrollBar.ChangedPosition += delegate
            {
                if (_updatingBar) return;
                _follow = false; _offset = _scrollBar.Position; SetNeedsDisplay();
            };
        }
        private static List<string> Wrap(string value, int width)
        {
            var result = new List<string>();
            foreach (var line in value.Replace("\r", "").Replace("\t", "    ").Split('\n'))
            {
                // Work with text elements so surrogate pairs are never split.
                var elements = StringInfo.GetTextElementEnumerator(line); var part = ""; int used = 0;
                while (elements.MoveNext())
                {
                    var element = elements.GetTextElement();
                    int cells = Math.Max(0, NStack.ustring.Make(element).ConsoleWidth);
                    if (used + cells > width && part.Length > 0) { result.Add(part); part = ""; used = 0; }
                    part += Char.IsControl(element[0]) ? "?" : element; used += cells;
                }
                result.Add(part);
            }
            return result;
        }
        public override void Redraw(Rect bounds)
        {
            int width = Math.Max(4, Bounds.Width - 1), left = (width - 3) / 2, right = width - left - 3, height = Math.Max(1, Bounds.Height - 1);
            if (_version != _buffer.Version || _width != width)
            {
                _width = width; _version = _buffer.Version; _rows.Clear();
                foreach (var entry in _buffer.Snapshot())
                {
                    var f = entry.Frame;
                    var header = f.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + f.Direction + " " + f.Protocol + " / " + f.Transport;
                    var raw = Wrap(header + "\n" + entry.Hex, Math.Max(1, left));
                    var decoded = Wrap(header + "\n" + entry.Decoded, Math.Max(1, right));
                    for (int i = 0; i < Math.Max(raw.Count, decoded.Count); i++)
                        _rows.Add(new Row { Raw = i < raw.Count ? raw[i] : "", Decoded = i < decoded.Count ? decoded[i] : "", Tx = f.Direction == "TX" });
                    _rows.Add(new Row { Raw = "", Decoded = "" });
                }
            }
            int maximum = Math.Max(0, _rows.Count - height);
            _offset = _follow ? maximum : Math.Min(_offset, maximum);
            Application.Driver.SetAttribute(Application.Driver.MakeAttribute(Color.Gray, BackgroundColor));
            for (int y = 0; y < Bounds.Height; y++) { Move(0, y); Application.Driver.AddStr(new string(' ', width)); Move(left + 1, y); Application.Driver.AddStr("│"); }
            Move(0, 0); Application.Driver.AddStr("Raw data (hex)"); Move(left + 3, 0); Application.Driver.AddStr("Decoded");
            for (int y = 0; y < height && _offset + y < _rows.Count; y++)
            {
                var row = _rows[_offset + y];
                Application.Driver.SetAttribute(Application.Driver.MakeAttribute(row.Tx ? Color.BrightYellow : Color.BrightCyan, BackgroundColor));
                Move(0, y + 1); Application.Driver.AddStr(row.Raw);
                Move(left + 3, y + 1); Application.Driver.AddStr(row.Decoded);
            }
            if (_scrollBar != null)
            {
                _updatingBar = true;
                try { _scrollBar.Size = Math.Max(Bounds.Height, _rows.Count + 1); _scrollBar.Position = _offset; _scrollBar.Refresh(); }
                finally { _updatingBar = false; }
            }
        }
        private void Scroll(int amount) { _follow = false; _offset = Math.Max(0, Math.Min(Math.Max(0, _rows.Count - Math.Max(1, Bounds.Height - 1)), _offset + amount)); SetNeedsDisplay(); }
        public override bool ProcessKey(KeyEvent e)
        {
            switch (e.Key)
            {
                case Key.CursorUp: Scroll(-1); return true; case Key.CursorDown: Scroll(1); return true;
                case Key.PageUp: Scroll(-Math.Max(1, Bounds.Height - 2)); return true;
                case Key.PageDown: Scroll(Math.Max(1, Bounds.Height - 2)); return true;
                case Key.Home: _offset = 0; _follow = false; SetNeedsDisplay(); return true;
                case Key.End: Follow(); return true;
            }
            return base.ProcessKey(e);
        }
        public override bool MouseEvent(MouseEvent e)
        {
            if (e.Flags.HasFlag(MouseFlags.WheeledUp)) { Scroll(-3); return true; }
            if (e.Flags.HasFlag(MouseFlags.WheeledDown)) { Scroll(3); return true; }
            return base.MouseEvent(e);
        }
        internal void Follow() { _follow = true; SetNeedsDisplay(); }
    }

    internal static partial class Program
    {
        private static readonly CommunicationLogBuffer DeviceCommunicationLog = new CommunicationLogBuffer();
        private static void ShowCommunicationLog()
        {
            var dialog = new Dialog("Device communication log") { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(2) };
            var capacity = new TextField(DeviceCommunicationLog.Capacity.ToString()) { X = 15, Y = 0, Width = 8 };
            var apply = new Button("Apply") { X = 25, Y = 0 };
            var status = new Label("") { X = 37, Y = 0, Width = Dim.Fill(1) };
            var log = new CommunicationLogView(DeviceCommunicationLog) { X = 1, Y = 3, Width = Dim.Fill(2), Height = Dim.Fill(3), BackgroundColor = ParseColor(_settings.Appearance.BackgroundColor, Color.Black) };
            var loRaOnly = new CheckBox("Record LoRa-related frames only") { X = 1, Y = 1, Checked = DeviceCommunicationLog.LoRaOnly };
            loRaOnly.Toggled += delegate
            {
                DeviceCommunicationLog.LoRaOnly = loRaOnly.Checked; _settings.CommunicationLogLoRaOnly = loRaOnly.Checked;
                Meshtastic.Client.MeshtasticSettingsStore.Save("meshtastic-settings.xml", _settings); log.Refresh();
            };
            var pause = new Button(DeviceCommunicationLog.Paused ? "Resume" : "Pause");
            pause.Clicked += delegate { DeviceCommunicationLog.Paused = !DeviceCommunicationLog.Paused; pause.Text = DeviceCommunicationLog.Paused ? "Resume" : "Pause"; log.Refresh(); };
            apply.Clicked += delegate
            {
                int count;
                if (!Int32.TryParse(capacity.Text.ToString(), out count) || count < 1 || count > 10000) { MessageBox.ErrorQuery("Buffer size", "Enter 1-10000 frames.", "OK"); return; }
                _settings.CommunicationLogCapacity = count; DeviceCommunicationLog.SetCapacity(count);
                Meshtastic.Client.MeshtasticSettingsStore.Save("meshtastic-settings.xml", _settings); log.Refresh();
            };
            var clear = new Button("Clear"); clear.Clicked += delegate { DeviceCommunicationLog.Clear(); log.Follow(); };
            var copy = new Button("Copy all"); copy.Clicked += delegate { CopyTextWithFallback(DeviceCommunicationLog.CopyText(), "Communication log copied."); };
            var follow = new Button("Follow / End"); follow.Clicked += delegate { log.Follow(); };
            var close = new Button("Close", true); close.Clicked += delegate { Application.RequestStop(); };
            dialog.Add(new Label("FIFO frames:") { X = 1, Y = 0 }, capacity, apply, status, loRaOnly, log);
            log.AttachScrollBar();
            dialog.AddButton(pause); dialog.AddButton(clear); dialog.AddButton(copy); dialog.AddButton(follow); dialog.AddButton(close);
            var timer = Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(250), loop =>
            {
                status.Text = (DeviceCommunicationLog.Paused ? "Paused" : "Recording") + " | " + DeviceCommunicationLog.Snapshot().Length + "/" + DeviceCommunicationLog.Capacity + " | RX cyan / TX yellow";
                log.Refresh(); return true;
            });
            try { log.SetFocus(); Application.Run(dialog, HandleTerminalGuiException); }
            finally { Application.MainLoop.RemoveTimeout(timer); }
        }
    }
}
