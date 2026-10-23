using MessageBox = ConsoleClient.SafeMessageBox;
using Dialog = ConsoleClient.SafeDialog;
using System;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using Meshtastic.Client;
using Terminal.Gui;
namespace ConsoleClient
{
    internal static partial class Program
    {
        private static void ShowMessageContent(StoredMeshMessage message)
        {
            if (message == null) return;
            var targets = MessageContentParser.Find(message.Text);
            if (targets.Count == 0) { ShowMessageDetails(message); return; }
            var dialog = new Dialog("Message links and coordinates", 94, 19);
            var list = new ListView(targets.Select(target => target.ToString()).ToList()) { X = 1, Y = 2, Width = Dim.Fill(2), Height = Dim.Fill(4) };
            dialog.Add(new Label("Select an item and press Enter, or double-click it.") { X = 1, Y = 0 }, list);
            MessageContentTarget mapTarget = null;
            Action open = () =>
            {
                if (list.SelectedItem < 0 || list.SelectedItem >= targets.Count) return;
                var target = targets[list.SelectedItem];
                if (ShowMessageContentActions(target)) { mapTarget = target; Application.RequestStop(); }
            };
            list.OpenSelectedItem += e => open();
            var select = new Button("Select", true); select.Clicked += open;
            var details = new Button("Message details"); details.Clicked += () => ShowMessageDetails(message);
            var close = new Button("Close"); close.Clicked += () => Application.RequestStop();
            dialog.AddButton(select); dialog.AddButton(details); dialog.AddButton(close);
            Application.Run(dialog, HandleTerminalGuiException);
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
        private static bool ShowMessageContentActions(MessageContentTarget target)
        {
            var dialog = new Dialog(target.Latitude.HasValue ? "GPS coordinates" : target.IsContact ? target.ImportProtocol + " contact" : target.IsMeshCoreChannel || target.IsMeshtasticChannel ? target.ImportProtocol + " channel" : "URL", 88, 12);
            dialog.Add(new TextView { Text = target.Text, ReadOnly = true, WordWrap = true, X = 1, Y = 1, Width = Dim.Fill(2), Height = 5 });
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