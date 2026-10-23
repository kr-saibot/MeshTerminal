using System;
using Meshtastic.Client;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
namespace ConsoleClient
{
    internal sealed class MessageContentTarget
    {
        internal string Text, Url;
        internal bool IsMeshCoreChannel, IsMeshtasticChannel, IsContact;
        internal MeshProtocol ImportProtocol;
        internal double? Latitude, Longitude;
        internal int Offset;
        public override string ToString() { return (Latitude.HasValue ? "GPS: " : IsContact ? ImportProtocol + " contact: " : IsMeshCoreChannel || IsMeshtasticChannel ? ImportProtocol + " channel: " : "URL: ") + Text; }
    }
    internal static class MessageContentParser
    {
        private static readonly Regex Links = new Regex(@"\b(?:https?://|meshcore://|www\.)[^\s<>""']+", RegexOptions.IgnoreCase);
        private static readonly Regex Coordinates = new Regex(@"(?<![\w.])(?<lat>[+-]?\d{1,3}[.,]\d+)\s*(?:[,;]|\s+)\s*(?<lon>[+-]?\d{1,3}[.,]\d+)(?![\w.])");
        internal static List<MessageContentTarget> Find(string text)
        {
            text = text ?? "";
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
            return targets.OrderBy(target => target.Offset).ToList();
        }
    }
}