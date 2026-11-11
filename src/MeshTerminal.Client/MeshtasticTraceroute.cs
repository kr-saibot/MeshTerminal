using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Meshtastic.Protobufs;
namespace MeshTerminal.Client
{
    public sealed class MeshtasticTracerouteResult : EventArgs
    {
        public uint Origin { get; internal set; }
        public uint Destination { get; internal set; }
        public DateTime ReceivedUtc { get; internal set; }
        public RouteDiscovery Route { get; internal set; }
        public MeshCoreNodePaths ToPaths(IEnumerable<StoredMeshNode> knownNodes)
        {
            var nodes=knownNodes.ToDictionary(n=>n.NodeNumber);
            Func<uint,MeshCorePathStep> step=id=> {
                StoredMeshNode node; nodes.TryGetValue(id,out node);
                return new MeshCorePathStep { Name=id==UInt32.MaxValue?"Unknown relay":node?.LongName??("!"+id.ToString("x8")),Latitude=node?.Latitude,Longitude=node?.Longitude };
            };
            Func<uint,uint,IEnumerable<uint>,IList<int>,IList<MeshCorePathStep>> build=(start,end,middle,snr)=> {
                var list=new List<MeshCorePathStep> { step(start) }; list.AddRange(middle.Select(step)); list.Add(step(end));
                if(snr.Count==list.Count-1) for(int i=1;i<list.Count;i++) if(snr[i-1]!=-128) list[i].SnrDb=snr[i-1]/4f;
                return list;
            };
            return new MeshCoreNodePaths {
                OutgoingSteps=Route.Route.Count>0||Route.SnrTowards.Count==1?build(Origin,Destination,Route.Route,Route.SnrTowards):null,
                IncomingAdvertSteps=Route.RouteBack.Count>0||Route.SnrBack.Count==1?build(Destination,Origin,Route.RouteBack,Route.SnrBack):null,
                Outgoing="Forward path not reported by this firmware.",IncomingAdvert="Return path not reported by this firmware."
            };
        }
    }
    public sealed partial class MeshtasticClient
    {
        private sealed class PendingTraceroute
        {
            internal uint Destination,Origin;
            internal readonly TaskCompletionSource<MeshtasticTracerouteResult> Completion=new TaskCompletionSource<MeshtasticTracerouteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private readonly ConcurrentDictionary<uint,PendingTraceroute> _traceroutes=new ConcurrentDictionary<uint,PendingTraceroute>();
        private DateTime _lastTracerouteUtc=DateTime.MinValue;
        public TimeSpan TracerouteTimeout { get; set; }=TimeSpan.FromMinutes(2);
        public event EventHandler<MeshtasticTracerouteResult> TracerouteReceived;
        public async Task<MeshtasticTracerouteResult> RequestTracerouteAsync(uint destination,int channelIndex=0,int? hopLimit=null,CancellationToken cancellationToken=default(CancellationToken))
        {
            if(Protocol!=MeshProtocol.Meshtastic || !IsConnected || Device.MyNode==null) throw new InvalidOperationException("Connect to a Meshtastic device first.");
            if(destination==0 || destination==UInt32.MaxValue || destination==Device.MyNode.MyNodeNum) throw new ArgumentException("Select another node for traceroute.");
            if(channelIndex<0 || channelIndex>7) throw new ArgumentOutOfRangeException(nameof(channelIndex));
            int hops=hopLimit??(int)(Device.LoRaConfig?.HopLimit??3);
            if(hops<0||hops>7) throw new ArgumentOutOfRangeException(nameof(hopLimit));
            cancellationToken.ThrowIfCancellationRequested();
            lock(_gate) {
                var remaining=30-(DateTime.UtcNow-_lastTracerouteUtc).TotalSeconds;
                if(remaining>0) throw new InvalidOperationException("Wait "+Math.Ceiling(remaining)+" seconds before starting another traceroute.");
                _lastTracerouteUtc=DateTime.UtcNow;
            }
            var id=NextPacketId(); var pending=new PendingTraceroute { Destination=destination,Origin=Device.MyNode.MyNodeNum }; _traceroutes[id]=pending;
            CancellationToken connectionToken; lock(_gate) connectionToken=_lifetime?.Token??new CancellationToken(true);
            using(var stop=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,connectionToken))
            {
                try {
                    await SendAsync(new ToRadio { Packet=new MeshPacket { Id=id,To=destination,Channel=(uint)channelIndex,HopLimit=(uint)hops,Priority=MeshPacket.Types.Priority.Reliable,Decoded=new Data { Portnum=PortNum.TracerouteApp,WantResponse=true,Payload=new RouteDiscovery().ToByteString() } } },stop.Token).ConfigureAwait(false);
                    var wait=Task.Delay(TracerouteTimeout,stop.Token);
                    if(await Task.WhenAny(pending.Completion.Task,wait).ConfigureAwait(false)==pending.Completion.Task) return await pending.Completion.Task.ConfigureAwait(false);
                    stop.Token.ThrowIfCancellationRequested();
                    throw new TimeoutException("Traceroute timed out after "+TracerouteTimeout.TotalSeconds+" seconds. No route response was received.");
                }
                finally { stop.Cancel(); PendingTraceroute ignored; _traceroutes.TryRemove(id,out ignored); }
            }
        }
        private void HandleTraceroute(MeshPacket packet)
        {
            PendingTraceroute pending;
            if(!_traceroutes.TryGetValue(packet.Decoded.RequestId,out pending) || packet.From!=pending.Destination || packet.To!=pending.Origin || packet.Decoded.WantResponse) return;
            try {
                var route=RouteDiscovery.Parser.ParseFrom(packet.Decoded.Payload);
                if(route.Route.Count>64||route.RouteBack.Count>64||route.SnrTowards.Count>65||route.SnrBack.Count>65) throw new InvalidDataException("Invalid traceroute length.");
                var result=new MeshtasticTracerouteResult { Origin=pending.Origin,Destination=pending.Destination,ReceivedUtc=DateTime.UtcNow,Route=route };
                if(pending.Completion.TrySetResult(result)) Raise(TracerouteReceived,result);
            }
            catch(Exception ex) { pending.Completion.TrySetException(new IOException("Invalid traceroute response.",ex)); }
        }
        private void HandleTracerouteRouting(MeshPacket packet)
        {
            PendingTraceroute pending;
            if(!_traceroutes.TryGetValue(packet.Decoded.RequestId,out pending) || (packet.From!=pending.Origin&&packet.From!=pending.Destination)) return;
            var routing=Routing.Parser.ParseFrom(packet.Decoded.Payload);
            if(routing.VariantCase==Routing.VariantOneofCase.ErrorReason && routing.ErrorReason!=Routing.Types.Error.None) pending.Completion.TrySetException(new IOException("Traceroute failed: "+routing.ErrorReason));
        }
        private void StopTraceroutes()
        {
            foreach(var pending in _traceroutes.Values) pending.Completion.TrySetException(new IOException("Traceroute interrupted: device disconnected."));
        }
    }
}
