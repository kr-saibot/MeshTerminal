using System;
using System.Linq;
using System.Text;
using Meshtastic.Protobufs;

namespace Meshtastic.Client
{
    public sealed class CommunicationFrameEventArgs : EventArgs
    {
        public DateTime TimestampUtc { get; private set; }
        public string Direction { get; private set; }
        public MeshProtocol Protocol { get; private set; }
        public string Transport { get; private set; }
        public uint LocalNodeNumber { get; private set; }
        public string LocalIdentity { get; private set; }
        public byte[] RawData { get; private set; }
        public byte[] Payload { get; private set; }
        public CommunicationFrameEventArgs(DateTime time, string direction, MeshProtocol protocol, string transport, byte[] raw, byte[] payload, uint localNodeNumber = 0, string localIdentity = null)
        {
            LocalNodeNumber = localNodeNumber; LocalIdentity = localIdentity;
            TimestampUtc = time; Direction = direction; Protocol = protocol; Transport = transport;
            RawData = (byte[])raw.Clone(); Payload = (byte[])payload.Clone();
        }
        public bool IsLoRaRelated()
        {
            try
            {
                if (Protocol == MeshProtocol.Meshtastic)
                {
                    var packet = Direction == "TX" ? ToRadio.Parser.ParseFrom(Payload).Packet : FromRadio.Parser.ParseFrom(Payload).Packet;
                    if (packet == null) return false;
                    if (Direction == "TX") return LocalNodeNumber == 0 || packet.To != LocalNodeNumber;
                    if (packet.From == 0) return false;
                    if (LocalNodeNumber != 0 && packet.From == LocalNodeNumber &&
                        (packet.To == LocalNodeNumber || packet.Decoded != null && (packet.Decoded.Portnum == PortNum.AdminApp || packet.Decoded.Portnum == PortNum.RoutingApp))) return false;
                    return true;
                }
                if (Payload.Length == 0) return false;
                var code = Payload[0];
                if (Direction == "TX")
                {
                    // Queued-message polling and local configuration do not transmit over LoRa.
                    switch (code)
                    {
                        case 2: case 3: case 7: case 16: case 25: case 26: case 27:
                        case 36: case 50: case 52: case 55: case 57: case 62: case 65: return true;
                        case 39: return Payload.Length > 4; // Four bytes request local telemetry only.
                        default: return false;
                    }
                }
                if (code == 0x8B && Payload.Length >= 8 && !String.IsNullOrEmpty(LocalIdentity) &&
                    LocalIdentity.StartsWith(Hex(Payload, 2, 6), StringComparison.OrdinalIgnoreCase)) return false;
                switch (code)
                {
                    case 6: case 7: case 8: case 16: case 17: case 27: // send result / queued radio messages
                    case 0x80: case 0x81: case 0x82: case 0x84: // advert / path / radio ACK
                    case 0x85: case 0x86: case 0x87: // remote login and status
                    case 0x88: case 0x89: case 0x8A: // RX packet / trace / new advert
                    case 0x8B: case 0x8C: case 0x8D: case 0x8E: return true;
                    default: return false;
                }
            }
            catch { return false; }
        }
        public string Decode()
        {
            try
            {
                if (Protocol == MeshProtocol.Meshtastic)
                {
                    if (Payload.Length == 0) return "Transport wake / resynchronization";
                    if (Direction == "TX") { var radio = ToRadio.Parser.ParseFrom(Payload); return radio.ToString() + DecodePacket(radio.Packet); }
                    else { var radio = FromRadio.Parser.ParseFrom(Payload); return radio.ToString() + DecodePacket(radio.Packet); }
                }
                return DecodeMeshCore(Payload, Direction == "TX");
            }
            catch (Exception ex) { return "Decode error: " + ex.Message; }
        }
        private static string DecodePacket(MeshPacket packet)
        {
            if (packet == null || packet.Decoded == null) return "";
            var data = packet.Decoded; var bytes = data.Payload;
            try
            {
                switch (data.Portnum)
                {
                    case PortNum.TextMessageApp: return "\nText: " + bytes.ToStringUtf8();
                    case PortNum.PositionApp: return "\nPosition: " + Position.Parser.ParseFrom(bytes);
                    case PortNum.TelemetryApp: return "\nTelemetry: " + Telemetry.Parser.ParseFrom(bytes);
                    case PortNum.NodeinfoApp: return "\nNode: " + User.Parser.ParseFrom(bytes);
                    case PortNum.AdminApp: return "\nAdmin: " + AdminMessage.Parser.ParseFrom(bytes);
                    case PortNum.RoutingApp: return "\nRouting: " + Routing.Parser.ParseFrom(bytes);
                    default: return "";
                }
            }
            catch (Exception ex) { return "\nApplication payload decode error: " + ex.Message; }
        }
        private static string Text(byte[] data, int offset, int count)
        { return offset >= data.Length ? "" : Encoding.UTF8.GetString(data, offset, Math.Min(count, data.Length - offset)).TrimEnd('\0'); }
        private static uint U32(byte[] data, int offset) { return MeshCoreSession.ReadUInt32(data, offset); }
        private static string Hex(byte[] data, int offset, int count) { return BitConverter.ToString(data, offset, count).Replace("-", ""); }
        private static string DecodeMeshCore(byte[] p, bool tx)
        {
            if (p.Length == 0) return "Empty frame";
            string[] commands = { "APP_START", "SEND_TXT_MSG", "SEND_CHANNEL_TXT_MSG", "GET_CONTACTS", "GET_DEVICE_TIME", "SET_DEVICE_TIME", "SEND_SELF_ADVERT", "SET_ADVERT_NAME", "ADD_UPDATE_CONTACT", "SYNC_NEXT_MESSAGE", "SET_RADIO_PARAMS", "SET_RADIO_TX_POWER", "RESET_PATH", "SET_ADVERT_LAT_LON", "REMOVE_CONTACT", "SHARE_CONTACT", "EXPORT_CONTACT", "IMPORT_CONTACT", "REBOOT", "GET_BATT_AND_STORAGE", "SET_TUNING_PARAMS", "DEVICE_QUERY", "EXPORT_PRIVATE_KEY", "IMPORT_PRIVATE_KEY", "SEND_RAW_DATA", "SEND_LOGIN", "SEND_STATUS_REQ", "HAS_CONNECTION", "LOGOUT", "GET_CONTACT_BY_KEY", "GET_CHANNEL", "SET_CHANNEL" };
            string name;
            if (tx) name = p[0] >= 1 && p[0] <= commands.Length ? commands[p[0] - 1] :
                p[0] == 38 ? "SET_OTHER_PARAMS" : p[0] == 39 ? "SEND_TELEMETRY_REQ" : p[0] == 50 ? "SEND_BINARY_REQ" : p[0] == 55 ? "SEND_DISCOVER_REQ" : "COMMAND";
            else
            {
                switch (p[0])
                {
                    case 0: name = "OK"; break; case 1: name = "ERROR"; break; case 2: name = "CONTACTS_START"; break;
                    case 3: name = "CONTACT"; break; case 4: name = "CONTACTS_END"; break; case 5: name = "SELF_INFO"; break;
                    case 6: name = "SENT"; break; case 7: name = "CONTACT_MSG_RECV"; break; case 8: name = "CHANNEL_MSG_RECV"; break;
                    case 9: name = "CURRENT_TIME"; break; case 10: name = "NO_MORE_MESSAGES"; break; case 12: name = "BATTERY_AND_STORAGE"; break;
                    case 13: name = "DEVICE_INFO"; break; case 15: name = "DISABLED"; break; case 16: name = "CONTACT_MSG_RECV_V3"; break;
                    case 17: name = "CHANNEL_MSG_RECV_V3"; break; case 18: name = "CHANNEL_INFO"; break; case 24: name = "STATS"; break;
                    case 0x80: name = "ADVERT"; break; case 0x81: name = "PATH_UPDATED"; break; case 0x82: name = "SEND_CONFIRMED"; break;
                    case 0x83: name = "MESSAGES_WAITING"; break; case 0x85: name = "LOGIN_SUCCESS"; break; case 0x86: name = "LOGIN_FAILED"; break;
                    case 0x88: name = "RX_LOG_DATA"; break; case 0x8A: name = "NEW_ADVERT"; break; case 0x8B: name = "TELEMETRY_RESPONSE"; break;
                    case 0x8C: name = "BINARY_RESPONSE"; break; case 0x8E: name = "DISCOVER_RESPONSE"; break; default: name = "RESPONSE / PUSH"; break;
                }
            }
            var text = new StringBuilder(name + " (0x" + p[0].ToString("X2") + ", " + p.Length + " bytes)");
            if ((tx && p[0] == 2 && p.Length >= 13) || (tx && p[0] == 3 && p.Length >= 7))
            {
                var direct = p[0] == 2;
                text.Append("\nType: " + p[1] + " | " + (direct ? "Attempt: " : "Channel: ") + p[2] + "\nTimestamp: " + U32(p, 3));
                if (direct) text.Append("\nDestination prefix: " + Hex(p, 7, 6));
                text.Append("\nText: " + Text(p, direct ? 13 : 7, p.Length));
            }
            else if (!tx && (p[0] == 7 || p[0] == 8 || p[0] == 16 || p[0] == 17))
            {
                bool direct = p[0] == 7 || p[0] == 16; var offset = p[0] >= 16 ? 4 : 1;
                if (p.Length >= offset + (direct ? 12 : 7))
                {
                    if (offset == 4) text.Append("\nSNR: " + (unchecked((sbyte)p[1]) / 4d) + " dB");
                    text.Append(direct ? "\nSender prefix: " + Hex(p, offset, 6) : "\nChannel: " + p[offset]);
                    offset += direct ? 6 : 1;
                    var path = p[offset++]; var type = p[offset++];
                    text.Append("\nPath: " + path + " | Text type: " + type + "\nTimestamp: " + U32(p, offset)); offset += 4;
                    if (type == 2 && p.Length >= offset + 4) { text.Append("\nAuthor prefix: " + Hex(p, offset, 4)); offset += 4; }
                    text.Append("\nText: " + Text(p, offset, p.Length));
                }
            }
            else if (((tx && p[0] == 9) || (!tx && (p[0] == 3 || p[0] == 0x8A))) && p.Length >= 148)
                text.Append("\nPublic key: " + Hex(p, 1, 32) + "\nNode type: " + p[33] + " | Flags: " + p[34] + " | Path length: " + p[35] + "\nName: " + Text(p, 100, 32) + "\nAdvert timestamp: " + U32(p, 132) + "\nLatitude: " + unchecked((int)U32(p, 136)) / 1000000d + "\nLongitude: " + unchecked((int)U32(p, 140)) / 1000000d);
            else if (((tx && p[0] == 32) || (!tx && p[0] == 18)) && p.Length >= 50)
                text.Append("\nChannel: " + p[1] + "\nName: " + Text(p, 2, 32) + "\nKey: " + Hex(p, 34, 16));
            else if ((!tx && p[0] == 9 || tx && p[0] == 6 || !tx && (p[0] == 2 || p[0] == 4)) && p.Length >= 5)
                text.Append("\n" + (!tx && p[0] == 2 ? "Contact count: " : "Timestamp: ") + U32(p, 1));
            else if (!tx && p[0] == 13 && p.Length >= 4)
                text.Append("\nProtocol version: " + p[1] + " | Channel slots: " + p[3] + (p.Length >= 80 ? "\nFirmware: " + Text(p, 60, 20) + "\nHardware: " + Text(p, 20, 40) : ""));
            else if (!tx && p[0] == 5 && p.Length >= 58)
                text.Append("\nNode type: " + p[1] + " | TX power: " + unchecked((sbyte)p[2]) + " dBm\nPublic key: " + Hex(p, 4, 32) +
                    "\nLatitude: " + unchecked((int)U32(p, 36)) / 1000000d + "\nLongitude: " + unchecked((int)U32(p, 40)) / 1000000d +
                    "\nFrequency: " + U32(p, 48) / 1000d + " MHz | Bandwidth: " + U32(p, 52) / 1000d + " kHz\nSF: " + p[56] + " | CR: " + p[57] + "\nName: " + Text(p, 58, p.Length));
            else if (!tx && p[0] == 0x8B && p.Length >= 8)
                text.Append("\nSender prefix: " + Hex(p, 2, 6) + "\n" + MeshCoreLpp.Decode(p.Skip(8).ToArray()).Description);
            else if (!tx && p[0] == 24 && p.Length >= 4 && p[1] == 1)
                text.Append("\nRadio stats | Noise floor: " + unchecked((short)(p[2] | p[3] << 8)) + " dBm\nData: " + Hex(p, 1, p.Length - 1));
            else if (tx && p[0] == 8) text.Append("\nName: " + Text(p, 1, p.Length));
            else if (tx && p[0] == 11 && p.Length >= 11)
                text.Append("\nFrequency: " + U32(p, 1) / 1000d + " MHz | Bandwidth: " + U32(p, 5) / 1000d + " kHz\nSF: " + p[9] + " | CR: " + p[10]);
            else if (tx && p[0] == 12 && p.Length >= 2) text.Append("\nTX power: " + unchecked((sbyte)p[1]) + " dBm");
            else if (tx && p[0] == 14 && p.Length >= 9)
                text.Append("\nLatitude: " + unchecked((int)U32(p, 1)) / 1000000d + "\nLongitude: " + unchecked((int)U32(p, 5)) / 1000000d);
            else if (tx && p[0] == 31 && p.Length >= 2) text.Append("\nChannel: " + p[1]);
            else if (tx && (p[0] == 15 || p[0] == 30) && p.Length >= 33) text.Append("\nPublic key: " + Hex(p, 1, 32));
            else if (!tx && p[0] == 12 && p.Length >= 3) text.Append("\nBattery: " + (p[1] | p[2] << 8) + " mV");
            else if (!tx && p[0] == 6 && p.Length >= 10) text.Append("\nType: " + p[1] + " | Tag: " + U32(p, 2) + " | Timeout: " + U32(p, 6) + " ms");
            else if (!tx && p[0] == 1 && p.Length >= 2) text.Append("\nError code: " + p[1]);
            else if (tx && p[0] == 26 && p.Length >= 33) text.Append("\nPublic key: " + Hex(p, 1, 32) + "\nPassword bytes: " + (p.Length - 33));
            else if (p.Length > 1) text.Append("\nData: " + Hex(p, 1, p.Length - 1));
            return text.ToString();
        }
    }
    public sealed partial class MeshtasticClient
    {
        public event EventHandler<CommunicationFrameEventArgs> CommunicationFrame;
        private void ReportCommunicationFrame(MeshProtocol protocol, string transport, string direction, byte[] raw, byte[] payload)
        {
            if (CommunicationFrame == null) return;
            if (String.Equals(transport, "Bluetooth LE", StringComparison.OrdinalIgnoreCase)) raw = payload;
            Raise(CommunicationFrame, new CommunicationFrameEventArgs(DateTime.UtcNow, direction, protocol, transport, raw, payload, Device.MyNode == null ? 0u : Device.MyNode.MyNodeNum, Device.NodeId));
        }
    }
}
