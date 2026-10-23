using System;
using System.Collections.Generic;
using Terminal.Gui;
namespace ConsoleClient
{
    // The framed list is a temporary top-level popup, so it covers the transcript
    // without reserving space or drawing a permanent frame around the selector.
    internal sealed class CommandDropdown : Button
    {
        private List<string> items = new List<string>();
        private int selected;
        public int SelectedItem { get { return selected; } set { selected = value; UpdateText(); } }
        public CommandDropdown() { Clicked += Open; }
        public void SetSource(List<string> source) { items = source; UpdateText(); }
        private void UpdateText() { Text = (selected >= 0 && selected < items.Count ? items[selected] : "Select command") + "  v"; }
        public override bool ProcessKey(KeyEvent keyEvent)
        {
            if (keyEvent.Key == Key.CursorDown || keyEvent.Key == Key.F4) { Open(); return true; }
            return base.ProcessKey(keyEvent);
        }
        private void Open()
        {
            if (!Enabled || items.Count == 0) return;
            var origin = ScreenToView(0, 0);
            var point = new Point(-origin.X, 1 - origin.Y);
            var width = Math.Min(78, Application.Top.Frame.Width - 2);
            var height = Math.Min(17, Application.Top.Frame.Height - point.Y - 1);
            if (height < 5) height = Math.Min(17, Application.Top.Frame.Height - 2);
            var popup = new SafeDialog("Repeater commands", width, height)
            {
                X = Math.Max(0, Math.Min(point.X, Application.Top.Frame.Width - width)),
                Y = Math.Max(0, Math.Min(point.Y, Application.Top.Frame.Height - height))
            };
            var list = new ListView(items) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), SelectedItem = Math.Max(0, selected) };
            list.OpenSelectedItem += args => { SelectedItem = list.SelectedItem; Application.RequestStop(); };
            popup.Add(list);
            popup.Ready += () => list.SetFocus();
            Application.Run(popup);
            SetFocus();
        }
    }
}
