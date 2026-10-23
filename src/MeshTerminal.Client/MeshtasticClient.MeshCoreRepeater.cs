using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeshTerminal.Client
{
    public sealed partial class MeshtasticClient
    {
        private sealed class RepeaterAuthorization
        {
            internal MeshCoreSession Session;
            internal uint Timestamp;
        }
        private readonly ConcurrentDictionary<uint, RepeaterAuthorization> _repeaterAuthorizations = new ConcurrentDictionary<uint, RepeaterAuthorization>();
        private readonly SemaphoreSlim _repeaterOperations = new SemaphoreSlim(1, 1);
        private int _repeaterTag;

        private byte[] RepeaterKey(uint number)
        {
            var key = GetMeshCoreContactKey(number);
            MeshNode node;
            if (!_nodes.TryGetValue(number, out node) || node.MeshCoreContactType != 2)
                throw new InvalidOperationException("Select a MeshCore repeater.");
            if (IsMeshCoreContactSaved(Hex(key, 0, 32)) != true)
                throw new InvalidOperationException("Save this repeater to the device first.");
            return key;
        }

        public async Task LoginMeshCoreRepeaterAsync(uint number, string password, CancellationToken token = default(CancellationToken))
        {
            await _repeaterOperations.WaitAsync(token).ConfigureAwait(false);
            byte[] bytes = null, command = null;
            try
            {
                RepeaterAuthorization ignored;
                _repeaterAuthorizations.TryRemove(number, out ignored);
                var key = RepeaterKey(number);
                bytes = Encoding.UTF8.GetBytes(password ?? "");
                if (bytes.Length > 139 || (password ?? "").IndexOf('\0') >= 0)
                    throw new ArgumentException("Password must fit in 139 UTF-8 bytes and contain no NUL.");
                var session = _meshCore;
                var time = await session.CommandAsync(new byte[] { 5 }, new byte[] { 9 }, token).ConfigureAwait(false);
                RequireMeshCoreLength(time, 5);
                var now = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                var clock = Math.Max(now, MeshCoreSession.ReadUInt32(time, 1));
                if (MeshCoreSession.ReadUInt32(time, 1) < now)
                    await session.CommandAsync(new byte[] { 6 }.Concat(BitConverter.GetBytes(now)).ToArray(), new byte[] { 0 }, token).ConfigureAwait(false);
                command = new byte[] { 26 }.Concat(key).Concat(bytes).ToArray();
                var response = await session.RemoteRequestAsync(command, (sent, frame) => frame.Length >= 8 && (frame[0] == 0x85 || frame[0] == 0x86) && Hex(frame, 2, 6) == Hex(key, 0, 6), token).ConfigureAwait(false);
                if (response[0] == 0x86) throw new UnauthorizedAccessException("Repeater login was rejected.");
                if (response.Length >= 13 && response[1] != 1) throw new UnauthorizedAccessException("This login has no administrator permission.");
                if (!IsConnected || !Object.ReferenceEquals(session, _meshCore)) throw new IOException("Connection changed during login. Log in again.");
                // Older firmware does not report permission bits; it enforces admin rights on each command.
                _repeaterAuthorizations[number] = new RepeaterAuthorization { Session = session, Timestamp = clock };
            }
            finally
            {
                if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
                if (command != null) Array.Clear(command, 0, command.Length);
                _repeaterOperations.Release();
            }
        }

        internal static string ParseRepeaterReply(byte[] frame, byte[] key, string tag)
        {
            if (frame == null || frame.Length == 0 || (frame[0] != 7 && frame[0] != 16)) return null;
            var offset = frame[0] == 16 ? 4 : 1;
            if (frame.Length < offset + 12 || Hex(frame, offset, 6) != Hex(key, 0, 6) || frame[offset + 7] != 1) return null;
            var text = Encoding.UTF8.GetString(frame, offset + 12, frame.Length - offset - 12).TrimEnd('\0');
            return text.StartsWith(tag, StringComparison.Ordinal) ? text.Substring(tag.Length) : null;
        }

        public async Task<string> SendMeshCoreRepeaterCommandAsync(uint number, string text, CancellationToken token = default(CancellationToken))
        {
            text = (text ?? "").Trim();
            if (text.Length == 0 || text.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0 || Encoding.UTF8.GetByteCount(text) > 157)
                throw new ArgumentException("Enter one command with at most 157 UTF-8 bytes.");
            await _repeaterOperations.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var key = RepeaterKey(number);
                RepeaterAuthorization auth;
                if (!_repeaterAuthorizations.TryGetValue(number, out auth) || !Object.ReferenceEquals(auth.Session, _meshCore))
                    throw new UnauthorizedAccessException("Log in to this repeater again after connecting.");
                var now = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                auth.Timestamp = Math.Max(now, auth.Timestamp + 1);
                // Firmware echoes a two-character command tag followed by '|'.
                const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
                var serial = (uint)Interlocked.Increment(ref _repeaterTag) % (62 * 62);
                var tag = "" + alphabet[(int)(serial / 62)] + alphabet[(int)(serial % 62)] + "|";
                var command = new byte[] { 2, 1, 0 }.Concat(BitConverter.GetBytes(auth.Timestamp)).Concat(key.Take(6)).Concat(Encoding.UTF8.GetBytes(tag + text)).ToArray();
                try
                {
                    var verb = text.Split(' ')[0].ToLowerInvariant();
                    if (verb == "reboot" || verb == "shutdown" || verb == "poweroff" || verb == "clkreboot")
                    {
                        await auth.Session.CommandAsync(command, new byte[] { 6 }, token).ConfigureAwait(false);
                        RepeaterAuthorization ignored; _repeaterAuthorizations.TryRemove(number, out ignored);
                        return "Command sent. This command does not return a reply; log in again afterwards.";
                    }
                    var response = await auth.Session.RemoteRequestAsync(command, (sent, frame) => ParseRepeaterReply(frame, key, tag) != null, token).ConfigureAwait(false);
                    return ParseRepeaterReply(response, key, tag);
                }
                finally { Array.Clear(command, 0, command.Length); }
            }
            finally { _repeaterOperations.Release(); }
        }
    }
}
