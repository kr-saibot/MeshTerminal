using System;
using System.IO;
using System.Linq;
using MeshTerminal.Client;
internal static class MeshCoreTelemetryPaddingTests
{
    public static void Run()
    {
        var sensors = new byte[] { 1, 116, 1, 159, 1, 103, 0, 0 };
        for (var padding = 1; padding <= 15; padding++)
        {
            var decoded = MeshCoreLpp.Decode(sensors.Concat(new byte[padding]).ToArray(), true);
            Check(Math.Abs(decoded.Voltage.Value - 4.15) < 0.001 && decoded.Temperature == 0, "Padded remote reply retains real zero-valued temperature");
            Check(!decoded.Description.Contains("Type 0"), "Padding does not create phantom sensors");
        }
        var gps = new byte[] { 1, 136, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Check(MeshCoreLpp.Decode(gps.Concat(new byte[1]).ToArray(), true).Position.LatitudeI == 0, "Zero GPS coordinates preserved");
        Rejected(sensors.Concat(new byte[] { 7 }).ToArray(), true, "Nonzero truncated header");
        Rejected(sensors.Concat(new byte[] { 1, 116, 0 }).ToArray(), true, "Incomplete zero-valued sensor field");
        Rejected(sensors.Concat(new byte[1]).ToArray(), false, "Local response remains strict");
        var file = Path.Combine(Path.GetTempPath(), "NodeSearch-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var settings = new MeshtasticApplicationSettings(); settings.Nodes.SearchText = "dm";
            MeshtasticSettingsStore.Save(file, settings);
            Check(!File.ReadAllText(file).Contains("<SearchText>"), "Search text not serialized");
            var xml = File.ReadAllText(file).Replace("<Nodes>", "<Nodes><SearchText>legacy filter</SearchText>");
            File.WriteAllText(file, xml);
            Check(String.IsNullOrEmpty(MeshtasticSettingsStore.Load(file).Nodes.SearchText), "Legacy saved search ignored");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Telemetry padding and transient node-search tests passed.");
    }
    private static void Rejected(byte[] bytes, bool remote, string description)
    {
        try { MeshCoreLpp.Decode(bytes, remote); }
        catch (InvalidDataException) { return; }
        throw new Exception(description);
    }
    private static void Check(bool value, string description) { if (!value) throw new Exception(description); }
}
