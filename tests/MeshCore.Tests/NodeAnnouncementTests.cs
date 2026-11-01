using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using MeshTerminal.Client;
using Meshtastic.Protobufs;
internal static class NodeAnnouncementTests
{
    private static void Check(bool value,string message) { if(!value)throw new Exception("Node announcements: "+message); }
    internal static NodeAnnouncementEventArgs Sample(bool isNew=true,MeshProtocol protocol=MeshProtocol.MeshCore)
    {
        var node=new MeshNode(new NodeInfo { Num=42,User=new User { Id=protocol==MeshProtocol.MeshCore?new string('a',64):"!0000002a",LongName="Mobile",ShortName="MOB" },Position=new Position { LatitudeI=483400000,LongitudeI=121280000 },HopsAway=2 }) { MeshCoreContactType=1 };
        node.UpdateRadioMetrics(-92,4.25f);return new NodeAnnouncementEventArgs(protocol,node,isNew,DateTime.UtcNow);
    }
    internal static void Run()
    {
        using(var client=new MeshtasticClient { Protocol=MeshProtocol.Meshtastic })
        {
            var events=new List<NodeAnnouncementEventArgs>();client.NodeAnnouncementReceived+=(s,e)=>events.Add(e);
            var handle=typeof(MeshtasticClient).GetMethod("Handle",BindingFlags.Instance|BindingFlags.NonPublic);
            Action<FromRadio> receive=frame=>handle.Invoke(client,new object[] {frame});
            receive(new FromRadio { NodeInfo=new NodeInfo { Num=1,User=new User { Id="!00000001",LongName="Stored contact" } } });
            Check(events.Count==0,"Contact snapshots do not create live MQTT events");
            receive(new FromRadio { Packet=new MeshPacket { From=42,RxRssi=-90,RxSnr=2.5f,HopStart=3,HopLimit=1,Decoded=new Data { Portnum=PortNum.NodeinfoApp,Payload=new User { Id="!0000002a",LongName="Mobile" }.ToByteString() } } });
            Check(events.Count==1&&events[0].IsNew&&events[0].Node.User.LongName=="Mobile"&&events[0].Node.HopsAway==2&&events[0].Rssi==-90,"Unknown live NodeInfo creates a complete new-node event");
            receive(new FromRadio { Packet=new MeshPacket { From=42,Decoded=new Data { Portnum=PortNum.NodeinfoApp,Payload=new User { Id="!0000002a",LongName="Renamed" }.ToByteString() } } });
            Check(events.Count==2&&!events[1].IsNew&&events[0].Node.User.LongName=="Mobile","Repeated NodeInfo is an announcement and earlier snapshots stay immutable");
            receive(new FromRadio { Packet=new MeshPacket { From=42,Decoded=new Data { Portnum=PortNum.TextMessageApp,Payload=ByteString.CopyFromUtf8("hello") } } });
            Check(events.Count==2,"Chat, signal and ordinary updates do not create announcements");
            receive(new FromRadio { MyInfo=new MyNodeInfo { MyNodeNum=42 } });
            receive(new FromRadio { Packet=new MeshPacket { From=42,Decoded=new Data { Portnum=PortNum.NodeinfoApp,Payload=new User { Id="!0000002a",LongName="Own" }.ToByteString() } } });
            Check(events.Count==2,"Own node announcements are excluded");
        }
        var publications=MqttPayload.Publications("nodes",MqttPayload.Announcement(Sample()));
        Check(publications.Any(p=>p.Topic=="nodes/kind"&&p.Payload=="Advert")&&publications.Any(p=>p.Topic=="nodes/node/type"&&p.Payload=="Companion")&&publications.Any(p=>p.Topic=="nodes/radio/rssi_dbm"&&p.Payload=="-92"),"Advert, type and signal topics");
        Check(publications.Any(p=>p.Topic=="nodes/node/latitude"&&p.Payload=="48.34")&&publications.Any(p=>p.Topic=="nodes/json"&&p.Payload.Contains("is_new")),"Position and full event JSON");
        var settings=new MqttClientSettings { Binding=MqttBinding.AdvertsAndNodes,OnlyNewNodes=true };
        settings.Validate();Check(!settings.IsChatBinding,"Node routes are receive-only");
        settings.AllowSending=true;bool rejected=false;try {settings.Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"Cannot send through announcement bindings");
        Console.WriteLine("Node announcement tests passed: live NodeInfo vs snapshots/chat/own node, new/repeated identities, immutable metadata and receive-only settings.");
    }
}
