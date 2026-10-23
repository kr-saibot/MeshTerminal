using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Meshtastic.Client
{
    public sealed partial class MeshtasticClient
    {
        private readonly ConcurrentDictionary<string, byte[]> _meshCoreDeviceContacts = new ConcurrentDictionary<string, byte[]>();
        private readonly ConcurrentDictionary<string, byte[]> _meshCoreContactFrames = new ConcurrentDictionary<string, byte[]>();
        private MeshCoreSession _meshCoreContactsSession;
        private volatile bool _meshCoreContactsKnown;
        public event EventHandler MeshCoreDeviceContactsChanged;
        public bool? IsMeshCoreContactSaved(string identity)
        {
            if (Protocol != MeshProtocol.MeshCore || !IsConnected || !ReferenceEquals(_meshCoreContactsSession, _meshCore)) return null;
            if (identity != null && _meshCoreDeviceContacts.ContainsKey(identity.ToLowerInvariant())) return true;
            return _meshCoreContactsKnown ? (bool?)false : null;
        }
        private void ResetMeshCoreContactSnapshot()
        {
            _meshCoreContactsKnown = false;
            _meshCoreContactsSession = _meshCore;
            _meshCoreDeviceContacts.Clear();
            Raise(MeshCoreDeviceContactsChanged, EventArgs.Empty);
        }
        private void RecordMeshCoreContactFrame(byte[] frame)
        {
            var identity = Hex(frame, 1, 32);
            _meshCoreContactFrames[identity] = (byte[])frame.Clone();
            if (frame[0] == 3) _meshCoreDeviceContacts[identity] = frame.Skip(1).Take(32).ToArray();
        }
        private void ForgetMeshCoreDeviceContact(string identity)
        {
            byte[] ignored; _meshCoreDeviceContacts.TryRemove(identity, out ignored);
            Raise(MeshCoreDeviceContactsChanged, EventArgs.Empty);
        }
        private static byte[] ParseMeshCorePublicKey(string identity)
        {
            if (identity == null || !Regex.IsMatch(identity, @"\A[0-9a-fA-F]{64}\z")) throw new ArgumentException("Enter the full public key: 64 hexadecimal characters.");
            var key = Enumerable.Range(0, 32).Select(i => Convert.ToByte(identity.Substring(i * 2, 2), 16)).ToArray();
            if (key.All(b => b == 0)) throw new ArgumentException("The public key must not be all zeroes.");
            return key;
        }
        public async Task SaveMeshCoreContactAsync(string identity, string name, byte type = 1, double? latitude = null, double? longitude = null, CancellationToken token = default(CancellationToken))
        {
            var key = ParseMeshCorePublicKey(identity); identity = Hex(key, 0, 32);
            var nameBytes = Encoding.UTF8.GetBytes(name ?? "");
            if (String.IsNullOrWhiteSpace(name) || name.IndexOf('\0') >= 0 || nameBytes.Length > 31) throw new ArgumentException("Name must contain 1-31 UTF-8 bytes and no NUL.");
            if (type < 1 || type > 4) throw new ArgumentException("Select chat, repeater, room or sensor.");
            if (latitude.HasValue != longitude.HasValue || (latitude.HasValue && (Double.IsNaN(latitude.Value) || Double.IsInfinity(latitude.Value) || Math.Abs(latitude.Value) > 90 || Double.IsNaN(longitude.Value) || Double.IsInfinity(longitude.Value) || Math.Abs(longitude.Value) > 180))) throw new ArgumentException("Enter valid latitude and longitude, or leave both empty.");
            await _meshCoreSync.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (Protocol != MeshProtocol.MeshCore || !IsConnected || _meshCore == null) throw new InvalidOperationException("Connect to MeshCore first.");
                if (Device.MyNode != null && _meshCoreKeys.TryGetValue(Device.MyNode.MyNodeNum, out var ownKey) && ownKey.SequenceEqual(key)) throw new ArgumentException("The own device cannot be added as a contact.");
                byte[] cached; var command = _meshCoreContactFrames.TryGetValue(identity, out cached) ? (byte[])cached.Clone() : new byte[148];
                command[0] = 9; key.CopyTo(command, 1); command[33] = type;
                if (cached == null) command[35] = 255; // Unknown path: firmware discovers it when sending.
                Array.Clear(command, 100, 32); nameBytes.CopyTo(command, 100);
                if (latitude.HasValue) { BitConverter.GetBytes((int)Math.Round(latitude.Value * 1000000)).CopyTo(command, 136); BitConverter.GetBytes((int)Math.Round(longitude.Value * 1000000)).CopyTo(command, 140); }
                try { await _meshCore.CommandAsync(command, new byte[] { 0 }, token).ConfigureAwait(false); }
                catch (MeshCoreCommandException ex) when (ex.Command == 9 && ex.ErrorCode == 3)
                { throw new IOException("The device contact list is full. Remove a contact from the device, then save this PC contact again. PC records and chat history are retained.", ex); }
                var reply = await _meshCore.CommandAsync(new byte[] { 30 }.Concat(key).ToArray(), new byte[] { 3 }, token).ConfigureAwait(false);
                RequireMeshCoreLength(reply, 148);
                if (!reply.Skip(1).Take(32).SequenceEqual(key) || reply[33] != type || MeshCoreString(reply, 100, 32) != name) throw new IOException("Contact save could not be verified. Refresh the device list.");
                Raise(MeshCoreDeviceContactsChanged, EventArgs.Empty);
            }
            finally { _meshCoreSync.Release(); }
        }
        public async Task RemoveMeshCoreContactAsync(string identity, CancellationToken token = default(CancellationToken))
        {
            var key = ParseMeshCorePublicKey(identity);
            await _meshCoreSync.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (Protocol != MeshProtocol.MeshCore || !IsConnected || _meshCore == null) throw new InvalidOperationException("Connect to MeshCore first.");
                await _meshCore.CommandAsync(new byte[] { 15 }.Concat(key).ToArray(), new byte[] { 0 }, token).ConfigureAwait(false);
                ForgetMeshCoreDeviceContact(Hex(key, 0, 32));
                foreach (var pair in _meshCoreKeys.Where(p => p.Value.SequenceEqual(key)).ToArray()) { MeshCoreRoomSession room; _meshCoreRooms.TryRemove(pair.Key, out room); }
            }
            finally { _meshCoreSync.Release(); }
        }
        public async Task<int> RemoveAllMeshCoreContactsAsync(CancellationToken token = default(CancellationToken), Action<int, int> progress = null)
        {
            if (!IsConnected || !ReferenceEquals(_meshCoreContactsSession, _meshCore) || !_meshCoreContactsKnown) throw new InvalidOperationException("First refresh the complete device contact list.");
            var session = _meshCore;
            var keys = _meshCoreDeviceContacts.Keys.ToArray();
            var count = 0;
            progress?.Invoke(0, keys.Length);
            foreach (var key in keys)
            {
                token.ThrowIfCancellationRequested();
                try { if (!ReferenceEquals(session, _meshCore)) throw new IOException("The device connection changed; deletion stopped."); await RemoveMeshCoreContactAsync(key, token).ConfigureAwait(false); count++; progress?.Invoke(count, keys.Length); }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { throw new IOException("Removed " + count + " of " + keys.Length + " device contacts. " + ex.Message, ex); }
            }
            return count;
        }
    }
}