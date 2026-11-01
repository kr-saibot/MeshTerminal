using System;
using System.Threading;
using System.Threading.Tasks;
using MeshTerminal.Client;
using Terminal.Gui;
using Dialog = MeshTerminal.SafeDialog;

namespace MeshTerminal
{
    internal static partial class Program
    {
        private static void ShowMeshCorePing(StoredMeshNode node)
        {
            var dialog = new Dialog("MeshCore reachability: " + (node.LongName ?? node.NodeId), 82, 13);
            var info = new TextView { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(4), ReadOnly = true, WordWrap = true,
                Text = "Companion: sends a visible direct message: Ping (MeshTerminal).\nOther node types: requests status; login permissions may be required.\nA timeout means no reply arrived; it does not prove the node is offline." };
            var start = new Button("Check"); var close = new Button("Close", true);
            var cancellation = new CancellationTokenSource(); bool active = true, busy = false;
            start.Clicked += async () =>
            {
                if (busy) return;
                busy = true; start.Enabled = false; info.Text = "Waiting for a reply...";
                string result;
                try
                {
                    var elapsed = await Task.Run(() => _mesh.CheckMeshCoreReachabilityAsync(node.NodeNumber, cancellation.Token));
                    result = "Reply received from " + (node.LongName ?? node.NodeId) + ".\nResponse time: " + elapsed.TotalMilliseconds.ToString("0") + " ms.";
                }
                catch (TimeoutException) { result = "Timeout: no matching reply received within the device's response deadline.\nThe node may still be reachable. Check radio conditions and status permissions."; }
                catch (OperationCanceledException) { result = "Check cancelled."; }
                catch (Exception ex) { result = "Check failed: " + ex.Message; }
                Ui(() => { if (!active) return; info.Text = result; busy = false; start.Enabled = true; });
            };
            close.Clicked += () => Application.RequestStop();
            dialog.Add(info); dialog.AddButton(start); dialog.AddButton(close);
            try { Application.Run(dialog, HandleTerminalGuiException); }
            finally { active = false; cancellation.Cancel(); }
        }
    }
}
