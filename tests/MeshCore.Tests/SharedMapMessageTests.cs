using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using MeshTerminal;
internal static class SharedMapMessageTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static string Encode(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('='); }
    public static void Run()
    {
        for (int shape = 0; shape < 4; shape++)
        {
            var uuid = Guid.NewGuid().ToString("D");
            var text = "///MAP+1|" + uuid.Replace("-", "") + "|48.34|12.12|e|" + shape + "|2|" + Encode("Test object") + "|" + Encode("Object description") + "|" + Encode("*") + "|" + (shape == 0 ? "|" : "48.35|12.13");
            MapOverlayPoint point;
            Check(SharedMapObjectProtocol.TryParse(text, out point) && point.Uuid == uuid && point.Shape == new[] { "Point", "Line", "Rectangle", "Circle" }[shape], "All shared shapes decode with stable UUID");
            Check(!point.Priority.HasValue, "Legacy messages remain readable without a priority");
            var legacy = text;
            text += "|1000000";
            Check(SharedMapObjectProtocol.TryParse(text, out point) && point.Priority == 1000000, "Shared priority is parsed for every shape");
            MapOverlayPoint invalid;
            Check(!SharedMapObjectProtocol.TryParse(legacy + "|1.5", out invalid) && !SharedMapObjectProtocol.TryParse(legacy + "|9223372036854775808", out invalid), "Malformed and overflowing priorities are rejected");
            Check(SharedMapObjectProtocol.TryParse(legacy + "|-100", out invalid) && invalid.Priority == -100, "Negative priority is preserved");
            var entry = MessageContentParser.Find(text).Single();
            Check(entry.MapObject != null && entry.ToString().Contains("Test object"), "Shared map message appears as a named map object");
            var data = new MapOverlayData();
            Check(SharedMapObjectProtocol.Restore(data, point), "Missing object can be restored");
            Check(!SharedMapObjectProtocol.Restore(data, point) && data.Places.Count == 1, "Existing object must not be duplicated or replaced");
            data.Places.Remove(point);
            MapOverlayPoint restored; SharedMapObjectProtocol.TryParse(text, out restored);
            Check(SharedMapObjectProtocol.Restore(data, restored), "Previously deleted object can be restored from original message");
            using (var stream = new MemoryStream())
            {
                var serializer = new XmlSerializer(typeof(MapOverlayData)); serializer.Serialize(stream, data); stream.Position = 0;
                var saved = ((MapOverlayData)serializer.Deserialize(stream)).Places.Single();
                Check(saved.Priority == 1000000 && saved.Uuid == uuid && saved.Shape == point.Shape && saved.ReferenceLatitude == point.ReferenceLatitude && saved.EffectiveFillDensity == 2 && saved.Description == "Object description", "Restored geometry and metadata survive map XML persistence");
            }
            data.WriteProtected = true;
            bool rejected = false; try { SharedMapObjectProtocol.Restore(data, point); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Protected map layers cannot be changed");
            Check(!SharedMapObjectProtocol.TryParse(text.Replace("48.34", "NaN"), out point), "Invalid geometry is rejected");
        }
        MapOverlayPoint ignored;
        Check(!SharedMapObjectProtocol.TryParse("///MAP-" + Guid.NewGuid().ToString("N"), out ignored), "Deletion messages are not mistaken for full objects");
        Console.WriteLine("Shared map message tests passed: points, lines, rectangles, circles, menu detection, restore after deletion, duplicate prevention and persistent geometry.");
    }
}
