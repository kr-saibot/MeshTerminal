using System;
using System.IO;
using MeshTerminal.Client;
internal static class RenameCompatibilityTests
{
    public static void Run()
    {
        var file = Path.Combine(Path.GetTempPath(), "meshterminal-legacy-settings-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(file, "<MeshtasticApplicationSettings><Connection><Protocol>MeshCore</Protocol></Connection><StorePositionData>true</StorePositionData><RepeaterQuickCommands><string>get radio</string><string>clock</string></RepeaterQuickCommands></MeshtasticApplicationSettings>");
            var settings = MeshtasticSettingsStore.Load(file);
            if (settings.ChessUseLetters) throw new Exception("Legacy settings must default to chess symbols.");
            if (settings.Connection.Protocol != MeshProtocol.MeshCore || !settings.StorePositionData || settings.RepeaterQuickCommands.Length != 2 || settings.RepeaterQuickCommands[0] != "get radio") throw new Exception("Legacy settings did not survive the MeshTerminal rename.");
            if (new MeshtasticMapSettings().MapItemPriority != 100) throw new Exception("Map item priority must default to 100.");
            settings.Map.MapItemPriority = -12345;
            MeshtasticSettingsStore.Save(file, settings);
            if (MeshtasticSettingsStore.Load(file).Map.MapItemPriority != -12345) throw new Exception("Map item priority preference was not persisted.");
            settings.ChessUseLetters = true;
            MeshtasticSettingsStore.Save(file, settings);
            if (!MeshtasticSettingsStore.Load(file).ChessUseLetters) throw new Exception("ASCII chess preference was not persisted.");
            settings.ChessDisplayMode = "Blocks";
            MeshtasticSettingsStore.Save(file, settings);
            if (MeshtasticSettingsStore.Load(file).ChessDisplayMode != "Blocks") throw new Exception("Block chess preference was not persisted.");
            if (typeof(MeshtasticClient).Assembly.GetName().Name != "MeshTerminal.Client") throw new Exception("Library assembly still uses the old product name.");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("MeshTerminal rename tests passed: legacy settings format, protocol, position preferences, command buttons and assembly identity.");
    }
}
