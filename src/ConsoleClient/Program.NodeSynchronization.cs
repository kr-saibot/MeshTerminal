using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace ConsoleClient
{
    internal static partial class Program
    {
        private static int _bufferNodeSynchronization;
        private static readonly SemaphoreSlim _nodePersistenceSynchronization = new SemaphoreSlim(1, 1);
        private static async Task ReceiveDeviceStateAsync(CancellationToken token)
        {
            await _nodePersistenceSynchronization.WaitAsync(token).ConfigureAwait(false);
            Interlocked.Exchange(ref _bufferNodeSynchronization, 1);
            _databaseWrites.Pause();
            try
            {
                await _mesh.RequestFullStateAsync(token).ConfigureAwait(false);
                if (_mesh.Protocol == Meshtastic.Client.MeshProtocol.MeshCore && !_mesh.LastNodeSynchronizationComplete && !_mesh.LastMeshCoreContactTransferEnded) return;
                var nodes = _mesh.Nodes.ToArray();
                Save(() =>
                {
                    foreach (var node in nodes) _store.AddOrUpdateNode(node);
                    Ui(QueueNodePageRefresh);
                }, "synchronized-nodes");
            }
            finally
            {
                Interlocked.Exchange(ref _bufferNodeSynchronization, 0);
                _databaseWrites.Resume();
                _nodePersistenceSynchronization.Release();
            }
        }
    }
}