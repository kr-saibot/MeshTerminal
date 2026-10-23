using System;
using System.Globalization;
using System.Linq;
using Meshtastic.Client;

namespace ConsoleClient
{
    internal static partial class Program
    {
        // Called on the database queue after storing the original chat message.
        private static void ReceiveLocPosition(MeshMessage message, DateTime receivedUtc)
        {
            Meshtastic.Protobufs.Position position;
            if (!LocMessagePosition.TryParse(message.Text, out position)) return;
            var positionNode = message.Protocol == MeshProtocol.MeshCore ? _store.ResolveMeshCorePositionNode(message.From) : message.From;
            if (_settings.StorePositionData && (!_settings.StorePositionFavoritesOnly || IsFavoriteNode(positionNode)))
            {
                _store.AddPosition(positionNode, position, receivedUtc);
                RefreshTelemetryViews(positionNode, true);
            }
            if (!_settings.Map.ReceivePositionMarkers) return;
            var node = _mesh.Nodes.FirstOrDefault(n => n.Number == positionNode);
            var sender = !String.IsNullOrWhiteSpace(message.SenderName) ? message.SenderName : node != null ? node.LongName : DisplayNodeName(message.From);
            var latitude = position.LatitudeI / 10000000d;
            var longitude = position.LongitudeI / 10000000d;
            var marker = new MapPositionMarker
            {
                NodeNumber = positionNode,
                Identity = message.Protocol == MeshProtocol.MeshCore && node != null ? node.Id : null,
                Latitude = latitude, Longitude = longitude, CallSign = sender,
                Symbol = _settings.Map.PositionMarkerSymbol ?? "+", Color = _settings.Map.PositionMarkerColor ?? "BrightYellow",
                Info = "Sender: " + sender + "\nCoordinates: " + latitude.ToString("F6", CultureInfo.InvariantCulture) + ", " + longitude.ToString("F6", CultureInfo.InvariantCulture),
                UpdatedUtc = receivedUtc
            };
            if (!MapPositionMarkerProtocol.ValidSymbol(marker.Symbol)) marker.Symbol = "+";
            Ui(delegate { if (_nodeMap != null) _nodeMap.SetPositionMarker(marker); });
        }
    }
}
