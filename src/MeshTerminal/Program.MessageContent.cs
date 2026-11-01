using MessageBox = MeshTerminal.SafeMessageBox;
using Dialog = MeshTerminal.SafeDialog;
using System;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using MeshTerminal.Client;
using Terminal.Gui;
namespace MeshTerminal
{
    internal static partial class Program
    {
        private static void ShowMessageContent(StoredMeshMessage message)
        {
            if (message == null) return;
            var targets = MessageContentParser.Find(message.Text, _store.GetNodes(StoredNodeSort.Name));
            if (targets.Count == 0) { ShowMessageDetails(message); return; }
            var dialog = new Dialog("Message links and coordinates", 94, 19);
            var list = new ListView(targets.Select(target => target.ToString() + (target.Latitude.HasValue ? " | " + target.PositionRelation(_mesh.Device.Latitude, _mesh.Device.Longitude) : "")).ToList()) { X = 1, Y = 2, Width = Dim.Fill(2), Height = Dim.Fill(4) };
            dialog.Add(new Label("Select an item and press Enter, or double-click it.") { X = 1, Y = 0 }, list);
            MessageContentTarget mapTarget = null;
            bool showSender = false, showChess = false;
            MapOverlayPoint sharedObjectToShow = null;
            Action open = () =>
            {
                if (list.SelectedItem < 0 || list.SelectedItem >= targets.Count) return;
                var target = targets[list.SelectedItem];
                if (target.Chess != null) { showChess = true; Application.RequestStop(); return; }
                if (target.MapObject != null)
                {
                    if (ShowSharedMapObjectActions(target.MapObject, message)) { sharedObjectToShow = target.MapObject; Application.RequestStop(); }
                    return;
                }
                if (target.NodeNumbers != null) { ShowMentionedNode(target); return; }
                if (ShowMessageContentActions(target)) { mapTarget = target; Application.RequestStop(); }
            };
            list.OpenSelectedItem += e => open();
            var select = new Button("Select", true); select.Clicked += open;
            var details = new Button("Message details"); details.Clicked += () => ShowMessageDetails(message);
            var senderDetails = new Button("Node details") { Enabled = message.FromNode.HasValue };
            senderDetails.Clicked += () => { showSender = true; Application.RequestStop(); };
            var close = new Button("Close"); close.Clicked += () => Application.RequestStop();
            dialog.AddButton(select); dialog.AddButton(senderDetails); dialog.AddButton(details); dialog.AddButton(close);
            Application.Run(dialog, HandleTerminalGuiException);
            if (showChess) { ShowChessFromMessage(message); return; }
            if (showSender) { ShowMessageSenderDetails(message); return; }
            if (sharedObjectToShow != null) { ShowSharedMapObjectOnMap(sharedObjectToShow); return; }
            if (mapTarget == null) return;
            var senderNumber = message.FromNode.GetValueOrDefault();
            if (message.Direction == MessageDirection.Outgoing && _mesh.Device.MyNode != null) senderNumber = _mesh.Device.MyNode.MyNodeNum;
            var sender = String.IsNullOrWhiteSpace(message.SenderName) ? DisplayNodeName(senderNumber) : message.SenderName;
            var latitude = mapTarget.Latitude.Value;
            var longitude = mapTarget.Longitude.Value;
            var marker = new MapPositionMarker
            {
                NodeNumber = senderNumber,
                Latitude = latitude, Longitude = longitude,
                CallSign = sender,
                Color = _settings.Map.PositionMarkerColor ?? "BrightYellow",
                Symbol = _settings.Map.PositionMarkerSymbol ?? "+",
                Info = "Sender: " + sender + "\nCoordinates: " +
                    latitude.ToString("F6", CultureInfo.InvariantCulture) + ", " + longitude.ToString("F6", CultureInfo.InvariantCulture),
                UpdatedUtc = DateTime.UtcNow
            };
            if (!MapPositionMarkerProtocol.ValidSymbol(marker.Symbol)) marker.Symbol = "+";
            _nodeMap.SetPositionMarker(marker);
            _nodeMap.ShowPositionMarkers = true;
            _mapFollowGps = false;
            ShowMapPage(); RefreshMapOverlays();
            _nodeMap.RestoreView(marker.Latitude, marker.Longitude, 50);
            SaveMapViewState();
            _nodeMap.SetFocus();
        }
        private static void ShowMentionedNode(MessageContentTarget target)
        {
            var candidates = _store.GetNodes(StoredNodeSort.Name).Where(n => target.NodeNumbers.Contains(n.NodeNumber)).ToList();
            if (candidates.Count == 1) { ShowNodeDetails(candidates[0]); return; }
            if (candidates.Count == 0) { MessageBox.Query("Node details", "This node is no longer available.", "OK"); return; }
            var dialog = new Dialog("Select node: " + target.Text, 90, 18);
            var list = new ListView(candidates.Select(n => n.LongName + " | " + n.NodeId).ToList()) { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3) };
            StoredMeshNode selected = null;
            Action choose = () => { if (list.SelectedItem >= 0 && list.SelectedItem < candidates.Count) { selected = candidates[list.SelectedItem]; Application.RequestStop(); } };
            var open = new Button("Node details"); open.Clicked += choose; list.OpenSelectedItem += args => choose();
            var cancel = new Button("Cancel", true); cancel.Clicked += () => Application.RequestStop();
            dialog.Add(list); dialog.AddButton(open); dialog.AddButton(cancel); Application.Run(dialog, HandleTerminalGuiException);
            if (selected != null) ShowNodeDetails(selected);
        }

        private static void ShowMessageSenderDetails(StoredMeshMessage message)
        {
            var sender = ResolveMessageSender(message);
            if (sender != null) ShowNodeDetails(sender);
        }

        private static StoredMeshNode ResolveMessageSender(StoredMeshMessage message)
        {
            if (message == null || !message.FromNode.HasValue) return null;
            var number = message.FromNode.Value;
            var sender = _store.GetNodes(StoredNodeSort.Name).FirstOrDefault(node => node.NodeNumber == number);
            if (sender == null && _mesh.Protocol == MeshProtocol.MeshCore && message.Kind == StoredMessageKind.Channel && !String.IsNullOrWhiteSpace(message.SenderName))
            {
                var candidates = _store.GetNodes(StoredNodeSort.Name).Where(n =>
                    n.NodeId != null && n.NodeId.Length == 64 && n.NodeId.All(Uri.IsHexDigit) &&
                    String.Equals(n.LongName, message.SenderName, StringComparison.Ordinal)).ToList();
                if (candidates.Count == 1) sender = candidates[0];
                else if (candidates.Count > 1)
                {
                    var chooser = new Dialog("Select sender: " + message.SenderName, 90, 17);
                    var choices = new ListView(candidates.Select(n => n.LongName + " | " + MeshCoreNodeType(n) + " | " + n.NodeId).ToList())
                    { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3) };
                    var open = new Button("Open");
                    Action choose = () => { if (choices.SelectedItem >= 0 && choices.SelectedItem < candidates.Count) { sender = candidates[choices.SelectedItem]; Application.RequestStop(); } };
                    open.Clicked += choose; choices.OpenSelectedItem += args => choose();
                    var cancel = new Button("Cancel"); cancel.Clicked += () => Application.RequestStop();
                    chooser.Add(choices); chooser.AddButton(open); chooser.AddButton(cancel);
                    Application.Run(chooser, HandleTerminalGuiException);
                    if (sender == null) return null;
                }
            }
            if (sender == null) MessageBox.Query("Sender", "No known contact could be found for this message sender.", "OK");
            return sender;
        }

        private static bool ShowMessageContentActions(MessageContentTarget target)
        {
            var dialog = new Dialog(target.Latitude.HasValue ? "GPS coordinates" : target.IsContact ? target.ImportProtocol + " contact" : target.IsMeshCoreChannel || target.IsMeshtasticChannel ? target.ImportProtocol + " channel" : "URL", 88, 12);
            dialog.Add(new TextView { Text = target.Text + (target.Latitude.HasValue ? "\n\n" + target.PositionRelation(_mesh.Device.Latitude, _mesh.Device.Longitude) + "\nFrom your own position to these coordinates." : ""), ReadOnly = true, WordWrap = true, X = 1, Y = 1, Width = Dim.Fill(2), Height = 5 });
            var showMap = false; var importChannel = false; var importContact = false;
            var copy = new Button("Copy"); copy.Clicked += () => CopyTextWithFallback(target.Text, "Copied.");
            dialog.AddButton(copy);
            if (target.IsMeshCoreChannel || target.IsMeshtasticChannel)
            {
                var import = new Button("Import channel"); import.Clicked += () => { importChannel = true; Application.RequestStop(); };
                dialog.AddButton(import);
            }
            if (target.IsContact)
            {
                var import = new Button("Import contact"); import.Clicked += () => { importContact = true; Application.RequestStop(); }; dialog.AddButton(import);
            }
            if (target.Url != null && !target.IsMeshCoreChannel && !target.IsMeshtasticChannel && !target.IsContact)
            {
                var browser = new Button("Open browser"); browser.Clicked += () =>
                {
                    try
                    {
                        var info = Environment.OSVersion.Platform == PlatformID.Win32NT
                            ? new ProcessStartInfo(target.Url) { UseShellExecute = true }
                            : new ProcessStartInfo("xdg-open", QuoteProcessArgument(target.Url)) { UseShellExecute = false, CreateNoWindow = true };
                        using (var process = Process.Start(info)) { }
                    }
                    catch (Exception ex) { MessageBox.ErrorQuery("Browser", "Could not open the browser: " + ex.Message + "\nYou can copy the URL instead.", "OK"); }
                };
                dialog.AddButton(browser);
            }
            if (target.Latitude.HasValue)
            {
                var map = new Button("Show on map"); map.Clicked += () => { showMap = true; Application.RequestStop(); };
                dialog.AddButton(map);
            }
            var close = new Button("Close", true); close.Clicked += () => Application.RequestStop();
            dialog.AddButton(close);
            Application.Run(dialog, HandleTerminalGuiException);
            if (importChannel) ShowSharedChannelImport(target.Url, target.ImportProtocol);
            if (importContact) ShowSharedContactImport(target.Url, target.ImportProtocol);
            return showMap;
        }
    }
}