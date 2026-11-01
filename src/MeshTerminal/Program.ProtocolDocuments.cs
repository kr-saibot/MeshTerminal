using System;
using System.IO;
using System.Linq;
using System.Text;
using Terminal.Gui;
using Dialog = MeshTerminal.SafeDialog;
using MessageBox = MeshTerminal.SafeMessageBox;

namespace MeshTerminal
{
    internal static partial class Program
    {
        private static void ShowProtocolDocuments()
        {
            string[] files;
            try
            {
                var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Protocols");
                files = Directory.Exists(folder) ? Directory.GetFiles(folder)
                    .Where(file => String.Equals(Path.GetExtension(file), ".md", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(file => String.Equals(Path.GetFileName(file), "PROTOCOLS.md", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase).ToArray() : new string[0];
            }
            catch (Exception ex) { MessageBox.ErrorQuery("Protocols", "Could not read the Protocols folder: " + ex.Message, "OK"); return; }
            if (files.Length == 0) { MessageBox.Query("Protocols", "No Markdown documents were found in the Protocols folder next to MeshTerminal.", "OK"); return; }
            var dialog = new Dialog("Protocol documents", Math.Min(82, Application.Driver.Cols - 2), Math.Min(20, Application.Driver.Rows - 2));
            var list = new ListView(files.Select(Path.GetFileName).ToList()) { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(4) };
            Action open = () =>
            {
                if (list.SelectedItem < 0 || list.SelectedItem >= files.Length) return;
                ShowProtocolDocument(files[list.SelectedItem]);
            };
            list.OpenSelectedItem += args => open();
            var show = new Button("Open"); show.Clicked += () => open();
            var close = new Button("Close", true); close.Clicked += () => Application.RequestStop();
            dialog.Add(list); dialog.AddButton(show); dialog.AddButton(close);
            Application.Run(dialog, HandleTerminalGuiException);
        }

        private static void ShowProtocolDocument(string path)
        {
            string content;
            try { content = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex) { MessageBox.ErrorQuery("Protocol document", "Could not open this document: " + ex.Message, "OK"); return; }
            var dialog = new Dialog(Path.GetFileName(path), Math.Min(120, Application.Driver.Cols - 2), Math.Max(8, Application.Driver.Rows - 2));
            var text = new TextView { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3), Text = content, ReadOnly = true, AllowsTab = false, WordWrap = true };
            dialog.Add(text);
            var scrollbar = new ScrollBarView(text, true, false) { AutoHideScrollBars = false };
            bool updating = false;
            scrollbar.ChangedPosition += () => { if (updating) return; text.TopRow = scrollbar.Position; text.SetNeedsDisplay(); };
            text.DrawContent += args =>
            {
                updating = true;
                try { scrollbar.Size = text.Lines; scrollbar.Position = text.TopRow; scrollbar.Refresh(); }
                finally { updating = false; }
            };
            var copy = new Button("Copy all"); copy.Clicked += () => CopyTextWithFallback(content, "Document copied.");
            var close = new Button("Close", true); close.Clicked += () => Application.RequestStop();
            dialog.AddButton(copy); dialog.AddButton(close);
            Application.Run(dialog, HandleTerminalGuiException);
        }
    }
}
