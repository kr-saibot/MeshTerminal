using System;
using System.Collections.Generic;
using System.Linq;
using MeshTerminal.Client;
using MeshTerminal;
internal static class PathMapTests
{
    internal static void Run()
    {
        var a = new MeshCorePathStep { Name = "A", Latitude = 50, Longitude = 8 };
        var b = new MeshCorePathStep { Name = "B", Latitude = 51, Longitude = 9 };
        var unknown = new MeshCorePathStep { Name = "unknown" };
        var paths = new MeshCoreNodePaths { OutgoingSteps = new[] { a, b }, IncomingAdvertSteps = new[] { b, unknown, a } };
        var points = MeshCorePathMap.Build(paths);
        Check(points.Count(point => point.Shape == "Line") == 1, "No invented link across missing position");
        Check(points.Single(point => point.Shape == "Line").ShortName == "\u2022" && points.Single(point => point.Shape == "Line").Color == "White", "Bullet TO line (U+2022)");
        Check(points.Count(point => point.Shape == "Point" && point.Color == "White") == 2 && points.Count(point => point.Shape == "Point" && point.Color == "BrightYellow") == 2, "Both directions have their own colors");
        Check(points.All(point => point.IsPathOverlay && !point.Selectable), "Temporary paths cannot be edited as persistent XML items");
        var now = DateTime.UtcNow;
        var node = new StoredMeshNode { LastReceivedUtc = now.AddHours(-2) };
        Check(MapNodeVisibility.IsRecentlyHeard(node, true, 2, now), "Age threshold is inclusive");
        Check(!MapNodeVisibility.IsRecentlyHeard(node, true, 1.5, now), "Older node hidden with fractional hours");
        node.LastReceivedUtc = DateTime.MinValue;
        Check(!MapNodeVisibility.IsRecentlyHeard(node, true, 24, now) && MapNodeVisibility.IsRecentlyHeard(node, false, 24, now), "Unknown time only hidden while age filter enabled");
        Console.WriteLine("Path map and age filter tests passed.");
    }
    private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
}
