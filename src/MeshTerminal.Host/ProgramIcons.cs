using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MeshTerminal.Host
{
    internal static class ProgramIcons
    {
        public static readonly string[] Colors = { "blue", "green", "orange", "red", "purple", "teal" };
        public static readonly string[] ColorNames = { "Blau", "Grün", "Orange", "Rot", "Violett", "Türkis" };
        public static string DirectoryPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "icons"); } }

        public static bool IsValid(string id)
        {
            if (id == "default") return true;
            if (id != null && id.StartsWith("file:", StringComparison.Ordinal))
            {
                var name = id.Substring(5);
                return !String.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                    && name == Path.GetFileName(name) && String.Equals(Path.GetExtension(name), ".ico", StringComparison.OrdinalIgnoreCase);
            }
            foreach (var label in new[] { "mc", "mt" })
                foreach (var color in Colors)
                    if (id == label + "-" + color || id == label + "-" + color + "-inverted") return true;
            return false;
        }

        // Return an independent icon so each window can safely own and dispose it.
        public static Icon Load(string id)
        {
            if (IsValid(id) && id.StartsWith("file:", StringComparison.Ordinal))
            {
                try
                {
                    using (var icon = new Icon(Path.Combine(DirectoryPath, id.Substring(5)), new Size(32, 32)))
                        return (Icon)icon.Clone();
                }
                catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
                { return Load("default"); }
            }
            var resource = id == "default" || !IsValid(id)
                ? "MeshTerminal.Host.assets.meshtastic-chirp.ico"
                : "MeshTerminal.Host.assets.icons." + id + ".ico";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) return (Icon)SystemIcons.Application.Clone();
                using (var icon = new Icon(stream, new Size(32, 32)))
                    return (Icon)icon.Clone();
            }
        }

        public static IEnumerable<KeyValuePair<string, string>> Choices()
        {
            var choices = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("default", "Standard") };
            foreach (var inverted in new[] { false, true })
                foreach (var label in new[] { "MC", "MT" })
                    for (var index = 0; index < Colors.Length; index++)
                        choices.Add(new KeyValuePair<string, string>(label.ToLowerInvariant() + "-" + Colors[index] + (inverted ? "-inverted" : ""),
                            label + " · " + ColorNames[index] + (inverted ? " · Rahmen" : "")));
            try
            {
                if (Directory.Exists(DirectoryPath))
                    foreach (var path in Directory.EnumerateFiles(DirectoryPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                    {
                        var name = Path.GetFileName(path);
                        if (!String.Equals(Path.GetExtension(name), ".ico", StringComparison.OrdinalIgnoreCase)
                            || IsValid(Path.GetFileNameWithoutExtension(name).ToLowerInvariant())) continue;
                        try
                        {
                            using (var icon = new Icon(path, new Size(32, 32)))
                                choices.Add(new KeyValuePair<string, string>("file:" + name, Path.GetFileNameWithoutExtension(name)));
                        }
                        catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException) { }
                    }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { }
            return choices;
        }
    }
}
