using System;
using Terminal.Gui;
namespace MeshTerminal
{
    internal static class ListViewScrollBar
    {
        internal static ScrollBarView Attach(ListView list)
        {
            var bar = new ScrollBarView(list, true, false) { AutoHideScrollBars = false };
            bool updating = false;
            bar.ChangedPosition += () =>
            {
                var count = list.Source == null ? 0 : list.Source.Count;
                // ListView rejects TopItem=0 when its source is empty.
                if (updating || count == 0) return;
                list.TopItem = Math.Max(0, Math.Min(bar.Position, count - 1));
                list.SetNeedsDisplay();
            };
            list.DrawContent += args =>
            {
                updating = true;
                try
                {
                    var count = list.Source == null ? 0 : list.Source.Count;
                    // The scrollbar still needs a nonzero range for an empty source.
                    bar.Size = Math.Max(1, count);
                    bar.Position = count == 0 ? 0 : Math.Max(0, Math.Min(list.TopItem, count - 1));
                    bar.Refresh();
                }
                finally { updating = false; }
            };
            return bar;
        }
    }
}
