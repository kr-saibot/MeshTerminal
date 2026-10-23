using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Meshtastic.ConsoleHost
{
    internal static class AppSettings
    {
        private const string RunValueName = "MeshtasticConsoleHost";
        internal static string GetSettingsFileName(string executable)
        {
            var name = Path.GetFileNameWithoutExtension(executable);
            return Path.Combine(Path.GetDirectoryName(executable), String.Equals(name, "MeshtasticConsoleHost", StringComparison.OrdinalIgnoreCase) ? "settings.ini" : name + ".settings.ini");
        }
        private static readonly string FileName = GetSettingsFileName(Application.ExecutablePath);        private static readonly string DefaultBellSoundPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "assets", "meshtastic-alert.wav");

        public static string ClientPath { get; set; }
        public static string ProgramIconId { get; set; } = "default";
        public static string BellSoundPath { get; set; } = DefaultBellSoundPath;
        public static int FontSize { get; set; } = 12;
        public static bool StartMinimized { get; set; }
        public static bool MinimizeToTray { get; set; }
        public static bool DarkMode { get; set; } = true;
        public static int WindowWidth { get; set; } = 1200;
        public static int WindowHeight { get; set; } = 760;
        public static bool LoadedFromProgramFolder { get; private set; }

        public static void Load()
        {
            try
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                LoadedFromProgramFolder = File.Exists(FileName);
                var source = FileName;
                if (File.Exists(source))
                {
                    foreach (var line in File.ReadAllLines(source))
                    {
                        var separator = line.IndexOf('=');
                        if (separator > 0)
                            values[line.Substring(0, separator)] = line.Substring(separator + 1);
                    }
                }

                string value;
                if (values.TryGetValue("ProgramIcon", out value) && ProgramIcons.IsValid(value)) ProgramIconId = value;
                // Never migrate an executable path from the old per-user settings.
                // Packaged builds must prefer the ConsoleClient beside the host.
                if (LoadedFromProgramFolder && values.TryGetValue("ClientPath", out value)) ClientPath = value;
                if (values.TryGetValue("BellSoundPath", out value))
                {
                    if (String.Equals(value, "<none>", StringComparison.OrdinalIgnoreCase)) BellSoundPath = null;
                    else if (!String.IsNullOrWhiteSpace(value)) BellSoundPath = value;
                }
                if (values.TryGetValue("FontSize", out value))
                {
                    int parsed;
                    if (Int32.TryParse(value, out parsed) && parsed >= 8 && parsed <= 32) FontSize = parsed;
                }
                if (values.TryGetValue("StartMinimized", out value))
                {
                    bool parsed;
                    if (Boolean.TryParse(value, out parsed)) StartMinimized = parsed;
                }
                if (values.TryGetValue("MinimizeToTray", out value))
                {
                    bool parsed;
                    if (Boolean.TryParse(value, out parsed)) MinimizeToTray = parsed;
                }
                if (values.TryGetValue("DarkMode", out value))
                {
                    bool parsed;
                    if (Boolean.TryParse(value, out parsed)) DarkMode = parsed;
                }
                if (values.TryGetValue("WindowWidth", out value))
                {
                    int parsed;
                    if (Int32.TryParse(value, out parsed) && parsed >= 640) WindowWidth = parsed;
                }
                if (values.TryGetValue("WindowHeight", out value))
                {
                    int parsed;
                    if (Int32.TryParse(value, out parsed) && parsed >= 400) WindowHeight = parsed;
                }

            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(FileName, new[]
                {
                    "ClientPath=" + (ClientPath ?? String.Empty),
                    "ProgramIcon=" + ProgramIconId,
                    "BellSoundPath=" + (BellSoundPath ?? "<none>"),
                    "FontSize=" + FontSize,
                    "StartMinimized=" + StartMinimized,
                    "MinimizeToTray=" + MinimizeToTray,
                    "DarkMode=" + DarkMode,
                    "WindowWidth=" + WindowWidth,
                    "WindowHeight=" + WindowHeight
                });
            }
            catch { }
        }

        public static bool StartsWithWindows
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                        return key != null && key.GetValue(RunValueName) != null;
                }
                catch { return false; }
            }
        }

        public static bool SetStartsWithWindows(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (enabled) key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(RunValueName, false);
                }
                return true;
            }
            catch { return false; }
        }
    }
}
