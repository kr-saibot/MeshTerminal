using System;
using System.Globalization;

namespace Meshtastic.Client
{
    public static class TelnetMessageFormatter
    {
        public static string Format(DateTime occurredUtc, string sender, string text, TelnetServerSettings settings)
        {
            var timestamp = settings.ShowTimestamps ? "[" + occurredUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] " : "";
            var senderPrefix = settings.ShowSender ? sender + ": " : "";
            return timestamp + senderPrefix + (text ?? "");
        }
    }
}