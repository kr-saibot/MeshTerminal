using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using MeshTerminal.Client;
using Meshtastic.Protobufs;
internal static class MqttSystemStatusTests
{
    private static void Check(bool condition,string message) { if(!condition)throw new Exception("MQTT system status: "+message); }
    internal static Dictionary<string,object> Sample()
    {
        var now=DateTime.UtcNow;
        var device=new DeviceInfo { MyNode=new MyNodeInfo { MyNodeNum=42 },LocalNode=new MeshNode(new NodeInfo { Num=42,User=new User { Id=new string('a',64),LongName="Own device" } }),
            HardwareName="Heltec V4",Metadata=new DeviceMetadata { FirmwareVersion="1.14" },FirmwareBuildDate="2026-10-29",MeshCoreProtocolVersion=10,
            RadioFrequencyMHz=869.525,RadioBandwidthKHz=62.5,RadioSpreadingFactor=7,RadioCodingRate=5,RadioTxPowerDbm=22,RoutingHashLengthBytes=3,
            DeviceClockUtc=now,DeviceClockReadAtUtc=now,DeviceClockSyncStatus="Synchronized with PC",NoiseFloorDbm=-117,MeshCoreBatteryVoltage=4.123,
            RadioReceivedPackets=4000000000,RadioSentPackets=123,RadioReceiveErrors=0,RadioStatisticsReadUtc=now };
        var connection=new ConnectionInfo("Bluetooth LE","AA:BB",now.AddMinutes(-1),TimeSpan.FromMinutes(5),2,70,90,now,now);
        return MqttSystemStatus.Create(MeshProtocol.MeshCore,device,ConnectionState.Connected,connection,"Bluetooth: Own device","Full Device info text");
    }
    internal static void Run()
    {
        var values=MqttPayload.Publications("system",Sample());
        Func<string,string> get=key=>values.Single(p=>p.Topic=="system/"+key).Payload;
        Check(get("device/name")=="Own device"&&get("device/hardware")=="Heltec V4"&&get("device/firmware")=="1.14"&&get("message")=="Full Device info text","Own device info and readable details");
        Check(get("radio/rx_packets")=="4000000000"&&get("radio/tx_packets")=="123"&&get("connection/rx_frames")=="90"&&get("connection/tx_frames")=="70","Radio vs transport counters, unsigned values");
        Check(get("radio/noise_floor_dbm")=="-117"&&get("battery/voltage")=="4.123"&&get("radio/routing_hash_bytes")=="3"&&get("clock/synchronization")=="Synchronized with PC","Noise, voltage, radio and clock fields");
        var d=new DeviceInfo { LocalNode=new MeshNode(new NodeInfo { User=new User { LongName="Meshtastic" },DeviceMetrics=new DeviceMetrics { BatteryLevel=101,Voltage=4.2f } }),LoRaConfig=new Config.Types.LoRaConfig { UsePreset=false,Bandwidth=62,SpreadFactor=8,CodingRate=5,OverrideFrequency=868.1f,ChannelNum=4,TxPower=20,TxEnabled=true,HopLimit=3 } };
        var offline=new ConnectionInfo("Serial","COM14",null,TimeSpan.Zero,0,0,0,null,null);
        values=MqttPayload.Publications("system",MqttSystemStatus.Create(MeshProtocol.Meshtastic,d,ConnectionState.Disconnected,offline,"COM14","Device info"));
        Check(values.Any(p=>p.Topic=="system/battery/external_power"&&p.Payload=="true")&&!values.Any(p=>p.Topic=="system/battery/percent"),"External power is not an invalid battery percentage");
        Check(values.Any(p=>p.Topic=="system/radio/bandwidth_khz"&&p.Payload=="62.5")&&values.Any(p=>p.Topic=="system/radio/preset"&&p.Payload=="Custom")&&values.Any(p=>p.Topic=="system/connected"&&p.Payload=="false"),"Custom Meshtastic radio settings and disconnected state");
        Check(!values.Any(p=>p.Topic=="system/radio/rx_packets"),"Unavailable radio counters are not fabricated zeros");
        using(var client=new MeshtasticClient { Protocol=MeshProtocol.MeshCore })
        {
            var frame=new byte[30];frame[0]=24;frame[1]=2;BitConverter.GetBytes(4000000000u).CopyTo(frame,2);BitConverter.GetBytes(55u).CopyTo(frame,6);BitConverter.GetBytes(7u).CopyTo(frame,26);
            var handler=typeof(MeshtasticClient).GetMethod("HandleMeshCoreFrame",BindingFlags.Instance|BindingFlags.NonPublic);handler.Invoke(client,new object[] {frame});
            Check(client.Device.RadioReceivedPackets==4000000000u&&client.Device.RadioSentPackets==55&&client.Device.RadioReceiveErrors==7,"Official MeshCore packet-stat offsets");
            handler.Invoke(client,new object[] {frame.Take(10).ToArray()});Check(!client.Device.RadioReceiveErrors.HasValue,"Absent optional receive-error counter is unknown");
        }
        using(var client=new MeshtasticClient())
        {
            var handle=typeof(MeshtasticClient).GetMethod("Handle",BindingFlags.Instance|BindingFlags.NonPublic);
            handle.Invoke(client,new object[] {new FromRadio { MyInfo=new MyNodeInfo { MyNodeNum=42 } }});
            handle.Invoke(client,new object[] {new FromRadio { Packet=new MeshPacket { From=42,Decoded=new Data { Portnum=PortNum.TelemetryApp,Payload=new Telemetry { LocalStats=new LocalStats { NumPacketsRx=222,NumPacketsTx=111,NumPacketsRxBad=3,NoiseFloor=-120 } }.ToByteString() } } }});
            Check(client.Device.RadioReceivedPackets==222&&client.Device.RadioSentPackets==111&&client.Device.NoiseFloorDbm==-120,"Own Meshtastic LocalStats feed radio counters and noise");
            handle.Invoke(client,new object[] {new FromRadio { Packet=new MeshPacket { From=99,Decoded=new Data { Portnum=PortNum.TelemetryApp,Payload=new Telemetry { LocalStats=new LocalStats { NumPacketsRx=999 } }.ToByteString() } } }});
            Check(client.Device.RadioReceivedPackets==222,"Remote telemetry cannot overwrite own counters");
        }
        var setting=new MqttClientSettings { Binding=MqttBinding.SystemStatus };setting.Validate();Check(setting.StatusIntervalSeconds==30&&!setting.IsChatBinding,"Receive-only status default interval");
        setting.StatusIntervalSeconds=4;bool rejected=false;try {setting.Validate();}catch(ArgumentException) {rejected=true;}Check(rejected,"Bounded publication interval");
        Console.WriteLine("MQTT system status tests passed: both device/radio layouts, clock, battery/external power, signed noise, separate radio/transport counters and missing data.");
    }
}
