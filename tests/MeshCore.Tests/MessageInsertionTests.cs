using System;
using MeshTerminal;
using Terminal.Gui;
internal static class MessageInsertionTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        Check(MessageClipboard.CleanText("a\t😀\0\u001bb\r\nc\rd") == "a    😀b\nc\nd", "Clipboard control characters must not reach terminal model");
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            Check(MessageClipboard.ReadOnStaThread(() => System.Threading.Thread.CurrentThread.GetApartmentState().ToString()) == "STA", "Windows clipboard reads require STA");
            int attempts = 0;
            Check(MessageClipboard.ReadOnStaThread(() => { if (++attempts < 3) throw new System.Runtime.InteropServices.ExternalException("busy"); return "clipboard test"; }) == "clipboard test" && attempts == 3, "Transient clipboard locking must be retried");
            bool handled = false;
            try { MessageClipboard.ReadOnStaThread(() => { throw new InvalidOperationException("test error"); }); }
            catch (InvalidOperationException) { handled = true; }
            Check(handled, "Clipboard failures return to caller instead of crashing a background thread");
        }
        Application.Init(new FakeDriver());
        try
        {
            var input = new TextView(new Rect(0, 0, 13, 4)) { WordWrap = true };
            Application.Top.Add(input);
            input.Text = "Hello world"; input.CursorPosition = new Point(5, 0);
            MessageTextInsertion.Insert(input, "😀");
            Check(input.Text.ToString().Replace("\r\n", "\n") == "Hello😀 world", "Emoji insertion must preserve text and valid runes");
            MessageTextInsertion.Insert(input, "👍🏽");
            Check(input.Text.ToString().Replace("\r\n", "\n") == "Hello😀👍🏽 world", "Cursor after emoji must count Unicode runes");
            input.Text = "before after"; input.CursorPosition = new Point(7, 0);
            MessageTextInsertion.Insert(input, "line1\r\nline2😀\r");
            Check(input.Text.ToString().Replace("\r\n", "\n") == "before line1\nline2😀\nafter", "Multiline clipboard text and cursor must survive wrapping");
            MessageTextInsertion.Insert(input, "X");
            Check(input.Text.ToString().Replace("\r\n", "\n") == "before line1\nline2😀\nXafter", "Cursor must follow multiline insertion");
            input.Text = "abcdefghijklmnopqrstuvwxyz"; input.CursorPosition = new Point(6, 1);
            MessageTextInsertion.Insert(input, "😀");
            Check(input.Text.ToString().Replace("\r\n", "\n") == "abcdefghijklmnopqr😀stuvwxyz", "Insert into a wrapped visual row at the original text position");
            for (int i = 0; i < 30; i++) MessageTextInsertion.Insert(input, "😀");
            Check(input.Text.ToString().Replace("\r\n", "\n").Contains("😀😀😀"), "Repeated emoji insertion must not corrupt the wrap model");
            input.Text = "AB"; input.CursorPosition = new Point(1, 0);
            MessageTextInsertion.Insert(input, MessageClipboard.CleanText("x\t😀\0\r\ny"));
            Check(input.Text.ToString().Replace("\r\n", "\n") == "Ax    😀\nyB", "Cleaned clipboard inserts safely in wrapped input");
            Check(input.WordWrap, "Wrapping must remain enabled");
            Application.Top.Remove(input); input.Dispose();
        }
        finally { Application.Shutdown(); }
        Console.WriteLine("Message insertion tests passed: real wrapped TextView, emojis, surrogate pairs, multiline clipboard, cursor position and repeated insertion.");
    }
}
