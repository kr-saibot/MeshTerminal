using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace MeshTerminal.Client
{
    // RepeaterStats layout from MeshCore examples/simple_repeater/MyMesh.h.
    public sealed class MeshCoreRepeaterStatus
    {
        public uint NodeNumber { get; internal set; }
        public DateTime ReceivedUtc { get; internal set; }
        public double? BatteryVoltage { get; internal set; }
        public int TxQueueLength { get; internal set; }
        public int? NoiseFloorDbm { get; internal set; }
        public int? LastRssiDbm { get; internal set; }
        public float? LastSnrDb { get; internal set; }
        public uint? ReceivedPackets { get; internal set; }
        public uint? SentPackets { get; internal set; }
        public uint? TxAirtimeSeconds { get; internal set; }
        public uint? UptimeSeconds { get; internal set; }
        public uint? SentFlood { get; internal set; }
        public uint? SentDirect { get; internal set; }
        public uint? ReceivedFlood { get; internal set; }
        public uint? ReceivedDirect { get; internal set; }
        public ushort? ErrorFlags { get; internal set; }
        public ushort? DirectDuplicates { get; internal set; }
        public ushort? FloodDuplicates { get; internal set; }
        public uint? RxAirtimeSeconds { get; internal set; }
        public uint? ReceiveErrors { get; internal set; }
        internal static MeshCoreRepeaterStatus Parse(byte[] frame, uint number)
        {
            if (frame == null || frame.Length < 16 || frame[0] != 0x87) throw new InvalidDataException("Truncated MeshCore repeater status response.");
            Func<int, int> u16 = offset => frame[offset] | (frame[offset + 1] << 8);
            Func<int, uint?> u32 = offset => frame.Length >= offset + 4 ? (uint?)MeshCoreSession.ReadUInt32(frame, offset) : null;
            Func<int, ushort?> optional16 = offset => frame.Length >= offset + 2 ? (ushort?)u16(offset) : null;
            var voltage = u16(8); var noise = unchecked((short)u16(12)); var rssi = unchecked((short)u16(14));
            return new MeshCoreRepeaterStatus
            {
                NodeNumber = number, ReceivedUtc = DateTime.UtcNow,
                BatteryVoltage = voltage == 0 || voltage == 65535 ? (double?)null : voltage / 1000d,
                TxQueueLength = u16(10), NoiseFloorDbm = noise == 0 ? (int?)null : noise,
                LastRssiDbm = rssi == 0 ? (int?)null : rssi,
                ReceivedPackets = u32(16), SentPackets = u32(20), TxAirtimeSeconds = u32(24), UptimeSeconds = u32(28),
                SentFlood = u32(32), SentDirect = u32(36), ReceivedFlood = u32(40), ReceivedDirect = u32(44),
                ErrorFlags = optional16(48), LastSnrDb = frame.Length >= 52 ? unchecked((short)u16(50)) / 4f : (float?)null,
                DirectDuplicates = optional16(52), FloodDuplicates = optional16(54), RxAirtimeSeconds = u32(56), ReceiveErrors = u32(60)
            };
        }
        public override string ToString()
        {
            var culture = CultureInfo.InvariantCulture;
            Func<uint?, string> counter = value => value.HasValue ? value.Value.ToString(culture) : "not available";
            var text = new StringBuilder();
            text.AppendLine("Repeater status (" + ReceivedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", culture) + ")");
            text.AppendLine("Battery: " + (BatteryVoltage.HasValue ? BatteryVoltage.Value.ToString("F3", culture) + " V" : "not available"));
            text.AppendLine("Noise floor: " + (NoiseFloorDbm.HasValue ? NoiseFloorDbm.Value + " dBm" : "not available"));
            text.AppendLine("Last RSSI / SNR at repeater: " + (LastRssiDbm.HasValue ? LastRssiDbm.Value + " dBm" : "-") + " / " + (LastSnrDb.HasValue ? LastSnrDb.Value.ToString("0.##", culture) + " dB" : "-"));
            text.AppendLine("TX queue: " + TxQueueLength);
            text.AppendLine("Uptime: " + (UptimeSeconds.HasValue ? TimeSpan.FromSeconds(UptimeSeconds.Value).ToString() : "not available"));
            text.AppendLine("Packets RX / TX: " + counter(ReceivedPackets) + " / " + counter(SentPackets));
            text.AppendLine("Sent flood / direct: " + counter(SentFlood) + " / " + counter(SentDirect));
            text.AppendLine("Received flood / direct: " + counter(ReceivedFlood) + " / " + counter(ReceivedDirect));
            text.AppendLine("Airtime TX / RX: " + counter(TxAirtimeSeconds) + " / " + counter(RxAirtimeSeconds) + " seconds");
            text.AppendLine("Duplicates direct / flood: " + (DirectDuplicates?.ToString(culture) ?? "-") + " / " + (FloodDuplicates?.ToString(culture) ?? "-"));
            text.AppendLine("Receive errors: " + counter(ReceiveErrors) + " | Error flags: " + (ErrorFlags.HasValue ? "0x" + ErrorFlags.Value.ToString("X4", culture) : "-"));
            return text.ToString();
        }
    }
    public sealed partial class MeshtasticClient
    {
        public event Action<MeshCoreRepeaterStatus> MeshCoreRepeaterStatusReceived;

        private void ReceiveMeshCoreRepeaterStatus(byte[] frame)
        {
            if (frame.Length < 16) return;
            var prefix = Hex(frame, 2, 6);
            var matches = _meshCoreKeys.Where(pair => Hex(pair.Value, 0, 6) == prefix).Take(2).ToArray();
            MeshNode node;
            if (matches.Length != 1 || !_nodes.TryGetValue(matches[0].Key, out node) || node.MeshCoreContactType != 2) return;
            MeshCoreRepeaterStatusReceived?.Invoke(MeshCoreRepeaterStatus.Parse(frame, node.Number));
        }

        public async Task<MeshCoreRepeaterStatus> RequestMeshCoreRepeaterStatusAsync(uint number, CancellationToken token = default(CancellationToken))
        {
            var key = RepeaterKey(number);
            var session = _meshCore;
            var response = await session.RemoteRequestAsync(new byte[] { 27 }.Concat(key).ToArray(), (sent, frame) =>
                frame.Length >= 8 && frame[0] == 0x87 && Hex(frame, 2, 6) == Hex(key, 0, 6), token).ConfigureAwait(false);
            return MeshCoreRepeaterStatus.Parse(response, number);
        }
    }
}
