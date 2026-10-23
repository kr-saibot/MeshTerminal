using System;
using MeshTerminal;

internal static class MapMarkerTests
{
    public static void Run()
    {
        var marker = new MapPositionMarker { NodeNumber = 123, Latitude = -33.123456, Longitude = 151.234567, CallSign = "Test" };
        var legacy = MapPositionMarkerProtocol.Build(marker);
        MapPositionMarker parsed;
        if (!MapPositionMarkerProtocol.TryParse(legacy, 123, out parsed) || MapPositionMarkerProtocol.TryParse(legacy, 456, out parsed))
            throw new Exception("Meshtastic marker sender validation failed.");
        marker.Identity = new string('a', 64);
        var meshCore = MapPositionMarkerProtocol.Build(marker);
        if (!MapPositionMarkerProtocol.TryParse(meshCore, 999, out parsed, key => key == marker.Identity ? 456u : 0u) || parsed.NodeNumber != 456 || parsed.Identity != marker.Identity)
            throw new Exception("MeshCore marker identity must survive different local node numbers.");
        if (MapPositionMarkerProtocol.TryParse(meshCore, 999, out parsed) || MapPositionMarkerProtocol.TryParse(meshCore.Replace(new string('a', 64), new string('z', 64)), 999, out parsed, key => 456u))
            throw new Exception("Invalid MeshCore marker accepted.");
        Console.WriteLine("Map marker tests passed: stable MeshCore keys and Meshtastic sender validation.");
    }
}