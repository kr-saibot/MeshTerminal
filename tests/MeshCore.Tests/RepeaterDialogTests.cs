using System;
using System.Linq;
using System.IO;
using MeshTerminal;
using MeshTerminal.Client;
internal static class RepeaterDialogTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        Check(DialogDefaultPolicy.Preferred(new[] { "Delete", "Cancel" }) == 1, "Deletion must default to cancel");
        Check(DialogDefaultPolicy.Preferred(new[] { "Save", "_Close" }) == 1, "Close is safe default");
        Check(DialogDefaultPolicy.Preferred(new[] { "Yes", "No" }) == 1, "Confirmation must default to no");
        Check(DialogDefaultPolicy.Preferred(new[] { "OK" }) == -1, "Single action keeps existing default");
        Check(RepeaterCommands.All.Count > 100 && RepeaterCommands.All.All(c => !String.IsNullOrWhiteSpace(c.Help)), "Complete documented catalog with help");
        Check(RepeaterCommands.All.Select(c => c.Template).Distinct().Count() == RepeaterCommands.All.Count, "No duplicate commands");
        Check(!RepeaterCommands.Find("erase").RemoteAllowed, "Erase is serial only");
        Check(RepeaterCommands.Find("region load #test").RemoteAllowed, "Named region load supports remote execution");
        Check(RepeaterCommands.Find("reboot").Confirm, "Reboot needs confirmation");
        Check(RepeaterCommands.ContainsPlaceholders("set radio <freq>"), "Unfilled command must not be sent");
        Check(!RepeaterCommands.ContainsPlaceholders("region default <null>"), "Null region token is valid");
        var password = "test-only-" + Guid.NewGuid().ToString("N");
        var credential = RepeaterCredential.Create("test-public-key", password);
        Check(credential.Read() == password && !credential.ProtectedPassword.Contains(password), "Protected password round trip");
        var file = Path.Combine(Path.GetTempPath(), "repeater-credential-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var settings = new MeshtasticApplicationSettings(); settings.RepeaterCredentials.Add(credential);
            Check(settings.RepeaterQuickCommands.Length == 6, "Six default quick commands");
            settings.RepeaterQuickCommands[0] = "get radio"; settings.RepeaterQuickCommands[5] = "";
            MeshtasticSettingsStore.Save(file, settings);
            Check(MeshtasticSettingsStore.Load(file).RepeaterQuickCommands.SequenceEqual(settings.RepeaterQuickCommands), "Custom and empty quick commands survive restart");
            Check(!File.ReadAllText(file).Contains(password), "No plaintext password in settings");
            Check(MeshtasticSettingsStore.Load(file).RepeaterCredentials.Single().Read() == password, "Password survives restart");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Repeater/dialog tests passed: safe defaults, command catalog, restrictions, parameter checks and protected password persistence.");
    }
}
