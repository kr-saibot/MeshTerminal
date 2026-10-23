using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MeshTerminal.Client
{
    public sealed class RepeaterCommand
    {
        public string Template { get; internal set; }
        public string Help { get; internal set; }
        public string Availability { get; internal set; }
        public bool RemoteAllowed { get; internal set; } = true;
        public bool Confirm { get; internal set; }
        public override string ToString() { return Template + (RemoteAllowed ? "" : " [serial only]"); }
        public string PreparedText { get { return Regex.Replace(Template, @"\s+\[[^\]]+\]", ""); } }
        public bool NeedsParameters { get { return Regex.IsMatch(PreparedText, @"<[^>]+>"); } }
    }

    public static class RepeaterCommands
    {
        public const string Reference = "https://github.com/meshcore-dev/MeshCore/blob/main/docs/cli_commands.md";
        public static readonly IReadOnlyList<RepeaterCommand> All = Build();
        private static List<RepeaterCommand> Build()
        {
            var items = new List<RepeaterCommand>();
            Action<string, string, bool, bool, string> add = (template, help, remote, confirm, availability) => items.Add(new RepeaterCommand { Template = template, Help = help, RemoteAllowed = remote, Confirm = confirm, Availability = availability });
            add("ver", "Shows the firmware version installed on the selected repeater.", true, false, "");
            add("board", "Shows the hardware/board name.", true, false, "");
            add("reboot", "Restarts the repeater. No radio reply is expected. Log in again afterwards.", true, true, "");
            add("poweroff", "Powers down the repeater where supported. No reply is expected; physical access may be needed to restart it.", true, true, "Board-dependent");
            add("shutdown", "Alias of poweroff. Stops the node without sending a reply.", true, true, "Board-dependent");
            add("clkreboot", "Resets the repeater clock and restarts it. No reply is expected.", true, true, "");
            add("clock", "Reads the repeater's current UTC clock.", true, false, "");
            add("clock sync", "Adjusts the repeater clock using the timestamp of this remote command.", true, false, "");
            add("time <epoch_seconds>", "Sets the UTC clock to a Unix timestamp, expressed as seconds since 1970-01-01.", true, false, "");
            add("advert", "Sends the repeater's identity advertisement using flooding.", true, false, "");
            add("advert.zerohop", "Sends an advertisement only to directly reachable radios.", true, false, "");
            add("start ota", "Starts the firmware's OTA update facility where available. Connectivity may change.", true, true, "Firmware/hardware-dependent");
            add("erase", "Erases the node configuration. This command requires a direct serial connection and cannot be sent through this remote dialog.", false, true, "Direct serial connection only");
            add("neighbors", "Lists up to eight recently heard neighbours. Each entry contains key prefix, advertisement timestamp and SNR scaled by four.", true, false, "Repeater firmware");
            add("neighbor.remove <pubkey_prefix>", "Removes matching entries from the neighbour list. A short or full public key can be supplied. This does not delete contacts on your Companion.", true, true, "Repeater firmware");
            add("discover.neighbors", "Requests discovery of direct, zero-hop neighbours.", true, false, "Repeater firmware");
            add("clear stats", "Resets the repeater's accumulated statistics counters.", true, true, "");
            add("stats-core", "Shows battery, uptime and system counters. Use local serial access for this text command; remote telemetry is available separately in the client.", false, false, "Direct serial connection only");
            add("stats-radio", "Shows receiver and airtime statistics through the serial CLI.", false, false, "Direct serial connection only");
            add("stats-packets", "Shows packet counters through the serial CLI.", false, false, "Direct serial connection only");
            add("log start", "Starts the repeater's own receive-log capture. This is separate from the client's RAM communication log.", true, false, "Node storage/logging support");
            add("log stop", "Stops the repeater's receive-log capture.", true, false, "Node storage/logging support");
            add("log erase", "Deletes the receive log stored on the repeater.", true, true, "Node storage/logging support");
            add("log", "Prints the repeater's saved log over its serial interface.", false, false, "Direct serial connection only");
            Action<string, string, string, string> pair = (setting, parameter, description, availability) =>
            {
                add("get " + setting, "Reads " + description + ".", true, false, availability);
                add("set " + setting + " " + parameter, "Changes " + description + ". Parameter: " + parameter + ".", true, setting == "prv.key", availability);
            };
            pair("radio", "<freq>,<bw>,<sf>,<cr>", "frequency in MHz, bandwidth in kHz, spreading factor 5-12 and coding rate 5-8; reboot to apply", "");
            pair("tx", "<dbm>", "LoRa transmit power in dBm (normally 1-22, depending on board)", "Board-dependent power range");
            add("tempradio <freq>,<bw>,<sf>,<cr>,<timeout_mins>", "Uses temporary radio settings for the supplied number of minutes. Changes are not retained after reboot.", true, false, "");
            add("get freq", "Reads the configured radio frequency in MHz.", true, false, "");
            add("set freq <frequency>", "Changes frequency in MHz through the serial CLI. For remote changes use set radio instead.", false, false, "Direct serial connection only");
            pair("radio.rxgain", "<on|off>", "the radio chip's boosted receive gain", "Firmware 1.14.1+ and supported radio");
            pair("radio.fem.rxgain", "<on|off>", "the external radio front-end receive gain", "Supported FEM boards only");
            pair("radio.fem.txgain", "<on|off>", "the external radio front-end transmit gain", "Supported FEM boards only");
            pair("name", "<name>", "the advertised node name", "Unicode names are limited by advert byte capacity");
            pair("lat", "<degrees>", "latitude in decimal degrees (-90 to 90)", "");
            pair("lon", "<degrees>", "longitude in decimal degrees (-180 to 180)", "");
            add("get prv.key", "Reads the identity private key through a direct serial connection.", false, false, "Direct serial connection only");
            add("set prv.key <private_key>", "Replaces the identity private key with 64 hexadecimal characters. This changes node identity after reboot and can prevent further remote access.", true, true, "Reboot required");
            add("password <new_password>", "Changes the repeater administrator password. The firmware echoes it in its reply; this client hides that reply. Update your saved password before the next login.", true, false, "");
            pair("guest.password", "<password>", "the guest login password", "");
            pair("owner.info", "<text>", "owner information; use | for a line break", "Firmware 1.12+");
            pair("adc.multiplier", "<value>", "the battery measurement correction factor (0-10)", "Supported battery ADC boards only");
            add("get public.key", "Shows the node's public identity key.", true, false, "");
            add("get role", "Shows the compiled node role.", true, false, "");
            foreach (var state in new[] { "", " on", " off" }) add("powersaving" + state, state == "" ? "Reads the power-saving state." : "Sets power saving" + state + ".", true, false, "Repeater firmware");
            pair("repeat", "<on|off>", "LoRa repeating; turning it off disables forwarding", "Repeater firmware");
            pair("path.hash.mode", "<0|1|2>", "advert path hash size: 0=one byte, 1=two bytes, 2=three bytes", "Firmware 1.14+");
            pair("loop.detect", "<off|minimal|moderate|strict>", "the routing loop-detection policy", "Firmware 1.14+");
            pair("txdelay", "<value>", "the flood retransmission random-delay factor (0-2)", "");
            pair("direct.txdelay", "<value>", "the direct retransmission delay factor (0-2)", "");
            pair("rxdelay", "<value>", "the experimental receive processing delay (0-20)", "");
            pair("dutycycle", "<percent>", "the allowed transmit duty-cycle percentage (1-100)", "Firmware 1.15+");
            pair("af", "<value>", "the older airtime factor (0-9)", "Deprecated on firmware 1.15+; prefer dutycycle");
            pair("int.thresh", "<value>", "the interference threshold", "");
            pair("cad", "<on|off>", "hardware channel activity detection before transmitting", "Supported radio/firmware");
            pair("agc.reset.interval", "<seconds>", "the AGC reset interval (0-1020 seconds, rounded down to multiples of four; zero disables)", "");
            pair("multi.acks", "<0|1>", "multiple acknowledgements support", "");
            pair("flood.advert.interval", "<hours>", "the flood advertisement interval (3-168 hours)", "");
            pair("advert.interval", "<minutes>", "the zero-hop advert interval (60-240 minutes in even steps; zero disables)", "");
            pair("flood.max", "<hops>", "the maximum flood hop count (0-64)", "");
            pair("flood.max.unscoped", "<hops>", "the maximum unscoped flood hop count (0-64)", "");
            pair("flood.max.advert", "<hops>", "the maximum advertisement flood hop count (0-64)", "");
            add("setperm <pubkey> <permissions>", "Sets a Companion's ACL permission: 0 guest, 1 read-only, 2 read-write, 3 administrator.", true, true, "");
            add("setperm <pubkey>", "Removes a Companion's ACL entry. Removing your own administrator key can prevent further access.", true, true, "");
            add("get acl", "Lists the complete access-control table through the serial CLI.", false, false, "Direct serial connection only");
            pair("allow.read.only", "<on|off>", "whether a room server permits read-only guest sessions", "Room-server firmware only");
            add("region load", "Starts interactive region input. The interactive form is unavailable through remote commands; use the named form or region def.", false, false, "Interactive serial command");
            add("region load <name> [flood_flag]", "Adds a named region; optional F permits flooding. Use * for the wildcard region. Save changes with region save.", true, false, "Region management, firmware 1.10+");
            add("region save", "Persists current region definitions and permissions on the repeater.", true, false, "Region management");
            add("region allowf <name>", "Allows flood forwarding for the named region or wildcard *. Persist with region save.", true, false, "Region management");
            add("region denyf <name>", "Blocks flood forwarding for the named region or wildcard *. This may affect reachability. Persist with region save.", true, true, "Region management");
            add("region get <name>", "Reads one region's parameters and forwarding policy.", true, false, "Region management");
            add("region home", "Reads the home region.", true, false, "Region management");
            add("region home <name>", "Selects the node's home region.", true, false, "Region management");
            add("region default", "Reads the default outgoing scope region.", true, false, "Region management");
            add("region default <name>", "Selects a default outgoing scope. Supply the literal <null> to clear it.", true, false, "Region management");
            add("region put <name> [parent_name]", "Creates or updates a region beneath an optional parent (wildcard by default). Save with region save.", true, false, "Region management");
            add("region def <token> [<token> ...]", "Defines a region hierarchy in one line. Each name descends into a child; name|jump creates a child then moves to an existing region. The cursor begins at *. Inspect with region, then region save.", true, false, "Recent firmware with region def support");
            add("region remove <name>", "Removes a region after its children have been removed. Persist with region save.", true, true, "Region management");
            add("region list <allowed|denied>", "Lists regions matching the filter using the serial CLI.", false, false, "Direct serial connection, firmware 1.12+");
            add("region", "Reads the region tree and flood permissions. On older firmware this command requires serial access.", true, false, "Remote support requires firmware 1.12+");
            add("gps", "Reads GPS power, activity, fix and satellite status.", true, false, "GPS-enabled firmware/hardware");
            add("gps <on|off>", "Enables or disables the node's GPS receiver.", true, false, "GPS-enabled firmware/hardware");
            add("gps sync", "Sets the node clock using GPS time.", true, false, "GPS support and time fix");
            add("gps setloc", "Copies the current GPS fix into the stored node location.", true, false, "GPS support and position fix");
            add("gps advert", "Reads the location policy for advertisements.", true, false, "GPS-enabled firmware/hardware");
            add("gps advert <none|share|prefs>", "Selects no advert position, live GPS position, or saved latitude/longitude.", true, false, "GPS-enabled firmware/hardware");
            add("sensor list [start]", "Lists sensor variables from optional index start (default zero). Use pagination if the reply is long.", true, false, "Sensor-enabled firmware");
            add("sensor get <key>", "Reads a sensor variable by name.", true, false, "Sensor-enabled firmware");
            add("sensor set <key> <value>", "Changes a sensor variable supported by this board.", true, false, "Sensor-enabled firmware");
            add("get bridge.type", "Shows which external bridge backend was compiled in.", true, false, "Bridge-enabled firmware");
            pair("bridge.enabled", "<on|off>", "external packet bridging", "Bridge-enabled firmware");
            pair("bridge.delay", "<ms>", "bridge forwarding delay in milliseconds (0-10000)", "Bridge-enabled firmware");
            pair("bridge.source", "<logRx|logTx>", "whether the bridge exports received or transmitted packets", "Bridge-enabled firmware");
            pair("bridge.baud", "<rate>", "bridge baud rate: 9600, 19200, 38400, 57600 or 115200", "RS-232 bridge only");
            pair("bridge.channel", "<channel>", "ESP-NOW bridge channel (1-14)", "ESP-NOW bridge only");
            pair("bridge.secret", "<secret>", "ESP-NOW bridge secret (up to 15 characters)", "ESP-NOW bridge only");
            add("get bootloader.ver", "Reads the bootloader version.", true, false, "nRF52 only");
            add("get pwrmgt.support", "Reads available power management features.", true, false, "Supported boards only");
            add("get pwrmgt.source", "Reads the current power source.", true, false, "Supported power-management boards");
            add("get pwrmgt.bootreason", "Reads the boot/reset and shutdown reason.", true, false, "Supported power-management boards");
            add("get pwrmgt.bootmv", "Reads the recorded boot voltage in millivolts.", true, false, "Supported power-management boards");
            add("eth.status", "Reads the Ethernet interface status and address.", true, false, "Ethernet-enabled repeater/room-server firmware");
            return items.OrderBy(c => c.Template, StringComparer.Ordinal).ToList();
        }
        public static RepeaterCommand Find(string text)
        {
            text = (text ?? "").Trim();
            return All.Where(c => { var prefix = Regex.Split(c.Template, @"\s+[<\[]")[0]; return text == prefix || text.StartsWith(prefix + " ", StringComparison.Ordinal); })
                .OrderByDescending(c => c.Template == text).ThenByDescending(c => Regex.Split(c.Template, @"\s+[<\[]")[0].Length).ThenByDescending(c => c.NeedsParameters).ThenByDescending(c => c.RemoteAllowed).FirstOrDefault();
        }
        public static bool ContainsPlaceholders(string text)
        { return Regex.IsMatch(text ?? "", @"<(?!null>)[^>]+>|\[(?:start|flood_flag|parent_name|<token>)[^\]]*\]"); }
    }
}
