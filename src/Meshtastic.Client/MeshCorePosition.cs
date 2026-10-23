using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Protobufs;

namespace Meshtastic.Client
{
    public sealed class MeshCorePositionEventArgs : EventArgs
    {
        public MeshCorePositionEventArgs(uint number, Position position, DateTime receivedUtc)
        { NodeNumber = number; Position = position; ReceivedUtc = receivedUtc; }
        public uint NodeNumber { get; private set; }
        public Position Position { get; private set; }
        public DateTime ReceivedUtc { get; private set; }
    }

    public sealed partial class MeshtasticClient
    {
        private async Task<Dictionary<string, string>> GetMeshCoreSensorSettingsAsync(MeshCoreSession session, CancellationToken token)
        {
            var response = await session.CommandAsync(new byte[] { 40 }, new byte[] { 21 }, token).ConfigureAwait(false);
            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in Encoding.UTF8.GetString(response, 1, response.Length - 1).TrimEnd((char)0).Split(','))
            {
                var split = entry.IndexOf(':');
                if (split > 0) settings[entry.Substring(0, split)] = entry.Substring(split + 1);
            }
            return settings;
        }

        private Task<byte[]> SetMeshCoreSensorSettingAsync(MeshCoreSession session, string name, string value, CancellationToken token)
        { return session.CommandAsync(new byte[] { 41 }.Concat(Encoding.ASCII.GetBytes(name + ":" + value)).ToArray(), new byte[] { 0 }, token); }

        private Task<byte[]> RefreshMeshCoreSelfAsync(MeshCoreSession session, CancellationToken token)
        { return session.CommandAsync(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }.Concat(Encoding.UTF8.GetBytes("ConsoleClient")).ToArray(), new byte[] { 5 }, token); }

        private async Task SetMeshCorePositionAsync(double latitude, double longitude, int? altitude, CancellationToken token)
        {
            if (altitude.HasValue) throw new NotSupportedException("MeshCore Companion does not support setting altitude.");
            var session = _meshCore;
            if (!IsConnected || session == null) throw new InvalidOperationException("Connect MeshCore first.");
            await _meshCoreSync.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(session, _meshCore)) throw new IOException("MeshCore connection changed.");
                var settings = await GetMeshCoreSensorSettingsAsync(session, token).ConfigureAwait(false);
                string gps;
                var disableGps = settings.TryGetValue("gps", out gps) && gps == "1";
                if (disableGps) await SetMeshCoreSensorSettingAsync(session, "gps", "0", token).ConfigureAwait(false);
                try
                {
                    var command = new byte[] { 14 }.Concat(BitConverter.GetBytes((int)Math.Round(latitude * 1000000)))
                        .Concat(BitConverter.GetBytes((int)Math.Round(longitude * 1000000))).ToArray();
                    await session.CommandAsync(command, new byte[] { 0 }, token).ConfigureAwait(false);
                }
                catch
                {
                    if (disableGps) await SetMeshCoreSensorSettingAsync(session, "gps", gps, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                var response = await RefreshMeshCoreSelfAsync(session, token).ConfigureAwait(false);
                RequireMeshCoreLength(response, 44);
                if ((int)MeshCoreSession.ReadUInt32(response, 36) != (int)Math.Round(latitude * 1000000) ||
                    (int)MeshCoreSession.ReadUInt32(response, 40) != (int)Math.Round(longitude * 1000000))
                    throw new IOException("MeshCore did not confirm the requested position.");
                Raise(DeviceInfoUpdated, EventArgs.Empty);
            }
            finally { _meshCoreSync.Release(); }
        }

        private async Task UseMeshCoreGpsAsync(CancellationToken token)
        {
            var session = _meshCore;
            if (!IsConnected || session == null) throw new InvalidOperationException("Connect MeshCore first.");
            await _meshCoreSync.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(session, _meshCore)) throw new IOException("MeshCore connection changed.");
                var settings = await GetMeshCoreSensorSettingsAsync(session, token).ConfigureAwait(false);
                if (!settings.ContainsKey("gps")) throw new NotSupportedException("This MeshCore device/firmware does not provide a GPS receiver setting.");
                string interval;
                if (settings.TryGetValue("gps_interval", out interval) && interval == "0")
                    await SetMeshCoreSensorSettingAsync(session, "gps_interval", "60", token).ConfigureAwait(false);
                await SetMeshCoreSensorSettingAsync(session, "gps", "1", token).ConfigureAwait(false);
                var verified = await GetMeshCoreSensorSettingsAsync(session, token).ConfigureAwait(false);
                string enabled;
                if (!verified.TryGetValue("gps", out enabled) || enabled != "1") throw new IOException("MeshCore did not confirm GPS activation.");
                await RefreshMeshCoreSelfAsync(session, token).ConfigureAwait(false);
                Raise(DeviceInfoUpdated, EventArgs.Empty);
            }
            finally { _meshCoreSync.Release(); }
        }

        private async Task SetAndAdvertiseMeshCorePositionAsync(double latitude, double longitude, int? altitude, CancellationToken token)
        {
            await SetMeshCorePositionAsync(latitude, longitude, altitude, token).ConfigureAwait(false);
            await SendAdvertAsync(true, true, token).ConfigureAwait(false);
        }
    }
}