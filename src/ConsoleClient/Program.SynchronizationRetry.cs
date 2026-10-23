using Dialog = ConsoleClient.SafeDialog;
using System;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Client;
using Terminal.Gui;
namespace ConsoleClient
{
    internal static partial class Program
    {
        private static async Task<bool> ConfirmSynchronizationRetryAsync(SynchronizationTimeoutInfo info, CancellationToken token)
        {
            var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dialog dialog = null;
            Action close = () =>
            {
                if (dialog == null) return;
                Application.Top.Remove(dialog);
                dialog.Dispose(); dialog = null;
                if (_pleaseWait != null) _pleaseWait.SetFocus();
                Application.Top.SetNeedsDisplay();
            };
            using (token.Register(() =>
            {
                answer.TrySetCanceled();
                Ui(close);
            }))
            {
                Ui(() =>
                {
                    if (answer.Task.IsCompleted) return;
                    dialog = new Dialog("Node synchronization timeout", 78, 12) { Modal = true };
                    var count = info.ReceivedNodes + " / " + (info.ExpectedNodes < 0 ? "?" : info.ExpectedNodes.ToString());
                    var last = String.IsNullOrWhiteSpace(info.LastNodeName) ? "-" : info.LastNodeName;
                    dialog.Add(new Label("Attempt " + info.Attempt + ": No new nodes received for " + info.Timeout.TotalSeconds.ToString("0") + " seconds.\nReceived: " + count + "\nLast received node: " + last +
                        "\n\nRetry the transfer, or skip nodes and load channels?")
                        { X = 2, Y = 1, Width = Dim.Fill(4), Height = 7 });
                    var retry = new Button("Retry") { X = Pos.Center() - 12, Y = 9, IsDefault = true };
                    var abort = new Button("Load channels") { X = Pos.Center() + 4, Y = 9 };
                    retry.Clicked += () => { close(); answer.TrySetResult(true); };
                    abort.Clicked += () => { close(); answer.TrySetResult(false); };
                    dialog.KeyPress += e => { if (e.KeyEvent.Key == Key.Esc) { e.Handled = true; close(); answer.TrySetResult(false); } };
                    dialog.Add(retry, abort);
                    Application.Top.Add(dialog);
                    retry.SetFocus();
                    dialog.SetNeedsDisplay();
                });
                return await answer.Task.ConfigureAwait(false);
            }
        }
    }
}