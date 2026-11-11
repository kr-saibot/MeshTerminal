using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using MeshTerminal.Client;
using Meshtastic.Protobufs;
internal static class TxUtilizationTests
{
    static void Check(bool condition,string message) { if(!condition) throw new Exception("TX utilization: "+message); }
    internal static void Run()
    {
        var now=DateTime.UtcNow;var window=new TxAirtimeWindow();
        Check(!window.Observe(100,0,now).Percent.HasValue,"A lifetime counter is a baseline, not a percentage");
        Check(!window.Observe(102,30,now.AddSeconds(30)).Percent.HasValue,"Warmup does not mislabel coarse second counters as zero");
        var sample=window.Observe(103,60,now.AddSeconds(60));
        Check(Math.Abs(sample.Percent.Value-5)<0.0001&&sample.WindowSeconds==60&&sample.Estimated,"Delta TX seconds divided by observed monotonic duration");
        Check(TxUtilizationDisplay.Compact(sample,true)=="TX: ~5%","Compact status keeps the estimated percentage without its window");
        Check(TxUtilizationDisplay.Details(sample,true).Contains("Observed window: 1m"),"Device info identifies the measurement window");
        Check(!window.Observe(0,65,now.AddSeconds(65)).Percent.HasValue,"Counter reset/wrap restarts the window");
        Check(window.Observe(0,65,now)==null,"Same-time observations cannot divide by zero");
        Check(!window.Observe(2000,70,now.AddSeconds(70)).Percent.HasValue,"Implausible counter jumps cannot create a duty-cycle spike");
        Check(!window.Observe(2001,200,now.AddSeconds(200)).Percent.HasValue,"Long polling gaps restart rather than extrapolating an unobserved hour");
        window.Reset();sample=window.Observe(0,0,now);
        for(int minute=1;minute<=75;minute++)
        {
            sample=window.Observe((uint)(Math.Min(minute,15)*60),minute*60,now.AddMinutes(minute));
            if(minute==60) Check(sample.WindowSeconds==3600&&Math.Abs(sample.Percent.Value-25)<0.0001,"Rolling hour includes the first 15 minutes of TX");
        }
        Check(sample.WindowSeconds==3600&&sample.Percent==0,"Expired airtime leaves the rolling hour; idle is a valid zero");
        Check(TxUtilizationDisplay.Compact(sample,false)=="TX: -","Disconnected device never shows an active percentage");
        using(var client=new MeshtasticClient())
        {
            var handle=typeof(MeshtasticClient).GetMethod("Handle",BindingFlags.Instance|BindingFlags.NonPublic);
            Action<FromRadio> receive=frame=>handle.Invoke(client,new object[]{frame});
            receive(new FromRadio { MyInfo=new MyNodeInfo { MyNodeNum=42 } });
            receive(new FromRadio { NodeInfo=new NodeInfo { Num=42,User=new User { LongName="Self" },DeviceMetrics=new DeviceMetrics { AirUtilTx=0 } } });
            Check(client.Device.TxUtilization.Percent==0&&!client.Device.TxUtilization.Estimated&&client.Device.TxUtilization.WindowSeconds==3600,"Meshtastic own-node reported zero is present, not missing");
            receive(new FromRadio { Packet=new MeshPacket { From=99,Decoded=new Data { Portnum=PortNum.TelemetryApp,Payload=new Telemetry { LocalStats=new LocalStats { AirUtilTx=99 } }.ToByteString() } } });
            Check(client.Device.TxUtilization.Percent==0,"Remote airtime cannot overwrite the own device");
            receive(new FromRadio { Packet=new MeshPacket { From=42,Decoded=new Data { Portnum=PortNum.TelemetryApp,Payload=new Telemetry { LocalStats=new LocalStats { AirUtilTx=1.25f } }.ToByteString() } } });
            Check(client.Device.TxUtilization.Percent==1.25&&!client.Device.TxUtilization.Estimated,"Own LocalStats updates the device-reported hourly percentage");
            var connection=client.ConnectionInfo;
            var data=MqttSystemStatus.Create(MeshProtocol.Meshtastic,client.Device,ConnectionState.Connected,connection,"test","");
            var radio=(Dictionary<string,object>)data["radio"];
            Check((double)radio["tx_utilization_percent"]==1.25&&(double)radio["tx_utilization_window_seconds"]==3600&&!(bool)radio["tx_utilization_estimated"],"MQTT carries the percentage, window and source");
            Check(MqttPayload.Publications("test",data).Any(p=>p.Topic=="test/radio/tx_utilization_percent"&&p.Payload=="1.25"),"MQTT scalar export retains decimal precision");
            typeof(MeshtasticClient).GetMethod("SetState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(client,new object[]{ConnectionState.Connected,null});
            client.DisconnectAsync().GetAwaiter().GetResult();
            Check(client.Device.TxUtilization==null,"Disconnect clears the live snapshot and measurement state");
        }
        Console.WriteLine("TX utilization tests passed: reported own-node/LocalStats values, remote isolation, warmup, rolling hour, counter resets, gaps, formatting, MQTT and disconnect.");
    }
}
