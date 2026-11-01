using System;
using System.Linq;
using System.Collections.Generic;
using MeshTerminal;
using Terminal.Gui;
internal static class NodeListScrollBarTests
{
    internal static void Run()
    {
        Application.Init(new FakeDriver());
        try
        {
            var frame = new FrameView(new Rect(0, 0, 60, 15));
            var list = new ListView { Width = Dim.Fill(), Height = Dim.Fill() };
            frame.Add(list); Application.Top.Add(frame);
            var bar = ListViewScrollBar.Attach(list);
            Application.Top.LayoutSubviews();
            list.SetSource(Enumerable.Range(0, 100).Select(i => "Node " + i).ToList());
            list.SelectedItem = 80; list.TopItem = 70;
            list.Redraw(list.Bounds);
            list.SetSource(new List<string>());
            list.Redraw(list.Bounds);
            bar.Position = 1; bar.Position = 0;
            list.Redraw(list.Bounds);
            if (bar.Position != 0) throw new Exception("Empty node search did not reset scrollbar");
            list.SetSource(new List<string> { "Found again" });
            list.SelectedItem = 0; list.Redraw(list.Bounds);
            if (list.TopItem != 0 || list.SelectedItem != 0) throw new Exception("Restoring search results lost the first item");
            list.SetSource(new List<string>()); list.Redraw(list.Bounds);
        }
        finally { Application.Shutdown(); }
        Console.WriteLine("Node list scrollbar tests passed: scrolled list, zero search results, empty scrolling and restored results.");
    }
}
