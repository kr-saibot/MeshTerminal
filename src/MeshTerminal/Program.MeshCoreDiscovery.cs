using MessageBox = MeshTerminal.SafeMessageBox;
using Dialog = MeshTerminal.SafeDialog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeshTerminal.Client;
using Terminal.Gui;
namespace MeshTerminal
{
    internal static partial class Program
    {
        private static string RepeaterDiscoveryDetails(MeshCoreRepeaterDiscoveryResult result)
        {
            if (result == null) return "Select a repeater to see its signal measurements.";
            return "Name: " + result.Name + "\nPublic key: " + result.PublicKey + "\nHops: 0 (direct)\nRSSI at this device: " + result.Rssi + " dBm\nSNR at this device: " + result.ReceivedSnr.ToString("F1", CultureInfo.InvariantCulture) + " dB\nSNR at repeater: " + result.UplinkSnr.ToString("F1", CultureInfo.InvariantCulture) + " dB";
        }
        private static void ShowMeshCoreRepeaterDiscovery()
        {
            if (_mesh.Protocol != MeshProtocol.MeshCore || !_mesh.IsConnected || _mesh.Device.MyNode == null) { MessageBox.Query("Repeater search", "Connect and synchronize MeshCore first.", "OK"); return; }
            var dialog = new Dialog("Find nearby MeshCore repeaters", 100, 27);
            var results = new List<MeshCoreRepeaterDiscoveryResult>();
            var list = new ListView(new List<string>()) { X = 1, Y = 3, Width = Dim.Fill(2), Height = 9 };
            var details = new TextView { X = 1, Y = 13, Width = Dim.Fill(2), Height = 7, ReadOnly = true, WordWrap = true, Text = RepeaterDiscoveryDetails(null) };
            var status = new Label("") { X = 1, Y = 21, Width = Dim.Fill(2) };
            var search = new Button("Search again");
            var closed = false; var busy = false; CancellationTokenSource cancellation = null;
            Action showSelected = () => { details.Text = RepeaterDiscoveryDetails(list.SelectedItem >= 0 && list.SelectedItem < results.Count ? results[list.SelectedItem] : null); };
            list.SelectedItemChanged += args => showSelected();
            Action start = async delegate
            {
                if (busy) return;
                busy = true; search.Enabled = false; results.Clear(); list.SetSource(new List<string>()); showSelected();
                cancellation = new CancellationTokenSource();
                var requestCancellation = cancellation;
                status.Text = "Searching for 10 seconds...";
                string error = null;
                try
                {
                    await Task.Run(() => _mesh.DiscoverMeshCoreRepeatersAsync(TimeSpan.FromSeconds(10), result => Ui(() =>
                    {
                        if (closed) return;
                        var selectedKey = list.SelectedItem >= 0 && list.SelectedItem < results.Count ? results[list.SelectedItem].PublicKey : null;
                        var index = results.FindIndex(item => item.PublicKey == result.PublicKey);
                        if (index >= 0) results[index] = result; else results.Add(result);
                        list.SetSource(results.Select(item => item.Name + " [" + item.PublicKey.Substring(0, 8) + "] | RSSI " + item.Rssi + " dBm | SNR " + item.ReceivedSnr.ToString("F1", CultureInfo.InvariantCulture) + " dB | uplink " + item.UplinkSnr.ToString("F1", CultureInfo.InvariantCulture) + " dB").ToList());
                        if (results.Count > 0) list.SelectedItem = Math.Max(0, results.FindIndex(item => item.PublicKey == selectedKey));
                        showSelected(); status.Text = "Searching... " + results.Count + " repeater(s) found.";
                    }), requestCancellation.Token));
                }
                catch (OperationCanceledException) { error = "Search cancelled."; }
                catch (Exception ex) { error = ex.Message; }
                finally { requestCancellation.Dispose(); }
                Ui(() => { if (closed) return; busy = false; search.Enabled = true; status.Text = error ?? (results.Count > 0 ? "Search complete: " + results.Count + " repeater(s)." : "No replies. Check radio settings; older firmware or repeated searches may not respond."); });
            };
            search.Clicked += start;
            var copy = new Button("Copy results"); copy.Clicked += () => CopyTextWithFallback(String.Join("\n\n", results.Select(RepeaterDiscoveryDetails)), "Search results copied.");
            var close = new Button("Close", true); close.Clicked += () => Application.RequestStop();
            dialog.Add(new Label("Searches directly reachable repeaters on the current radio settings.") { X = 1, Y = 1 }, new Label("Names come from known contacts. Results are not automatically added as contacts.") { X = 1, Y = 2 }, list, details, status);
            dialog.AddButton(search); dialog.AddButton(copy); dialog.AddButton(close); dialog.Ready += start;
            try { Application.Run(dialog, HandleTerminalGuiException); }
            finally { closed = true; if (busy && cancellation != null) { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } } }
        }
    }
}
