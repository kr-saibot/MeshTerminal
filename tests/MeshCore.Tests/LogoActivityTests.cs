using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MeshTerminal;
using MeshTerminal.Client;
internal static class LogoActivityTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("Logo activity: " + message); }
    private static string Trace(LogoActivityCycle cycle)
    {
        var positions = new List<string> { cycle.VerticalOffset + "," + cycle.HorizontalOffset };
        for (int i = 0; cycle.Active && i < 100; i++) { cycle.Advance(); positions.Add(cycle.VerticalOffset + "," + cycle.HorizontalOffset); }
        Check(!cycle.Active, "One-shot cycle must terminate");
        return String.Join(";", positions);
    }
    internal static void Run()
    {
        Check(Trace(new LogoActivityCycle(4, 0, 2, 1, false)) == "0,0;2,0;4,0;0,0", "Normal animation shows final frame, returns to rest and stops");
        Check(Trace(new LogoActivityCycle(4, 0, 2, 1, true)) == "0,0;2,0;4,0;2,0;0,0", "Ping-pong performs exactly one outbound and return pass");
        Check(Trace(new LogoActivityCycle(5, 2, 2, 1, false)) == "0,0;2,1;4,2;5,0;0,0", "Fast axis waits at rest without looping while the other axis completes");
        Check(Trace(new LogoActivityCycle(3, 2, 100, 100, true)) == "0,0;3,2;0,0", "Large steps preserve end frames and return once");
        Check(Trace(new LogoActivityCycle(0, 0, 8, 8, false)) == "0,0", "A static logo does not schedule animation");
        var ended = new LogoActivityCycle(1, 0, 1, 1, false); Trace(ended); ended.Advance();
        Check(!ended.Active && ended.VerticalOffset == 0, "Extra ticks cannot restart a finished cycle");
        var file = Path.Combine(Path.GetTempPath(), "logo-activity-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(file, "<MeshtasticApplicationSettings><Logo><AnimateTallLogos>true</AnimateTallLogos></Logo></MeshtasticApplicationSettings>");
            var settings = MeshtasticSettingsStore.Load(file);
            Check(!settings.Logo.AnimateOnLoRaTraffic && !settings.Logo.FlashOnDeviceFrames && settings.Logo.AnimateTallLogos, "Older settings keep continuous animation with both activity options off");
            settings.Logo.AnimateOnLoRaTraffic = true; settings.Logo.FlashOnDeviceFrames = true;
            MeshtasticSettingsStore.Save(file, settings); var restored = MeshtasticSettingsStore.Load(file);
            Check(restored.Logo.AnimateOnLoRaTraffic && restored.Logo.FlashOnDeviceFrames, "Both independent options survive restart");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Logo activity tests passed: finite loop/ping-pong, two-axis timing, static logos and saved opt-in preferences.");
    }
}
