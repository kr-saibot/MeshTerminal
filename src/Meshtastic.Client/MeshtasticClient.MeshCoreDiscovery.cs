using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
namespace Meshtastic.Client
{
    public sealed class MeshCoreRepeaterDiscoveryResult
    {
        public string PublicKey { get; internal set; }
        public string Name { get; internal set; }
        public int Rssi { get; internal set; }
        public float ReceivedSnr { get; internal set; }
        public float UplinkSnr { get; internal set; }
    }
    public sealed partial class MeshtasticClient
    {
        private readonly object _repeaterDiscoveryGate = new object();
        private uint _repeaterDiscoveryTag;
        private DateTime _repeaterDiscoveryUntil;
        private MeshCoreSession _repeaterDiscoverySession;
        private Dictionary<string, MeshCoreRepeaterDiscoveryResult> _repeaterDiscoveryResults;
        private Action<MeshCoreRepeaterDiscoveryResult> _repeaterDiscoveryCallback;

        public async Task<IList<MeshCoreRepeaterDiscoveryResult>> DiscoverMeshCoreRepeatersAsync(TimeSpan duration, Action<MeshCoreRepeaterDiscoveryResult> onResult = null, CancellationToken token = default(CancellationToken))
        {
            if (Protocol != MeshProtocol.MeshCore || !IsConnected || Device.MyNode == null) throw new InvalidOperationException("Connect and synchronize MeshCore first.");
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException("duration");
            if (Device.MeshCoreProtocolVersion.HasValue && Device.MeshCoreProtocolVersion.Value < 8) throw new NotSupportedException("Repeater discovery requires a newer MeshCore companion firmware (protocol v8 or later).");
            token.ThrowIfCancellationRequested();
            var session = _meshCore;
            var lifetime = _lifetime;
            var random = new byte[4];
            using (var generator = RandomNumberGenerator.Create()) do { generator.GetBytes(random); } while (BitConverter.ToUInt32(random, 0) == 0);
            var tag = BitConverter.ToUInt32(random, 0);
            lock (_repeaterDiscoveryGate)
            {
                if (_repeaterDiscoveryTag != 0) throw new InvalidOperationException("A repeater search is already running.");
                _repeaterDiscoveryResults = new Dictionary<string, MeshCoreRepeaterDiscoveryResult>();
                _repeaterDiscoveryTag = tag; _repeaterDiscoverySession = session;
                // Allow the command to wait for any currently running device operation.
                _repeaterDiscoveryUntil = DateTime.UtcNow.AddMinutes(1);
                _repeaterDiscoveryCallback = onResult;
            }
            try
            {
                // 55 = zero-hop control data. 0x80 = discovery request;
                // filter bit 2 = repeaters; request full public keys, since=0.
                var command = new byte[] { 55, 0x80, 4 }.Concat(random).Concat(new byte[4]).ToArray();
                // Finish the ACK even if the user closes the results dialog:
                // cancelling an in-flight Companion command closes its transport.
                await session.CommandAsync(command, new byte[] { 0 }, lifetime.Token).ConfigureAwait(false);
                lock (_repeaterDiscoveryGate) _repeaterDiscoveryUntil = DateTime.UtcNow.Add(duration);
                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
                    await Task.Delay(duration, wait.Token).ConfigureAwait(false);
                lock (_repeaterDiscoveryGate) return _repeaterDiscoveryResults.Values.OrderBy(result => result.Name).ThenBy(result => result.PublicKey).ToList();
            }
            finally
            {
                lock (_repeaterDiscoveryGate) { _repeaterDiscoveryTag = 0; _repeaterDiscoverySession = null; _repeaterDiscoveryCallback = null; _repeaterDiscoveryResults = null; }
            }
        }
        private void HandleMeshCoreDiscoveryResponse(byte[] frame)
        {
            // Push header: code, SNR*4, RSSI, path. Payload:
            // type, uplink SNR*4, tag (4), public key (32).
            if (frame.Length < 42 || frame[0] != 0x8E || frame[4] != 0x92 || frame[3] != 0) return;
            MeshCoreRepeaterDiscoveryResult result;
            Action<MeshCoreRepeaterDiscoveryResult> callback;
            lock (_repeaterDiscoveryGate)
            {
                if (_repeaterDiscoveryTag == 0 || !ReferenceEquals(_repeaterDiscoverySession, _meshCore) || DateTime.UtcNow > _repeaterDiscoveryUntil || MeshCoreSession.ReadUInt32(frame, 6) != _repeaterDiscoveryTag) return;
                var key = Hex(frame, 10, 32);
                var node = _nodes.Values.FirstOrDefault(candidate => String.Equals(candidate.Id, key, StringComparison.OrdinalIgnoreCase));
                result = new MeshCoreRepeaterDiscoveryResult { PublicKey = key, Name = node == null ? "Unknown repeater" : (!String.IsNullOrWhiteSpace(node.LongName) ? node.LongName : node.ShortName ?? "Unknown repeater"), Rssi = (sbyte)frame[2], ReceivedSnr = (sbyte)frame[1] / 4f, UplinkSnr = (sbyte)frame[5] / 4f };
                _repeaterDiscoveryResults[key] = result;
                callback = _repeaterDiscoveryCallback;
            }
            try { if (callback != null) callback(result); }
            catch (Exception ex) { Debug("Repeater discovery callback failed: " + ex.Message); }
        }
    }
}
