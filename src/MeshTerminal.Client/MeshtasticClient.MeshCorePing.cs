using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeshTerminal.Client
{
    public sealed partial class MeshtasticClient
    {
        public async Task<TimeSpan> CheckMeshCoreReachabilityAsync(uint number, CancellationToken token = default(CancellationToken))
        {
            var key = GetMeshCoreContactKey(number);
            if (Device.MyNode != null && number == Device.MyNode.MyNodeNum) throw new InvalidOperationException("Select another node to test.");
            if (IsMeshCoreContactSaved(Hex(key, 0, 32)) != true) throw new InvalidOperationException("Save this contact to the device first.");
            MeshNode node;
            if (!_nodes.TryGetValue(number, out node) || node.MeshCoreContactType < 1 || node.MeshCoreContactType > 4)
                throw new InvalidOperationException("The contact type is unknown. Refresh contacts first.");
            var session = _meshCore;
            var watch = Stopwatch.StartNew();
            if (node.MeshCoreContactType == 1)
            {
                uint timestamp;
                lock (_gate)
                {
                    timestamp = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    timestamp = Math.Max(timestamp, _meshCoreLastTimestamp + 1); _meshCoreLastTimestamp = timestamp;
                }
                var command = new byte[] { 2, 0, 0 }.Concat(BitConverter.GetBytes(timestamp)).Concat(key.Take(6)).Concat(Encoding.UTF8.GetBytes("Ping (MeshTerminal)")).ToArray();
                var ack = await session.RemoteRequestAsync(command, (sent, frame) =>
                    frame.Length >= 9 && frame[0] == 0x82 && MeshCoreSession.ReadUInt32(sent, 2) != 0 &&
                    MeshCoreSession.ReadUInt32(frame, 1) == MeshCoreSession.ReadUInt32(sent, 2), token).ConfigureAwait(false);
                return TimeSpan.FromMilliseconds(MeshCoreSession.ReadUInt32(ack, 5));
            }
            await session.RemoteRequestAsync(new byte[] { 27 }.Concat(key).ToArray(), (sent, frame) =>
                frame.Length >= 8 && frame[0] == 0x87 && Hex(frame, 2, 6) == Hex(key, 0, 6), token).ConfigureAwait(false);
            return watch.Elapsed;
        }
    }
}
