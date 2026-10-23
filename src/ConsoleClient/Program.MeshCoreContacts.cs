using MessageBox = ConsoleClient.SafeMessageBox;
using Dialog = ConsoleClient.SafeDialog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Client;
using NStack;
using Terminal.Gui;

namespace ConsoleClient
{
    internal static partial class Program
    {
        private static string MeshCoreNodeType(StoredMeshNode node)
        {
            var type = _mesh.Nodes.FirstOrDefault(n => n.Number == node.NodeNumber)?.MeshCoreContactType ?? node.MeshCoreContactType;
            return type == 1 ? "Companion" : type == 2 ? "Repeater" : type == 3 ? "Roomserver" : type == 4 ? "Sensor" : "Unknown";
        }
        private static int _meshCoreContactFilter;
        private static string MeshCoreContactFilterLabel { get { return _meshCoreContactFilter == 1 ? "Contacts: PC" : _meshCoreContactFilter == 2 ? "Contacts: DEV" : "Contacts: all"; } }
        private static bool MatchesMeshCoreContactFilter(StoredMeshNode node)
        {
            if (_mesh.Protocol != MeshProtocol.MeshCore || _meshCoreContactFilter == 0) return true;
            if (_mesh.Device.MyNode != null && node.NodeNumber == _mesh.Device.MyNode.MyNodeNum) return false;
            var saved = _mesh.IsMeshCoreContactSaved(node.NodeId);
            return _meshCoreContactFilter == 1 ? saved == false : saved == true;
        }
        private static string MeshCoreContactTag(StoredMeshNode node)
        {
            if (_mesh.Device.MyNode != null && node.NodeNumber == _mesh.Device.MyNode.MyNodeNum) return "[OWN]";
            var saved = _mesh.IsMeshCoreContactSaved(node.NodeId);
            return saved == true ? "[DEV]" : saved == false ? "[PC]" : "[?]";
        }
        private static string FormatNodeAge(DateTime time)
        {
            if (time == DateTime.MinValue) return "-";
            var age = DateTime.UtcNow - time.ToUniversalTime();
            return age.TotalDays >= 1 ? Math.Floor(age.TotalDays).ToString(CultureInfo.InvariantCulture) + "d"
                : age.TotalHours >= 1 ? Math.Floor(age.TotalHours).ToString(CultureInfo.InvariantCulture) + "h"
                : Math.Max(0, Math.Floor(age.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + "m";
        }
        private static string MeshCoreContactStatus(StoredMeshNode node)
        {
            if (_mesh.Device.MyNode != null && node.NodeNumber == _mesh.Device.MyNode.MyNodeNum) return "Own device";
            var saved = _mesh.IsMeshCoreContactSaved(node.NodeId);
            return saved == true ? "In device" : saved == false ? "PC only" : "Not checked";
        }
        private static void RunMeshCoreContactOperation(Func<CancellationToken, Task> operation)
        {
            if (!_mesh.IsConnected || _mesh.Protocol != MeshProtocol.MeshCore) { MessageBox.ErrorQuery("Device contacts", "Connect to MeshCore first.", "OK"); return; }
            if (Interlocked.CompareExchange(ref _deviceRefreshActive, 1, 0) != 0) { MessageBox.Query("Device contacts", "A device operation is already active.", "OK"); return; }
            var cancellation = new CancellationTokenSource();
            ShowPleaseWait("Updating device contacts...", () => cancellation.Cancel());
            Task.Run(async () =>
            {
                Exception error = null;
                try { await operation(cancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { error = ex; }
                finally
                {
                    Ui(() => { HidePleaseWait(); cancellation.Dispose(); Interlocked.Exchange(ref _deviceRefreshActive, 0); RefreshNodePage(true); if (error != null) MessageBox.ErrorQuery("Device contacts", error.Message, "OK"); });
                }
            });
        }
        private static void RemoveAllMeshCoreDeviceContacts()
        {
            if (MessageBox.Query("Remove all device contacts", "Remove ALL contacts from the connected MeshCore device?\nPC records and chats remain. Automatic adding can add contacts again.", "Remove all", "Cancel") == 0)
                RunMeshCoreContactOperation(async token => { await _mesh.RemoveAllMeshCoreContactsAsync(token, (count, total) => Ui(() => ShowPleaseWait("Removing device contacts: " + count + " / " + total))); });
        }
        private static void RemoveMeshCoreDeviceContact(StoredMeshNode node)
        {
            if (MessageBox.Query("Remove device contact", "Remove " + (node.LongName ?? node.NodeId) + " from the device?\nPC records and chats remain. Automatic adding can add it again.", "Remove", "Cancel") == 0)
                RunMeshCoreContactOperation(token => _mesh.RemoveMeshCoreContactAsync(node.NodeId, token));
        }
        private static int ShowNodeDetailsDialog(string text, List<string> labels)
        {
            var width = Math.Min(110, Application.Top.Frame.Width - 2);
            var dialog = new Dialog("Node details", width, Math.Min(text.Split('\n').Length + 9, Application.Top.Frame.Height - 2));
            var info = new TextView { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(6), ReadOnly = true, WordWrap = true, Text = text };
            dialog.Add(info);
            var result = labels.Count - 1;
            Button close = null;
            var split = (labels.Count + 1) / 2;
            for (var row = 0; row < 2; row++)
            {
                var start = row == 0 ? 0 : split; var end = row == 0 ? split : labels.Count;
                var total = Enumerable.Range(start, end - start).Sum(i => labels[i].Length + 6);
                var x = Math.Max(1, (width - 2 - total) / 2);
                for (var index = start; index < end; index++)
                {
                    var chosen = index;
                    var button = new Button(labels[index], index == labels.Count - 1) { X = x, Y = Pos.AnchorEnd(row == 0 ? 4 : 2) };
                    button.Clicked += () => { result = chosen; Application.RequestStop(); };
                    dialog.Add(button);
                    if (index == labels.Count - 1) close = button;
                    x += labels[index].Length + 6;
                }
            }
            dialog.Ready += () => close.SetFocus();
            dialog.KeyPress += e => { if (e.KeyEvent.Key == Key.Esc) { result = labels.Count - 1; e.Handled = true; Application.RequestStop(); } };
            Application.Run(dialog, HandleTerminalGuiException);
            return result;
        }
        private static void EditMeshCoreContact(StoredMeshNode node)
        {
            var dialog = new Dialog(node == null ? "New MeshCore contact" : "Save MeshCore contact to device", 90, 18);
            var name = new TextField(node?.LongName ?? "") { X = 14, Y = 1, Width = Dim.Fill(2) };
            var key = new TextField(node?.NodeId ?? "") { X = 1, Y = 4, Width = Dim.Fill(2) };
            var live = node == null ? null : _mesh.Nodes.FirstOrDefault(n => n.Number == node.NodeNumber);
            var type = new RadioGroup(new Rect(14, 6, 28, 4), new ustring[] { "Chat / Companion", "Repeater", "Room server", "Sensor" }) { SelectedItem = Math.Max(0, Math.Min(3, (live?.MeshCoreContactType ?? node?.MeshCoreContactType ?? 1) - 1)) };
            var latitude = new TextField(node?.Latitude?.ToString("F6", CultureInfo.InvariantCulture) ?? "") { X = 14, Y = 11, Width = 22 };
            var longitude = new TextField(node?.Longitude?.ToString("F6", CultureInfo.InvariantCulture) ?? "") { X = 54, Y = 11, Width = 22 };
            dialog.Add(new Label("Name:") { X = 1, Y = 1 }, name, new Label("Full public key (64 hexadecimal characters):") { X = 1, Y = 3 }, key,
                new Label("Type:") { X = 1, Y = 6 }, type, new Label("Latitude:") { X = 1, Y = 11 }, latitude, new Label("Longitude:") { X = 41, Y = 11 }, longitude,
                new Label("Coordinates are optional. For a PC-only node, check that the selected type is correct.") { X = 1, Y = 13, Width = Dim.Fill(2) });
            string chosenKey = null, chosenName = null; byte chosenType = 1; double? lat = null, lon = null;
            var save = new Button("Save to device", true); save.Clicked += () =>
            {
                double parsedLat, parsedLon; var latText = latitude.Text.ToString().Trim(); var lonText = longitude.Text.ToString().Trim();
                if ((latText.Length == 0) != (lonText.Length == 0) || (latText.Length > 0 && (!Double.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedLat) || !Double.TryParse(lonText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedLon))))
                { MessageBox.ErrorQuery("Contact", "Enter both coordinates as decimal numbers, or leave both empty.", "OK"); return; }
                if (latText.Length > 0) { lat = Double.Parse(latText, CultureInfo.InvariantCulture); lon = Double.Parse(lonText, CultureInfo.InvariantCulture); }
                chosenKey = key.Text.ToString().Trim(); chosenName = name.Text.ToString().Trim(); chosenType = (byte)(type.SelectedItem + 1); Application.RequestStop();
            };
            var cancel = new Button("Cancel"); cancel.Clicked += () => Application.RequestStop();
            dialog.AddButton(save); dialog.AddButton(cancel); Application.Run(dialog, HandleTerminalGuiException);
            if (chosenKey != null) RunMeshCoreContactOperation(token => _mesh.SaveMeshCoreContactAsync(chosenKey, chosenName, chosenType, lat, lon, token));
        }
    }
}