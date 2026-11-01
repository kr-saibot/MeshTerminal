using System;
using Meshtastic.Protobufs;
namespace MeshTerminal.Client
{
    /// <summary>A live radio announcement, not a contact-list synchronization or local node edit.</summary>
    public sealed class NodeAnnouncementEventArgs : EventArgs
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public MeshProtocol Protocol { get; private set; }
        public NodeInfo Node { get; private set; }
        public byte? ContactType { get; private set; }
        public DateTime ReceivedUtc { get; private set; }
        public bool IsNew { get; private set; }
        public int? Rssi { get; private set; }
        public float? Snr { get; private set; }
        internal NodeAnnouncementEventArgs(MeshProtocol protocol, MeshNode node, bool isNew, DateTime receivedUtc)
        {
            Protocol=protocol; Node=node.Value.Clone(); ContactType=node.MeshCoreContactType;
            IsNew=isNew; ReceivedUtc=receivedUtc; Rssi=node.LastRssi; Snr=node.LastSnr;
        }
    }
    public sealed partial class MeshtasticClient
    {
        public event EventHandler<NodeAnnouncementEventArgs> NodeAnnouncementReceived;
        private void AnnounceNode(MeshNode node, bool isNew, DateTime receivedUtc)
        {
            if(node==null||(Device.MyNode!=null&&node.Number==Device.MyNode.MyNodeNum))return;
            Raise(NodeAnnouncementReceived,new NodeAnnouncementEventArgs(Protocol,node,isNew,receivedUtc));
        }
    }
}
