using System;
using System.Collections.Generic;
using Meshtastic.Client;
namespace ConsoleClient
{
    internal static class MeshCorePathMap
    {
        internal static List<MapOverlayPoint> Build(MeshCoreNodePaths paths)
        {
            var result = new List<MapOverlayPoint>(); Add(result, paths.OutgoingSteps, "TO", "White"); Add(result, paths.IncomingAdvertSteps, "FROM", "BrightYellow"); return result;
        }
        private static bool HasPosition(MeshCorePathStep step) { return step != null && step.Latitude.HasValue && step.Longitude.HasValue && !Double.IsNaN(step.Latitude.Value) && !Double.IsNaN(step.Longitude.Value) && Math.Abs(step.Latitude.Value) <= 90 && Math.Abs(step.Longitude.Value) <= 180; }
        private static void Add(List<MapOverlayPoint> result, IList<MeshCorePathStep> steps, string direction, string color)
        {
            if (steps == null) return;
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index]; if (!HasPosition(step)) continue;
                result.Add(new MapOverlayPoint { Latitude = step.Latitude.Value, Longitude = step.Longitude.Value, ShortName = direction + ": " + step.Name, Color = color, Selectable = false, IsPathOverlay = true, OverlayName = "MeshCore paths", Description = step.Name });
                if (index == 0 || !HasPosition(steps[index - 1])) continue;
                var previous = steps[index - 1];
                result.Add(new MapOverlayPoint { Shape = "Line", ShortName = "\u2022", Latitude = previous.Latitude.Value, Longitude = previous.Longitude.Value, ReferenceLatitude = step.Latitude, ReferenceLongitude = step.Longitude, Color = color, Selectable = false, IsPathOverlay = true, OverlayName = "MeshCore paths" });
            }
        }
    }
}
