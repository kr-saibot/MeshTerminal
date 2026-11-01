using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MeshTerminal.Client;
using Terminal.Gui;
using Dialog = MeshTerminal.SafeDialog;
using MessageBox = MeshTerminal.SafeMessageBox;
namespace MeshTerminal
{
    internal static partial class Program
    {
        private static MapOverlayFile FindSharedObjectFile(string uuid)
        {
            return MapOverlayFiles.FirstOrDefault(f => f.Data?.Places != null && f.Data.Places.Any(p => String.Equals(p.Uuid, uuid, StringComparison.OrdinalIgnoreCase)));
        }
        private static bool ShowSharedMapObjectActions(MapOverlayPoint point, StoredMeshMessage message)
        {
            var file = FindSharedObjectFile(point.Uuid);
            var dialog = new Dialog("Shared map object", 90, 20);
            var text = "Name: " + point.ShortName + "\nShape: " + point.Shape + "\nDescription: " + point.Description +
                "\nPosition: " + InsertionCoordinates(point.Latitude, point.Longitude) +
                (point.ReferenceLatitude.HasValue ? "\nReference: " + InsertionCoordinates(point.ReferenceLatitude.Value, point.ReferenceLongitude.Value) : "") +
                "\nPriority: " + (point.Priority.HasValue ? point.Priority.Value.ToString(CultureInfo.InvariantCulture) : "not set") +
                "\nColor: " + point.Color + " | Fill: " + point.EffectiveFillDensity +
                "\nUUID: " + point.Uuid + "\n\n" + (file == null ? "Not stored locally (deleted or not imported). Restore from this message to show it." : "Stored in: " + file.DisplayName);
            dialog.Add(new TextView { Text = text, ReadOnly = true, WordWrap = true, AllowsTab = false, X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3) });
            bool show = false;
            var open = new Button(file == null ? "Restore and show" : "Show on map");
            open.Clicked += () =>
            {
                try
                {
                    if (FindSharedObjectFile(point.Uuid) == null) RestoreSharedMapObject(point, message);
                    show = true; Application.RequestStop();
                }
                catch (Exception ex) { MessageBox.ErrorQuery("Shared map object", ex.Message, "OK"); }
            };
            var copy = new Button("Copy message"); copy.Clicked += () => CopyTextWithFallback(message.Text, "Map message copied.");
            var close = new Button("Close", true); close.Clicked += () => Application.RequestStop();
            dialog.AddButton(open); dialog.AddButton(copy); dialog.AddButton(close);
            Application.Run(dialog, HandleTerminalGuiException);
            return show;
        }
        private static void RestoreSharedMapObject(MapOverlayPoint point, StoredMeshMessage message)
        {
            if (FindSharedObjectFile(point.Uuid) != null) return;
            var target = MapOverlayFiles.FirstOrDefault(f => f.Data != null && !f.Data.WriteProtected && String.Equals(Path.GetFileName(f.FileName), _settings.Map.MapPointSharingOverlayFile, StringComparison.OrdinalIgnoreCase));
            if (target == null) target = MapOverlayFiles.FirstOrDefault(f => f.Data != null && !f.Data.WriteProtected &&
                f.Data.Name == "Restored map objects" && Path.GetFileName(f.FileName).StartsWith("restored-map-items", StringComparison.OrdinalIgnoreCase));
            bool newFile = target == null;
            if (newFile)
            {
                var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "map");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "restored-map-items.xml");
                int suffix = 2;
                while (File.Exists(path)) path = Path.Combine(directory, "restored-map-items-" + suffix++ + ".xml");
                target = new MapOverlayFile { FileName = path, DisplayName = "Restored map objects", Data = new MapOverlayData { Name = "Restored map objects" } };
            }
            if (target.Data.Places == null) target.Data.Places = new List<MapOverlayPoint>();
            point.Creator = !String.IsNullOrWhiteSpace(message.SenderName) ? message.SenderName : FormatMapPointCreator(message.FromNode.GetValueOrDefault());
            point.SourceFile = target.FileName; point.OverlayName = target.Data.Name; point.Selectable = target.Data.Selectable;
            if (!SharedMapObjectProtocol.Restore(target.Data, point)) return;
            try { SaveMapOverlayFile(target); }
            catch { target.Data.Places.Remove(point); throw; }
            if (newFile) MapOverlayFiles.Add(target);
            target.Enabled = true;
            SaveMapOverlayState();
        }
        private static void ShowSharedMapObjectOnMap(MapOverlayPoint original)
        {
            var file = FindSharedObjectFile(original.Uuid);
            if (file == null) { MessageBox.Query("Map object", "The object is no longer available. Open the message again to restore it.", "OK"); return; }
            var point = file.Data.Places.First(p => String.Equals(p.Uuid, original.Uuid, StringComparison.OrdinalIgnoreCase));
            file.Enabled = true; _mapFollowGps = false;
            ShowMapPage(); SaveMapOverlayState(); RefreshMapOverlays();
            double latitude = point.Latitude, longitude = point.Longitude, scale = 50;
            if (point.ReferenceLatitude.HasValue && point.ReferenceLongitude.HasValue)
            {
                double lat2 = point.ReferenceLatitude.Value, lon2 = point.ReferenceLongitude.Value;
                var distance = MeshtasticClient.GetDistanceMeters(latitude, longitude, lat2, lon2).GetValueOrDefault();
                if (point.Shape != "Circle")
                {
                    latitude = (latitude + lat2) / 2;
                    var delta = ((lon2 - longitude + 540) % 360) - 180;
                    longitude = ((longitude + delta / 2 + 540) % 360) - 180;
                }
                var rows = Math.Max(4, Math.Min(_nodeMap.Frame.Height - 4, (_nodeMap.Frame.Width - 4) / 2));
                scale = Math.Max(5, distance * (point.Shape == "Circle" ? 2 : 1) / (rows * 0.7));
            }
            _nodeMap.RestoreView(latitude, longitude, scale);
            SaveMapViewState(); _nodeMap.SetFocus();
        }
    }
}
