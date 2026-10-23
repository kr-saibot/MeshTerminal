using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Protobufs;

namespace MeshTerminal.Client
{
    public sealed class MeshCoreRoomSession
    {
        public uint NodeNumber { get; internal set; }
        public bool CanPost { get; internal set; }
        public DateTime LoggedInUtc { get; internal set; }
    }

    public sealed partial class MeshtasticClient
    {
        private readonly ConcurrentDictionary<uint, MeshCoreRoomSession> _meshCoreRooms = new ConcurrentDictionary<uint, MeshCoreRoomSession>();
        public event EventHandler MeshCoreRoomsChanged;

        public MeshCoreRoomSession GetMeshCoreRoomSession(uint number)
        {
            MeshCoreRoomSession room;
            return IsConnected && _meshCoreRooms.TryGetValue(number, out room) ? room : null;
        }

        public int GetMaximumTextPayloadBytes(bool channelMessage, uint? destination)
        {
            MeshNode node;
            return Protocol == MeshProtocol.MeshCore && !channelMessage && destination.HasValue && _nodes.TryGetValue(destination.Value, out node) && node.MeshCoreContactType == 3 ? 151 : GetMaximumTextPayloadBytes(channelMessage);
        }

        private byte[] GetMeshCoreContactKey(uint number)
        {
            if (Protocol != MeshProtocol.MeshCore || !IsConnected || _meshCore == null) throw new InvalidOperationException("Connect to a MeshCore Companion device first.");
            byte[] key;
            if (!_meshCoreKeys.TryGetValue(number, out key)) throw new InvalidOperationException("Select a known MeshCore device contact.");
            if (_meshCoreKeys.Values.Count(value => Hex(value, 0, 6) == Hex(key, 0, 6)) != 1) throw new InvalidOperationException("Ambiguous contact key prefix.");
            return key;
        }

        public async Task<MeshCoreRoomSession> LoginMeshCoreRoomAsync(uint number, string password, CancellationToken token = default(CancellationToken))
        {
            var key = GetMeshCoreContactKey(number);
            MeshNode node;
            if (!_nodes.TryGetValue(number, out node) || node.MeshCoreContactType != 3) throw new InvalidOperationException("Select a Room Server contact to log in.");
            var bytes = Encoding.UTF8.GetBytes(password ?? "");
            if (bytes.Length > 139 || (password ?? "").IndexOf('\0') >= 0) throw new ArgumentException("The room password is too long or contains a NUL character.", "password");
            var session = _meshCore;
            // Login uses the Companion RTC, rather than an app-supplied timestamp.
            // Move an outdated RTC forward to avoid room replay-protection failures.
            var time = await session.CommandAsync(new byte[] { 5 }, new byte[] { 9 }, token).ConfigureAwait(false);
            var now = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            if (MeshCoreSession.ReadUInt32(time, 1) < now)
                await session.CommandAsync(new byte[] { 6 }.Concat(BitConverter.GetBytes(now)).ToArray(), new byte[] { 0 }, token).ConfigureAwait(false);
            var command = new byte[] { 26 }.Concat(key).Concat(bytes).ToArray();
            byte[] response;
            try
            {
                response = await session.RemoteRequestAsync(command, (sent, frame) => frame.Length >= 8 && (frame[0] == 0x85 || frame[0] == 0x86) && Hex(frame, 2, 6) == Hex(key, 0, 6), token).ConfigureAwait(false);
            }
            finally { Array.Clear(command, 33, bytes.Length); Array.Clear(bytes, 0, bytes.Length); }
            if (response[0] == 0x86) throw new UnauthorizedAccessException("Room Server login was rejected.");
            // A disconnected old session must never authorize a newly opened radio.
            if (!IsConnected || !Object.ReferenceEquals(session, _meshCore)) throw new IOException("The connection changed during room login.");
            var room = new MeshCoreRoomSession { NodeNumber = number, CanPost = response.Length >= 13 ? (response[12] & 3) >= 2 : response[1] != 2, LoggedInUtc = DateTime.UtcNow };
            _meshCoreRooms[number] = room;
            Raise(MeshCoreRoomsChanged, EventArgs.Empty);
            return room;
        }

        public async Task<MeshTelemetry> RequestMeshCoreTelemetryAsync(uint? number = null, CancellationToken token = default(CancellationToken))
        {
            if (Protocol != MeshProtocol.MeshCore || !IsConnected || Device.MyNode == null) throw new InvalidOperationException("Connect and synchronize MeshCore first.");
            var target = number ?? Device.MyNode.MyNodeNum;
            var key = GetMeshCoreContactKey(target);
            var session = _meshCore;
            byte[] payload;
            if (target == Device.MyNode.MyNodeNum)
            {
                var response = await session.CommandAsync(new byte[] { 39, 0, 0, 0 }, new byte[] { 0x8B }, token).ConfigureAwait(false);
                RequireMeshCoreLength(response, 8);
                if (Hex(response, 2, 6) != Hex(key, 0, 6)) throw new InvalidDataException("Telemetry reply belongs to another device.");
                payload = response.Skip(8).ToArray();
            }
            else
            {
                // Modern binary request includes a tag in the response. This
                // prevents a late reply being assigned to a different request.
                var command = new byte[] { 50 }.Concat(key).Concat(new byte[] { 3, 0, 0, 0 }).ToArray();
                var response = await session.RemoteRequestAsync(command, (sent, frame) => frame.Length >= 6 && frame[0] == 0x8C && MeshCoreSession.ReadUInt32(frame, 2) == MeshCoreSession.ReadUInt32(sent, 2), token).ConfigureAwait(false);
                payload = response.Skip(6).ToArray();
            }
            var parsed = MeshCoreLpp.Decode(payload, target != Device.MyNode.MyNodeNum);
            var metrics = new EnvironmentMetrics();
            if (parsed.Voltage.HasValue) metrics.Voltage = (float)parsed.Voltage.Value;
            if (parsed.Temperature.HasValue) metrics.Temperature = (float)parsed.Temperature.Value;
            if (parsed.Humidity.HasValue) metrics.RelativeHumidity = (float)parsed.Humidity.Value;
            if (parsed.Pressure.HasValue) metrics.BarometricPressure = (float)parsed.Pressure.Value;
            var telemetry = new MeshTelemetry(new MeshPacket { From = target }, new Telemetry { EnvironmentMetrics = metrics }) { MeshCoreRawTelemetry = parsed.Description };
            MeshNode node;
            if (parsed.Position != null && _nodes.TryGetValue(target, out node)) { parsed.Position.Time = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; node.Value.Position = parsed.Position; Raise(NodeUpdated, new NodeEventArgs(node)); RecordMeshCorePosition(target, parsed.Position); }
            Raise(TelemetryReceived, new MeshTelemetryEventArgs(telemetry));
            return telemetry;
        }
    }

    // CayenneLPP uses big-endian sensor fields, unlike Companion frame integers.
    // Preserve all channels and unsupported fields in the detailed raw view.
    internal sealed class MeshCoreLpp
    {
        public double? Voltage, Temperature, Humidity, Pressure;
        public Position Position;
        public string Description;
        public static MeshCoreLpp Decode(byte[] payload, bool allowEncryptionPadding = false)
        {
            var result = new MeshCoreLpp(); var lines = new List<string>(); var offset = 0;
            while (offset < payload.Length)
            {
                // Remote encrypted replies retain zero-fill from the last AES block.
                // Check at record boundaries only: do not trim zero-valued sensors.
                if (allowEncryptionPadding && lines.Count > 0 && payload.Skip(offset).All(value => value == 0))
                {
                    if (payload.Length - offset > 15) throw new InvalidDataException("Invalid MeshCore telemetry padding.");
                    break;
                }
                if (payload.Length - offset < 2) throw new InvalidDataException("Truncated MeshCore sensor header at byte " + offset + ". Received: " + BitConverter.ToString(payload));
                var channel = payload[offset++]; var type = payload[offset++];
                int size;
                switch (type)
                {
                    case 0: case 1: case 102: case 104: case 120: case 142: size = 1; break;
                    case 2: case 3: case 101: case 103: case 115: case 116: case 117: case 121: case 125: case 128: case 132: size = 2; break;
                    case 100: case 118: case 130: case 131: case 133: size = 4; break;
                    case 113: case 134: size = 6; break;
                    case 135: size = 3; break;
                    case 136: size = 9; break;
                    default: lines.Add("Ch " + channel + ": unsupported type " + type + "; remaining " + BitConverter.ToString(payload, offset)); offset = payload.Length; continue;
                }
                if (payload.Length - offset < size) throw new InvalidDataException("Truncated MeshCore sensor type " + type + ".");
                var unsigned = size <= 4 ? BigEndian(payload, offset, size, false) : 0;
                var signed = size <= 4 ? BigEndian(payload, offset, size, true) : 0;
                string text;
                switch (type)
                {
                    case 116: var voltage = unsigned / 100d; if (!result.Voltage.HasValue) result.Voltage = voltage; text = "Voltage " + Format(voltage) + " V"; break;
                    case 103: var temperature = signed / 10d; result.Temperature = temperature; text = "Temperature " + Format(temperature) + " °C"; break;
                    case 104: var humidity = unsigned / 2d; result.Humidity = humidity; text = "Humidity " + Format(humidity) + " %"; break;
                    case 115: var pressure = unsigned / 10d; result.Pressure = pressure; text = "Pressure " + Format(pressure) + " hPa"; break;
                    case 136:
                        var latitude = BigEndian(payload, offset, 3, true) / 10000d; var longitude = BigEndian(payload, offset + 3, 3, true) / 10000d; var altitude = BigEndian(payload, offset + 6, 3, true) / 100d;
                        if (latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180) result.Position = new Position { LatitudeI = (int)Math.Round(latitude * 10000000), LongitudeI = (int)Math.Round(longitude * 10000000), Altitude = (int)Math.Round(altitude) };
                        text = "GPS " + Format(latitude) + ", " + Format(longitude) + "; altitude " + Format(altitude) + " m"; break;
                    case 2: case 3: text = "Analog " + Format(signed / 100d); break;
                    case 101: text = "Illuminance " + unsigned + " lux"; break;
                    case 117: text = "Current " + Format(unsigned / 1000d) + " A"; break;
                    case 118: text = "Frequency " + unsigned + " Hz"; break;
                    case 120: text = "Percentage " + unsigned + " %"; break;
                    case 121: text = "Altitude " + signed + " m"; break;
                    case 125: text = "Concentration " + unsigned + " ppm"; break;
                    case 128: text = "Power " + unsigned + " W"; break;
                    case 130: text = "Distance " + Format(unsigned / 1000d) + " m"; break;
                    case 131: text = "Energy " + Format(unsigned / 1000d) + " kWh"; break;
                    case 132: text = "Direction " + unsigned + "°"; break;
                    case 133: text = "Unix time " + unsigned; break;
                    case 113: case 134: var factor = type == 113 ? 1000d : 100d; text = (type == 113 ? "Acceleration " : "Gyroscope ") + String.Join(", ", Enumerable.Range(0, 3).Select(axis => Format(BigEndian(payload, offset + axis * 2, 2, true) / factor))); break;
                    default: text = "Type " + type + ": " + (size <= 4 ? unsigned.ToString(CultureInfo.InvariantCulture) : BitConverter.ToString(payload, offset, size)); break;
                }
                lines.Add("Ch " + channel + ": " + text); offset += size;
            }
            result.Description = String.Join("\n", lines) + "\nCayenneLPP: " + BitConverter.ToString(payload);
            return result;
        }
        private static long BigEndian(byte[] bytes, int offset, int count, bool signed)
        {
            long value = 0; for (var index = 0; index < count; index++) value = (value << 8) | bytes[offset + index];
            if (signed && (bytes[offset] & 128) != 0) value -= 1L << (count * 8);
            return value;
        }
        private static string Format(double value) { return value.ToString("0.####", CultureInfo.InvariantCulture); }
    }
}
