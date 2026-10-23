using System;
using System.Globalization;
using System.Text;

namespace MeshTerminal
{
    internal sealed class MapPositionMarker
    {
        public uint NodeNumber;
        public string Identity;
        public double Latitude, Longitude;
        public string Color = "BrightYellow", Symbol = "+", CallSign = "", Info = "";
        public DateTime UpdatedUtc;
    }

    internal static class MapPositionMarkerProtocol
    {
        public const string Prefix = "///MAPPOS+";
        public static readonly string[] Colors = { "Black", "Blue", "Green", "Cyan", "Red", "Magenta", "Brown", "Gray", "DarkGray", "BrightBlue", "BrightGreen", "BrightCyan", "BrightRed", "BrightMagenta", "BrightYellow", "White" };
        private static string Encode(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? "")).TrimEnd('='); }
        private static string Decode(string value) { return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '='))); }
        public static bool ValidSymbol(string symbol)
        {
            if (String.IsNullOrEmpty(symbol) || new StringInfo(symbol).LengthInTextElements != 1) return false;
            // One BMP character keeps the marker exactly one terminal cell wide.
            return symbol.Length == 1 && !Char.IsControl(symbol[0]) && !Char.IsSurrogate(symbol[0]) &&
                CharUnicodeInfo.GetUnicodeCategory(symbol, 0) != UnicodeCategory.NonSpacingMark &&
                CharUnicodeInfo.GetUnicodeCategory(symbol, 0) != UnicodeCategory.Format &&
                symbol[0] < 0x2e80;
        }
        public static string Build(MapPositionMarker marker)
        {
            var color = Array.FindIndex(Colors, value => String.Equals(value, marker.Color, StringComparison.OrdinalIgnoreCase));
            return Prefix + (String.IsNullOrEmpty(marker.Identity) ? "2|" + marker.NodeNumber.ToString("x8") : "3|" + marker.Identity) + "|" +
                marker.Latitude.ToString("F6", CultureInfo.InvariantCulture) + "|" + marker.Longitude.ToString("F6", CultureInfo.InvariantCulture) + "|" +
                Math.Max(0, color).ToString("x", CultureInfo.InvariantCulture) + "|" + Encode(marker.Symbol) + "|" + Encode(marker.CallSign) + "|" + Encode(marker.Info);
        }
        public static bool TryParse(string text, uint sender, out MapPositionMarker marker, Func<string, uint> identityResolver = null)
        {
            marker = null;
            if (text == null || !text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            var parts = text.Substring(Prefix.Length).Split('|');
            uint node; double lat, lon; int color;
            var meshCore = parts.Length == 8 && parts[0] == "3";
            if (meshCore)
            {
                if (identityResolver == null || parts[1].Length != 64) return false;
                foreach (var digit in parts[1]) if (!Uri.IsHexDigit(digit)) return false;
                node = identityResolver(parts[1].ToLowerInvariant());
            }
            else
            {
                if (parts.Length < 2 || !UInt32.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out node) || node == 0 || node != sender) return false;
            }
            if (!((parts.Length == 7 && parts[0] == "1") || (parts.Length == 8 && parts[0] == "2") || meshCore) ||

                !Double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out lat) || Double.IsNaN(lat) || Double.IsInfinity(lat) || lat < -90 || lat > 90 ||
                !Double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out lon) || Double.IsNaN(lon) || Double.IsInfinity(lon) || lon < -180 || lon > 180 ||
                !Int32.TryParse(parts[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color) || color < 0 || color >= Colors.Length) return false;
            try
            {
                var symbol = Decode(parts[5]); var callSign = Decode(parts[6]).Trim();
                var info = parts.Length == 8 ? Decode(parts[7]) : "";
                if (!ValidSymbol(symbol) || String.IsNullOrWhiteSpace(callSign) || callSign.Length > 80 ||
                    callSign.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0) return false;
                marker = new MapPositionMarker { NodeNumber = node, Identity = meshCore ? parts[1].ToLowerInvariant() : null, Latitude = lat, Longitude = lon, Color = Colors[color], Symbol = symbol, CallSign = callSign, Info = info, UpdatedUtc = DateTime.UtcNow };
                return true;
            }
            catch { return false; }
        }
    }
}

namespace MeshTerminal
{
    internal static class LocMessagePosition
    {
        private static readonly System.Text.RegularExpressions.Regex Pattern = new System.Text.RegularExpressions.Regex(
            @"\A\s*\[LOC\]\s*(?<lat>[+-]?\d{1,3}(?:\.\d+)?)\s*,\s*(?<lon>[+-]?\d{1,3}(?:\.\d+)?)\s*\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        internal static bool TryParse(string text, out Meshtastic.Protobufs.Position position)
        {
            position = null;
            if (text == null) return false;
            var match = Pattern.Match(text);
            double lat, lon;
            if (!match.Success || !Double.TryParse(match.Groups["lat"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lat) ||
                !Double.TryParse(match.Groups["lon"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lon) ||
                Double.IsNaN(lat) || Double.IsInfinity(lat) || Double.IsNaN(lon) || Double.IsInfinity(lon) || Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return false;
            position = new Meshtastic.Protobufs.Position { LatitudeI = (int)Math.Round(lat * 10000000, MidpointRounding.AwayFromZero), LongitudeI = (int)Math.Round(lon * 10000000, MidpointRounding.AwayFromZero) };
            return true;
        }
    }
}
