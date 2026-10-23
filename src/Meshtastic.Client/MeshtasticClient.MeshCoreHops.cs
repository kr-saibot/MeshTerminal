using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Meshtastic.Client
{
    public sealed partial class MeshtasticClient
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, uint> _meshCoreAdvertHops = new System.Collections.Concurrent.ConcurrentDictionary<string, uint>();

        internal static uint? DecodeMeshCoreHops(byte encodedPath)
        {
            var size = (encodedPath >> 6) + 1;
            var count = encodedPath & 63;
            return size <= 3 && size * count <= 64 ? (uint?)count : null;
        }

        private async Task RefreshMeshCoreAdvertPathAsync(MeshCoreSession session, byte[] key, CancellationToken token)
        {
            try
            {
                if (!ReferenceEquals(session, _meshCore) || !IsConnected) return;
                var reply = await session.CommandAsync(new byte[] { 42, 0 }.Concat(key).ToArray(), new byte[] { 22 }, token).ConfigureAwait(false);
                if (!ReferenceEquals(session, _meshCore)) return;
                RequireMeshCoreLength(reply, 6);
                var hops = DecodeMeshCoreHops(reply[5]);
                if (!hops.HasValue || reply.Length < 6 + (reply[5] & 63) * ((reply[5] >> 6) + 1)) return;
                var identity = Hex(key, 0, 32);
                _meshCoreAdvertHops[identity] = hops.Value;
                MeshNode node;
                if (_nodes.TryGetValue(ResolveMeshCoreIdentity(identity), out node))
                {
                    node.Value.HopsAway = hops.Value;
                    Raise(NodeUpdated, new NodeEventArgs(node));
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException ex) { Debug("MeshCore advert path unavailable: " + ex.Message); }
            catch (Exception ex) { Debug("MeshCore advert path refresh failed: " + ex.Message); }
        }
    }
}
