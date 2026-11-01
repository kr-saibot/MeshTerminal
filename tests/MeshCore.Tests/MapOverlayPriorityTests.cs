using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using MeshTerminal;
internal static class MapOverlayPriorityTests
{
    internal static void Run()
    {
        var serializer = new XmlSerializer(typeof(MapOverlayData));
        var xml = "<MapData><Place><ShortName>Legacy</ShortName></Place><Place><Priority>-2</Priority></Place><Place><Priority>5000000</Priority></Place><Place><Priority>5000000</Priority></Place></MapData>";
        var data = (MapOverlayData)serializer.Deserialize(new StringReader(xml));
        Check(!data.Places[0].Priority.HasValue, "Legacy overlays have no priority");
        var labels = MapOverlayPriority.Labels(data.Places).ToArray();
        Check(labels[0] == data.Places[2] && labels[1] == data.Places[3] && labels[2] == data.Places[1] && labels[3] == data.Places[0], "Higher priority wins label collisions, explicit negative values precede missing values, ties retain order");
        var shapes = MapOverlayPriority.Shapes(data.Places).ToArray();
        Check(shapes[0] == data.Places[0] && shapes[3] == data.Places[3], "High-priority shapes draw last");
        var output = new StringWriter(); serializer.Serialize(output, data);
        var restored = (MapOverlayData)serializer.Deserialize(new StringReader(output.ToString()));
        Check(restored.Places[2].Priority == 5000000 && !restored.Places[0].Priority.HasValue && !output.ToString().Contains("xsi:nil"), "Optional priorities survive XML round trips without adding empty values");
        Console.WriteLine("Map overlay priority tests passed: legacy XML, optional values, collision order and serialization.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
