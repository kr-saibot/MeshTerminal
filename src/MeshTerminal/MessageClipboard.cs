using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
namespace MeshTerminal
{
    internal static class MessageClipboard
    {
        internal static string Read()
        {
            string text;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                text = ReadOnStaThread(() => System.Windows.Forms.Clipboard.GetText(System.Windows.Forms.TextDataFormat.UnicodeText));
            else if (!Terminal.Gui.Clipboard.TryGetClipboardData(out text))
                throw new InvalidOperationException("The clipboard is not available.");
            return CleanText(text);
        }
        internal static string ReadOnStaThread(Func<string> read)
        {
            string text = null;
            Exception error = null;
            var worker = new Thread(() =>
            {
                try
                {
                    for (int attempt = 0; ; attempt++)
                    {
                        try { text = read(); break; }
                        catch (ExternalException) { if (attempt >= 2) throw; Thread.Sleep(30); }
                    }
                }
                catch (Exception ex) { error = ex; }
            }) { IsBackground = true, Name = "MeshTerminal clipboard reader" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            if (!worker.Join(3000)) throw new TimeoutException("Reading the clipboard timed out. Please try again.");
            if (error != null) throw new InvalidOperationException("The clipboard could not be read. " + error.Message, error);
            return text ?? "";
        }
        internal static string CleanText(string text)
        {
            var result = new StringBuilder();
            foreach (char c in (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n"))
            {
                if (c == '\t') result.Append("    ");
                else if (c == '\n' || !Char.IsControl(c)) result.Append(c);
            }
            // Replace malformed UTF-16 before passing text to the terminal model.
            return Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(result.ToString()));
        }
    }
}
