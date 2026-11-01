using System;

using MeshTerminal.Client;

using System.Collections.Generic;

using System.Globalization;

using System.Linq;

using System.Text.RegularExpressions;

namespace MeshTerminal

{

    internal sealed class MessageContentTarget

    {

        internal string Text, Url;

        internal bool IsMeshCoreChannel, IsMeshtasticChannel, IsContact;

        internal MeshProtocol ImportProtocol;

        internal double? Latitude, Longitude;

        internal int Offset;

        internal List<uint> NodeNumbers;
        internal MapOverlayPoint MapObject;
        internal ChessPosition Chess;

        internal string PositionRelation(double? ownLatitude, double? ownLongitude)

        {

            var distance = MeshtasticClient.GetDistanceMeters(ownLatitude, ownLongitude, Latitude, Longitude);

            var bearing = MeshtasticClient.GetInitialBearingDegrees(ownLatitude, ownLongitude, Latitude, Longitude);

            if (!distance.HasValue) return "Distance / direction: own position unavailable";

            var length = distance.Value < 1000 ? Math.Round(distance.Value).ToString(CultureInfo.InvariantCulture) + " m" : (distance.Value / 1000d).ToString("F1", CultureInfo.InvariantCulture) + " km";

            return "Distance: " + length + " | Direction: " + (distance.Value < 1 ? "same position" : bearing.HasValue ? Math.Round(bearing.Value).ToString(CultureInfo.InvariantCulture) + "° " + MeshtasticClient.GetCompassDirection(bearing.Value) : "unavailable");

        }

        public override string ToString() { return Chess != null ? "Chess: " + (String.IsNullOrEmpty(Chess.Notation) ? "Show game" : Chess.Notation) : MapObject != null ? "Map object: " + MapObject.Shape + " | " + MapObject.ShortName : (NodeNumbers != null ? "Node: " : Latitude.HasValue ? "GPS: " : IsContact ? ImportProtocol + " contact: " : IsMeshCoreChannel || IsMeshtasticChannel ? ImportProtocol + " channel: " : "URL: ") + Text; }

    }

    internal static class MessageContentParser

    {

        private static readonly Regex Links = new Regex(@"\b(?:https?://|meshcore://|www\.)[^\s<>""']+", RegexOptions.IgnoreCase);

        private static readonly Regex Coordinates = new Regex(@"(?<![\w.])(?<lat>[+-]?\d{1,3}[.,]\d+)\s*(?:[,;]|\s+)\s*(?<lon>[+-]?\d{1,3}[.,]\d+)(?![\w.])");

        internal static List<MessageContentTarget> Find(string text, IEnumerable<StoredMeshNode> knownNodes = null)

        {

            text = text ?? "";
            ChessPosition chess;
            if (ChessProtocol.TryParse(text, out chess))
                return new List<MessageContentTarget> { new MessageContentTarget { Text = text, Chess = chess } };
            MapOverlayPoint sharedObject;
            if (SharedMapObjectProtocol.TryParse(text, out sharedObject))
                return new List<MessageContentTarget> { new MessageContentTarget { Text = text, MapObject = sharedObject, Offset = 0 } };


            var targets = new List<MessageContentTarget>();

            foreach (Match match in Links.Matches(text))

            {

                var value = match.Value.TrimEnd('.', ',', ';', '!', '?');

                while (value.EndsWith(")") && value.Count(c => c == ')') > value.Count(c => c == '(')) value = value.Substring(0, value.Length - 1);

                while (value.EndsWith("]") && value.Count(c => c == ']') > value.Count(c => c == '[')) value = value.Substring(0, value.Length - 1);

                var url = value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + value : value;

                if (url.StartsWith("meshcore://channel/add?", StringComparison.OrdinalIgnoreCase))

                { targets.Add(new MessageContentTarget { Text = value, Url = url, Offset = match.Index, IsMeshCoreChannel = true, ImportProtocol = MeshProtocol.MeshCore }); continue; }

                if (url.StartsWith("meshcore://contact/add?", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(url, @"\Ameshcore://[0-9a-fA-F]{200,510}\z", RegexOptions.IgnoreCase))

                { targets.Add(new MessageContentTarget { Text = value, Url = url, Offset = match.Index, IsContact = true, ImportProtocol = MeshProtocol.MeshCore }); continue; }

                Uri uri;

                if (Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https") && !String.IsNullOrWhiteSpace(uri.Host))

                    targets.Add(new MessageContentTarget { Text = value, Url = url, Offset = match.Index,

                        IsMeshtasticChannel = uri.Host.Equals("meshtastic.org", StringComparison.OrdinalIgnoreCase) && (uri.AbsolutePath.TrimEnd('/') == "/e" || uri.AbsolutePath.TrimEnd('/') == "/d") && uri.Fragment.Length > 1,

                        IsContact = uri.Host.Equals("meshtastic.org", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.TrimEnd('/') == "/v" && uri.Fragment.Length > 1, ImportProtocol = MeshProtocol.Meshtastic });

            }

            foreach (Match match in Coordinates.Matches(text))

            {

                double lat, lon;

                if (!Double.TryParse(match.Groups["lat"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) ||

                    !Double.TryParse(match.Groups["lon"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out lon) ||

                    Math.Abs(lat) > 90 || Math.Abs(lon) > 180) continue;

                targets.Add(new MessageContentTarget { Text = match.Value, Latitude = lat, Longitude = lon, Offset = match.Index });

            }

            if (knownNodes != null)

            {

                foreach (var group in knownNodes.Where(n => !String.IsNullOrWhiteSpace(n.LongName) && n.LongName.Trim().Length >= 2)

                    .GroupBy(n => n.LongName.Trim(), StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Key.Length))

                {

                    var pattern = @"(?<![\p{L}\p{N}_])" + Regex.Escape(group.Key) + @"(?![\p{L}\p{N}_])";

                    foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))

                    {

                        if (targets.Any(t => match.Index < t.Offset + t.Text.Length && t.Offset < match.Index + match.Length)) continue;

                        targets.Add(new MessageContentTarget { Text = match.Value, Offset = match.Index, NodeNumbers = group.Select(n => n.NodeNumber).Distinct().ToList() });

                    }

                }

            }

            return targets.OrderBy(target => target.Offset).ToList();

        }

    }

}