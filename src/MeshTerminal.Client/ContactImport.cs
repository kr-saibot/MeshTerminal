using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Protobufs;

namespace MeshTerminal.Client
{
    public sealed partial class MeshtasticClient
    {
        public async Task ImportSharedContactAsync(MeshProtocol protocol, string link, CancellationToken token = default(CancellationToken))
        {
            if (Protocol != protocol || !IsConnected || Device.MyNode == null) throw new InvalidOperationException("Connect and synchronize the matching device first.");
            if (protocol == MeshProtocol.MeshCore)
            {
                var contact = SharedLinkImport.MeshCoreContact(link);
                if (contact.MeshCoreCard == null) { await SaveMeshCoreContactAsync(contact.PublicKey, contact.Name, contact.Type, token: token).ConfigureAwait(false); return; }
                await _meshCoreSync.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await _meshCore.CommandAsync(new byte[] { 18 }.Concat(contact.MeshCoreCard).ToArray(), new byte[] { 0 }, token).ConfigureAwait(false);
                    var key = Enumerable.Range(0, 32).Select(i => Convert.ToByte(contact.PublicKey.Substring(i * 2, 2), 16)).ToArray();
                    try
                    {
                        var reply = await _meshCore.CommandAsync(new byte[] { 30 }.Concat(key).ToArray(), new byte[] { 3 }, token).ConfigureAwait(false);
                        RequireMeshCoreLength(reply, 148);
                        if (!reply.Skip(1).Take(32).SequenceEqual(key)) throw new IOException("Unexpected contact reply.");
                    }
                    catch (MeshCoreCommandException ex) when (ex.ErrorCode == 2)
                    { throw new IOException("The device did not store this card. Check signature, contact space and automatic adding settings.", ex); }
                }
                finally { _meshCoreSync.Release(); }
                return;
            }
            var imported = SharedLinkImport.MeshtasticContact(link).MeshtasticContact;
            if (imported.NodeNum == Device.MyNode.MyNodeNum) throw new ArgumentException("The own device cannot be imported as a remote contact.");
            MeshNode known;
            if (_nodes.TryGetValue(imported.NodeNum, out known) && known.Value.User != null && known.Value.User.PublicKey.Length == 32 &&
                !known.Value.User.PublicKey.Equals(imported.User.PublicKey)) throw new InvalidOperationException("This node already has a different public key. Resolve the key conflict before importing.");
            await _channelEditing.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var session = await RequestLocalAdminAsync(new AdminMessage { GetChannelRequest = 1 }, false, token).ConfigureAwait(false);
                await RequestLocalAdminAsync(new AdminMessage { AddContact = imported.Clone(), SessionPasskey = session.SessionPasskey }, true, token).ConfigureAwait(false);
                UpdateNode(new NodeInfo { Num = imported.NodeNum, User = imported.User.Clone() });
            }
            finally { _channelEditing.Release(); }
        }
    }
}