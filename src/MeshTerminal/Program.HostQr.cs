using System;
using System.Linq;
using System.Threading;
using Terminal.Gui;
using Dialog = MeshTerminal.SafeDialog;
using MessageBox = MeshTerminal.SafeMessageBox;
namespace MeshTerminal
{
    internal static partial class Program
    {
        private static HostQrBridge _hostQrBridge;
        private static int _hostQrPending;
        private static void StartHostQrBridge()
        {
            var name = Environment.GetEnvironmentVariable(HostQrBridge.EnvironmentKey);
            // Child shell commands must not inherit the host channel.
            Environment.SetEnvironmentVariable(HostQrBridge.EnvironmentKey, null);
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || !HostQrBridge.ValidName(name)) return;
            _hostQrBridge = new HostQrBridge(name, text =>
            {
                if (Volatile.Read(ref _shutdownStarted) != 0) throw new InvalidOperationException();
                if (Interlocked.CompareExchange(ref _hostQrPending, 1, 0) != 0) throw new InvalidOperationException();
                Ui(() => { try { ShowHostQrText(text); } finally { Interlocked.Exchange(ref _hostQrPending, 0); } });
            });
        }
        private static void ShowHostQrText(string text)
        {
            if (Volatile.Read(ref _shutdownStarted) != 0) return;
            var targets = MessageContentParser.Find(text).Where(t => t.IsContact || t.IsMeshCoreChannel || t.IsMeshtasticChannel).ToList();
            if (targets.Count == 1 && String.Equals(targets[0].Text.Trim(), text.Trim(), StringComparison.Ordinal))
            {
                var target = targets[0];
                if (target.IsContact) ShowSharedContactImport(target.Url, target.ImportProtocol);
                else ShowSharedChannelImport(target.Url, target.ImportProtocol);
                return;
            }
            var dialog = new Dialog("QR text from host", Math.Min(90, Application.Driver.Cols - 2), Math.Min(20, Application.Driver.Rows - 2));
            var preview = new TextView { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(4), ReadOnly = true, AllowsTab = false, WordWrap = true, Text = text };
            bool insert = false;
            var add = new Button("Insert into message"); add.Clicked += () => { insert = true; Application.RequestStop(); };
            var copy = new Button("Copy"); copy.Clicked += () => CopyTextWithFallback(text, "QR text copied.");
            var close = new Button("Cancel", true); close.Clicked += () => Application.RequestStop();
            dialog.Add(preview); dialog.AddButton(add); dialog.AddButton(copy); dialog.AddButton(close);
            Application.Run(dialog, HandleTerminalGuiException);
            if (insert) { ShowChatPage(); MessageTextInsertion.Insert(_input, text); _input.SetFocus(); }
        }
    }
}
