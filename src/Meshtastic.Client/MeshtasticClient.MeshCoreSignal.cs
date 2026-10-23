using System;
using System.Collections.Concurrent;
using System.Linq;
namespace Meshtastic.Client
{
    public sealed partial class MeshtasticClient
    {
        internal sealed class MeshCoreAdvertSignal
        {
            public string Key;
            public int Rssi;
            public float Snr;
            public DateTime ReceivedUtc;
        }
        private readonly ConcurrentDictionary<string, MeshCoreAdvertSignal> _meshCoreAdvertSignals = new ConcurrentDictionary<string, MeshCoreAdvertSignal>();
        internal static MeshCoreAdvertSignal DecodeMeshCoreAdvertSignal(byte[] frame)
        {
            if (frame == null || frame.Length < 5 || frame[0] != 0x88) return null;
            var header = frame[3];
            if ((header >> 6) != 0 || ((header >> 2) & 15) != 4) return null;
            var route = header & 3;
            var offset = (route == 0 || route == 3) ? 8 : 4;
            if (frame.Length <= offset) return null;
            var path = frame[offset++];
            if (!DecodeMeshCoreHops(path).HasValue) return null;
            offset += (path & 63) * ((path >> 6) + 1);
            // Advert: public key (32), timestamp (4), signature (64).
            if (frame.Length < offset + 100) return null;
            return new MeshCoreAdvertSignal { Key = Hex(frame, offset, 32), Rssi = (sbyte)frame[2], Snr = (sbyte)frame[1] / 4f, ReceivedUtc = DateTime.UtcNow };
        }
        private void HandleMeshCoreRxLog(byte[] frame)
        {
            var signal = DecodeMeshCoreAdvertSignal(frame);
            if (signal == null) return;
            // Bound pending signals, including adverts not added as contacts.
            if (_meshCoreAdvertSignals.Count > 2048)
                foreach (var item in _meshCoreAdvertSignals.Where(item => item.Value.ReceivedUtc < DateTime.UtcNow.AddSeconds(-30)).ToArray())
                { MeshCoreAdvertSignal ignored; _meshCoreAdvertSignals.TryRemove(item.Key, out ignored); }
            _meshCoreAdvertSignals[signal.Key] = signal;
            DateTime received;
            if (_meshCoreLastReception.TryGetValue(signal.Key, out received) && received > DateTime.UtcNow.AddSeconds(-5))
                ApplyMeshCoreAdvertSignal(signal.Key);
        }
        private void ApplyMeshCoreAdvertSignal(string key)
        {
            MeshCoreAdvertSignal signal;
            if (!_meshCoreAdvertSignals.TryGetValue(key, out signal) || signal.ReceivedUtc < DateTime.UtcNow.AddSeconds(-5)) return;
            MeshNode node;
            if (!_nodes.TryGetValue(ResolveMeshCoreIdentity(key), out node)) return;
            node.UpdateRadioMetrics(signal.Rssi, signal.Snr);
            Raise(NodeUpdated, new NodeEventArgs(node));
        }
    }
}
