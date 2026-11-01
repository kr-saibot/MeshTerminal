using System;
using System.Globalization;
using System.Text;
using System.Collections.Generic;
using System.Linq;
namespace MeshTerminal
{
    internal static class SharedMapObjectProtocol
    {
        internal const string Prefix = "///MAP+";
        private static string DecodeSharedMapText(string value)
        {
            value = value ?? "";
            return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '=')));
        }
        internal static bool Restore(MapOverlayData target, MapOverlayPoint point)
        {
            if (target == null || point == null) throw new ArgumentNullException();
            if (target.WriteProtected) throw new InvalidOperationException("This map layer is write-protected.");
            Guid uuid;
            if (!Guid.TryParse(point.Uuid, out uuid)) throw new ArgumentException("Invalid map object UUID.");
            if (target.Places == null) target.Places = new List<MapOverlayPoint>();
            if (target.Places.Any(p => String.Equals(p.Uuid, point.Uuid, StringComparison.OrdinalIgnoreCase))) return false;
            target.Places.Add(point);
            return true;
        }
        internal static bool TryParse(string text, out MapOverlayPoint point)
        {
            point = null;
            if (String.IsNullOrWhiteSpace(text) || !text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            var parts = text.Substring(Prefix.Length).Split('|');
            Guid uuid;
            double latitude, longitude;
            if ((parts.Length != 12 && parts.Length != 13) || parts[0] != "1") return false;
            if (!Guid.TryParse(parts[1], out uuid) ||
                !Double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) ||
                !Double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude) ||
                Double.IsNaN(latitude) || Double.IsInfinity(latitude) || Double.IsNaN(longitude) || Double.IsInfinity(longitude) || latitude < -90d || latitude > 90d || longitude < -180d || longitude > 180d) return false;
            try
            {
                long? priority = null;
                if (parts.Length == 13 && parts[12].Length > 0)
                {
                    long value; if (!Int64.TryParse(parts[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return false;
                    priority = value;
                }
                int colorCode, shapeCode, fillDensity;
                if (!Int32.TryParse(parts[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out colorCode) || colorCode < 0 || colorCode > 15 || !Int32.TryParse(parts[5], out shapeCode) || shapeCode < 0 || shapeCode > 3 || !Int32.TryParse(parts[6], out fillDensity) || fillDensity < 0 || fillDensity > 9) return false;
                var colors = new[] { "Black", "Blue", "Green", "Cyan", "Red", "Magenta", "Brown", "Gray", "DarkGray", "BrightBlue", "BrightGreen", "BrightCyan", "BrightRed", "BrightMagenta", "BrightYellow", "White" };
                var shapes = new[] { "Point", "Line", "Rectangle", "Circle" };
                var name = DecodeSharedMapText(parts[7]).Trim();
                if (String.IsNullOrWhiteSpace(name)) return false;
                point = new MapOverlayPoint
                {
                    Priority = priority, Uuid = uuid.ToString("D"), Latitude = latitude, Longitude = longitude,
                    Color = colors[colorCode], Shape = shapes[shapeCode], Filled = fillDensity > 0, FillDensity = fillDensity,
                    ShortName = name, Description = DecodeSharedMapText(parts[8]).Trim(), FillSymbol = DecodeSharedMapText(parts[9])
                };
                if (point.Shape != "Point")
                {
                    double referenceLatitude, referenceLongitude;
                    if (Double.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out referenceLatitude) && Double.TryParse(parts[11], NumberStyles.Float, CultureInfo.InvariantCulture, out referenceLongitude) && referenceLatitude >= -90d && referenceLatitude <= 90d && referenceLongitude >= -180d && referenceLongitude <= 180d)
                    { point.ReferenceLatitude = referenceLatitude; point.ReferenceLongitude = referenceLongitude; }
                    else return false;
                }
                return true;
            }
            catch { return false; }
        }

    }
}
