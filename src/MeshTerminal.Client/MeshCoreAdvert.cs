using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeshTerminal.Client
{
    public sealed partial class MeshtasticClient
    {
        public async Task SendAdvertAsync(bool flood, bool includePosition, CancellationToken token = default(CancellationToken))
        {
            if (Protocol != MeshProtocol.MeshCore) throw new NotSupportedException("The advert position option requires MeshCore.");
            if (!IsConnected || _meshCore == null) throw new InvalidOperationException("Connect MeshCore first.");
            await _meshCoreSync.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var session = _meshCore;
                var start = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }.Concat(Encoding.ASCII.GetBytes("MeshTerminal")).ToArray();
                var self = await session.CommandAsync(start, new byte[] { 5 }, token).ConfigureAwait(false);
                RequireMeshCoreLength(self, 48);
                var original = new byte[] { 38, self[47], self[46], self[45], self[44] };
                var desired = (byte)(includePosition ? 1 : 0);
                var changed = self[45] != desired;
                Exception failure = null;
                try
                {
                    if (changed)
                    {
                        var settings = (byte[])original.Clone(); settings[3] = desired;
                        await session.CommandAsync(settings, new byte[] { 0 }, token).ConfigureAwait(false);
                        var check = await session.CommandAsync(start, new byte[] { 5 }, token).ConfigureAwait(false);
                        RequireMeshCoreLength(check, 48);
                        if (check[45] != desired) throw new IOException("The device did not apply the requested advert position setting.");
                    }
                    await session.CommandAsync(new byte[] { 7, (byte)(flood ? 1 : 0) }, new byte[] { 0 }, token).ConfigureAwait(false);
                }
                catch (Exception ex) { failure = ex; }
                // Restore even after cancellation or a rejected advert. Use the original
                // session so a reconnect can never apply old preferences to another radio.
                if (changed)
                {
                    try
                    {
                        await session.CommandAsync(original, new byte[] { 0 }, CancellationToken.None).ConfigureAwait(false);
                        var restored = await session.CommandAsync(start, new byte[] { 5 }, CancellationToken.None).ConfigureAwait(false);
                        RequireMeshCoreLength(restored, 48);
                        if (!restored.Skip(44).Take(4).SequenceEqual(self.Skip(44).Take(4))) throw new IOException("The device returned different advert settings.");
                    }
                    catch (Exception restoreError)
                    {
                        throw new IOException((failure == null ? "Advert was submitted, but" : "Advert was not confirmed and") + " the original advert position setting could not be restored. Reconnect and check the device's advert position setting.", restoreError);
                    }
                }
                if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            }
            finally { _meshCoreSync.Release(); }
        }
    }
}
