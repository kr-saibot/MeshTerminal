using System;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Meshtastic.Client
{
    public sealed partial class MeshtasticClient
    {
        private MeshCoreSession _clockSynchronizedSession;
        private async Task<uint> ReadMeshCoreClockAsync(MeshCoreSession session, CancellationToken token)
        {
            var reply = await session.CommandAsync(new byte[] { 5 }, new byte[] { 9 }, token).ConfigureAwait(false);
            RequireMeshCoreLength(reply, 5);
            var seconds = MeshCoreSession.ReadUInt32(reply, 1);
            Device.DeviceClockUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
            Device.DeviceClockReadAtUtc = DateTime.UtcNow;
            Raise(DeviceInfoUpdated, EventArgs.Empty);
            return seconds;
        }
        private async Task SynchronizeMeshCoreClockAsync(MeshCoreSession session, CancellationToken token)
        {
            if (ReferenceEquals(session, _clockSynchronizedSession)) return;
            Device.DeviceClockSyncStatus = "Not synchronized";
            var deviceTime = await ReadMeshCoreClockAsync(session, token).ConfigureAwait(false);
            var pcTime = checked((uint)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
            if (deviceTime <= pcTime)
            {
                try { await session.CommandAsync(new byte[] { 6 }.Concat(BitConverter.GetBytes(pcTime)).ToArray(), new byte[] { 0 }, token).ConfigureAwait(false); }
                catch (MeshCoreCommandException ex) when (ex.Command == 6 && ex.ErrorCode == 6)
                { Debug("MeshCore clock setting rejected; checking actual device time."); }
            }
            await ReadMeshCoreClockAsync(session, token).ConfigureAwait(false);
            var difference = (Device.DeviceClockUtc.Value - Device.DeviceClockReadAtUtc.Value).TotalSeconds;
            Device.DeviceClockSyncStatus = Math.Abs(difference) <= 2 ? "Synchronized with PC"
                : difference > 0 ? "Device clock is ahead; firmware does not allow setting it backwards"
                : "Device clock could not be synchronized";
            _clockSynchronizedSession = session;
            Raise(DeviceInfoUpdated, EventArgs.Empty);
        }
        public async Task RefreshMeshCoreBatteryAsync(CancellationToken token = default(CancellationToken))
        {
            if (!IsConnected || Protocol != MeshProtocol.MeshCore || _loadingMeshCoreContacts) return;
            await _meshCore.CommandAsync(new byte[] { 20 }, new byte[] { 12 }, token).ConfigureAwait(false);
        }
        public async Task RefreshRadioStatisticsAsync(CancellationToken token = default(CancellationToken))
        {
            if (!IsConnected || Protocol != MeshProtocol.MeshCore || Device.MeshCoreProtocolVersion.GetValueOrDefault() < 8 || _loadingMeshCoreContacts) return;
            await _meshCore.CommandAsync(new byte[] { 56, 1 }, new byte[] { 24 }, token).ConfigureAwait(false);
        }
        public async Task RefreshOwnDeviceInfoAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!IsConnected) throw new InvalidOperationException("Connect to the device first.");
            if (Protocol != MeshProtocol.MeshCore) { await RequestFullStateAsync(cancellationToken).ConfigureAwait(false); return; }
            var session = _meshCore;
            await session.CommandAsync(new byte[] { 22, 3 }, new byte[] { 13 }, cancellationToken).ConfigureAwait(false);
            var name = Encoding.UTF8.GetBytes("ConsoleClient");
            var command = new byte[8 + name.Length]; command[0] = 1; Array.Copy(name, 0, command, 8, name.Length);
            await session.CommandAsync(command, new byte[] { 5 }, cancellationToken).ConfigureAwait(false);
            await session.CommandAsync(new byte[] { 20 }, new byte[] { 12 }, cancellationToken).ConfigureAwait(false);
            await ReadMeshCoreClockAsync(session, cancellationToken).ConfigureAwait(false);
            await RefreshRadioStatisticsAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
