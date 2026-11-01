using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeshTerminal.Client;
using Meshtastic.Protobufs;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
internal static class MqttTests
{
    private static void Check(bool value,string message) { if(!value)throw new Exception("MQTT: "+message); }
    private static async Task Until(Func<bool> condition,string message,int milliseconds=12000)
    {
        var end=DateTime.UtcNow.AddMilliseconds(milliseconds);
        while(!condition()) { if(DateTime.UtcNow>end)throw new Exception("MQTT timeout: "+message);await Task.Delay(25); }
    }
    private static int FreePort() { var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port; }
    private static StoredMeshMessage Message(StoredMessageKind kind=StoredMessageKind.Channel)
    {
        return new StoredMeshMessage { Id=Guid.NewGuid(),OccurredUtc=new DateTime(2024,1,2,3,4,5,DateTimeKind.Utc),CreatedUtc=DateTime.UtcNow,Kind=kind,Direction=MessageDirection.Incoming,FromNode=123,ToNode=kind==StoredMessageKind.Channel?UInt32.MaxValue:1,ChannelIndex=kind==StoredMessageKind.Channel?(int?)39:null,ChannelName="#JMBR",Text="Hallo \"Welt\"\nGrüße 😀",ReceivedRssi=-85,ReceivedSnr=3.25f,ReceivedHops=2 };
    }
    private static MqttClientSettings Settings(int port,string topic,MqttBinding binding,bool sending=false)
    {
        return new MqttClientSettings { Protocol=MeshProtocol.MeshCore,Name=topic,Server="127.0.0.1",Port=port,Enabled=true,Username="user",Password="test-secret",BaseTopic="test/"+topic,Binding=binding,ChannelIndex=39,NodeId=new string('a',64),AllowSending=sending };
    }
    internal static async Task RunAsync()
    {
        var file=Path.Combine(Path.GetTempPath(),"mqtt-"+Guid.NewGuid().ToString("N")+".xml");
        try
        {
            var defaults=new MeshtasticApplicationSettings();Check(defaults.MqttClients.Count==0&&!new MqttClientSettings().AllowSending,"Opt-in clients and sending");
            File.WriteAllText(file,"<MeshtasticApplicationSettings/>");Check(MeshtasticSettingsStore.Load(file).MqttClients.Count==0,"Legacy settings migrate without clients");
            var setting=Settings(1883,"channel",MqttBinding.Channel,true);setting.UseTls=true;setting.FavoritesOnly=true;defaults.MqttClients.Add(setting);
            defaults.MqttClients.Add(new MqttClientSettings { Name="Meshtastic",NodeId="!12345678",Binding=MqttBinding.DirectNode });
            MeshtasticSettingsStore.Save(file,defaults);var loaded=MeshtasticSettingsStore.Load(file).MqttClients;
            Check(loaded.Count==2&&loaded[0].ChannelIndex==39&&loaded[0].AllowSending&&loaded[0].FavoritesOnly&&loaded[0].UseTls&&loaded[0].Password=="test-secret"&&loaded[1].NodeId=="!12345678","Protocol, high channel, credentials, flags and persistent IDs round-trip");
            defaults.MqttClients.Add(new MqttClientSettings { Binding=MqttBinding.AdvertsAndNodes,OnlyNewNodes=true,FavoritesOnly=true });
            MeshtasticSettingsStore.Save(file,defaults);Check(MeshtasticSettingsStore.Load(file).MqttClients.Last().OnlyNewNodes&&MeshtasticSettingsStore.Load(file).MqttClients.Last().Binding==MqttBinding.AdvertsAndNodes,"Announcement binding and new-node filter persist");
            defaults.MqttClients.Add(new MqttClientSettings { Binding=MqttBinding.SystemStatus,StatusIntervalSeconds=60 });
            MeshtasticSettingsStore.Save(file,defaults);Check(MeshtasticSettingsStore.Load(file).MqttClients.Last().StatusIntervalSeconds==60,"System status interval persists");
            var invalid=setting.Copy();invalid.BaseTopic="bad/#";bool rejected=false;try { invalid.Validate(); }catch(ArgumentException) { rejected=true; }Check(rejected,"Wildcard publication topics rejected");
            invalid=setting.Copy();invalid.Binding=MqttBinding.Telemetry;rejected=false;try { invalid.Validate(); }catch(ArgumentException) { rejected=true; }Check(rejected,"Telemetry cannot enable sending");
            var channel=Message();Check(MqttPayload.Matches(setting,channel,setting.NodeId,true)&&!MqttPayload.Matches(setting,channel,setting.NodeId,false),"Channel favorites filter");
            channel.ChannelIndex=38;Check(!MqttPayload.Matches(setting,channel,setting.NodeId,true),"Other channel excluded");channel.ChannelIndex=39;channel.Direction=MessageDirection.Outgoing;Check(!MqttPayload.Matches(setting,channel,setting.NodeId,true),"No outgoing echo loop");
            var direct=Settings(1883,"dm",MqttBinding.DirectNode);Check(MqttPayload.Matches(direct,Message(StoredMessageKind.Direct),direct.NodeId,false)&&!MqttPayload.Matches(direct,Message(StoredMessageKind.Direct),new string('b',64),false)&&!MqttPayload.Matches(direct,Message(),direct.NodeId,false),"Specific direct node and message-kind isolation");
            var all=Settings(1883,"all",MqttBinding.AllDirect);Check(MqttPayload.Matches(all,Message(StoredMessageKind.Direct),null,false),"All direct messages includes unknown sender");
            var payload=MqttPayload.Publications("test",MqttPayload.Message(MeshProtocol.MeshCore,Message(),direct.NodeId,"DM2TK",null));var json=payload.Single(p=>p.Topic=="test/json").Payload;
            Check(payload.Any(p=>p.Topic=="test/radio/rssi_dbm"&&p.Payload=="-85")&&payload.Any(p=>p.Topic=="test/channel/index"&&p.Payload=="39")&&payload.Any(p=>p.Topic=="test/sender/id"&&p.Payload==direct.NodeId),"Available metadata has individual topics");
            var dictionary=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);Check((string)dictionary["message"]==Message().Text&&!json.Contains("test-secret"),"JSON preserves UTF-8, quotes and newlines without credentials");
            var telemetry=MqttTelemetry.From(new MeshTelemetry(new MeshPacket { From=123 },new Telemetry { DeviceMetrics=new DeviceMetrics { BatteryLevel=0,Voltage=4.125f,UptimeSeconds=0 } }));
            var telemPayload=MqttPayload.Publications("test",MqttPayload.Telemetry(MeshProtocol.Meshtastic,telemetry,"!0000007b","Mobile"));
            Check(telemPayload.Any(p=>p.Topic=="test/telemetry/battery_percent"&&p.Payload=="0")&&!telemPayload.Any(p=>p.Topic=="test/telemetry/temperature_c"),"Zero telemetry values included, unavailable values omitted");
            var position=MqttTelemetry.From(123,new Position { LatitudeI=483400000,LongitudeI=121280000 },DateTime.UtcNow);Check(position.Latitude==48.34&&position.Longitude==12.128,"Position telemetry factory");
            var status=MqttTelemetry.From(MeshCoreRepeaterStatus.Parse(RepeaterStatusTests.Frame(new byte[6]),123));Check(status.NoiseFloorDbm==-117&&status.LastSnrDb==-3.25,"Repeater status metadata");
            var db=Path.ChangeExtension(file,".db");
            try { using(var store=MeshtasticMessageStore.CreateSqlite(db)) { store.Initialize();int events=0;store.PositionStored+=t=> { events++;Check(t.NodeNumber==123,"Stored position identity"); };store.AddPosition(123,new Position(),DateTime.UtcNow);store.AddPosition(123,new Position(),DateTime.UtcNow,false);Check(events==1,"Position hooks include shared markers and avoid duplicate live exports"); } }finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach(var f in Directory.GetFiles(Path.GetDirectoryName(db),Path.GetFileName(db)+"*"))File.Delete(f); }
        }
        finally { if(File.Exists(file))File.Delete(file); }
        await WireAsync();
        Console.WriteLine("MQTT tests passed: configuration, bindings, favorites, both protocols, UTF-8/JSON metadata, telemetry, real broker authentication, send opt-in, retained replay, reconnect and bounded queues.");
    }
    private static async Task WireAsync()
    {
        var port=FreePort();var received=new ConcurrentQueue<MqttPublication>();var sent=new ConcurrentQueue<string>();int authenticated=0;
        var options=new MqttServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointPort(port).WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointBoundIPV6Address(IPAddress.IPv6Loopback).Build();
        using(var server=new MqttFactory().CreateMqttServer(options))
        {
            server.ValidatingConnectionAsync+=e=> { if(e.UserName!="user"||e.Password!="test-secret")e.ReasonCode=MqttConnectReasonCode.BadUserNameOrPassword;else Interlocked.Increment(ref authenticated);return Task.CompletedTask; };
            server.InterceptingPublishAsync+=e=> { var m=e.ApplicationMessage;received.Enqueue(new MqttPublication(m.Topic,Encoding.UTF8.GetString(m.PayloadSegment.Array??new byte[0],m.PayloadSegment.Offset,m.PayloadSegment.Count)));return Task.CompletedTask; };
            await server.StartAsync();
            Func<string,string,bool,Task> inject=(topic,text,retain)=>server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(text).WithRetainFlag(retain).Build()) { SenderClientId="test-injector" });
            await inject("test/channel/send","stale retained command",true);
            await inject("test/all/send/"+new string('a',64),MqttClientManager.SendTopicPlaceholder,true);
            await inject("test/all/send/"+new string('b',64),MqttClientManager.SendTopicPlaceholder,true);
            var configs=new[] { Settings(port,"channel",MqttBinding.Channel,true),Settings(port,"direct",MqttBinding.DirectNode,true),Settings(port,"all",MqttBinding.AllDirect,true),Settings(port,"telemetry",MqttBinding.Telemetry),Settings(port,"readonly",MqttBinding.Channel),Settings(port,"disabled",MqttBinding.Channel),Settings(port,"otherprotocol",MqttBinding.Channel),Settings(port,"nodes",MqttBinding.AdvertsAndNodes),Settings(port,"newnodes",MqttBinding.AdvertsAndNodes),Settings(port,"favnodes",MqttBinding.AdvertsAndNodes),Settings(port,"system",MqttBinding.SystemStatus) };
            configs[0].FavoritesOnly=true;configs[5].Enabled=false;configs[6].Protocol=MeshProtocol.Meshtastic;configs[8].OnlyNewNodes=true;configs[9].FavoritesOnly=true;configs[10].StatusIntervalSeconds=5;configs[10].FavoritesOnly=true;
            var manager=new MqttClientManager(MeshProtocol.MeshCore,configs,(s,target,text,token)=> { token.ThrowIfCancellationRequested();if(text=="fail")throw new InvalidOperationException("simulated send failure");sent.Enqueue(s.Name+"|"+target+"|"+text);return Task.CompletedTask; },s => new[] { new string('a',64), new string('b',64), new string('a',64) },MqttSystemStatusTests.Sample);
            manager.Start();
            try
            {
                await Until(()=>manager.GetStatuses().Count(s=>s.State=="Connected")==9,"all independent clients connect");
                Check(authenticated==9&&manager.GetStatuses().Count==10,"Authentication, protocol isolation and disabled clients");
                await Until(()=>received.Any(p=>p.Topic=="test/system/json"),"System status sent immediately on connect");
                Check(received.Any(p=>p.Topic=="test/system/radio/rx_packets"&&p.Payload=="4000000000")&&received.Any(p=>p.Topic=="test/system/connection/rx_frames"&&p.Payload=="90"),"System topics include device and transport counters");
                await inject("test/system/send","must not send",false);
                await Until(()=>received.Count(p=>p.Topic=="test/system/json")>=2,"Status automatically repeats at configured interval",8000);
                await Until(()=>manager.GetStatuses().First(s=>s.Name=="channel").LastError.Contains("Retained"),"retained request blocked");Check(sent.Count==0,"Retained subscription replay never transmits");
                var retained=await server.GetRetainedMessagesAsync();
                var markers=retained.Where(p=>p.Topic.StartsWith("test/",StringComparison.Ordinal)&&p.Topic.Contains("/send")).ToList();
                Check(markers.Count==3&&markers.All(p=>Encoding.UTF8.GetString(p.PayloadSegment.Array,p.PayloadSegment.Offset,p.PayloadSegment.Count)==MqttClientManager.SendTopicPlaceholder),"Only one retained send topic per enabled route; legacy ID topics removed");
                Check(markers.All(p=>!p.Topic.Contains("readonly")&&!p.Topic.Contains("telemetry")&&!p.Topic.Contains("disabled")),"No command placeholders when sending is disabled");
                await inject("test/channel/send",MqttClientManager.SendTopicPlaceholder,false);await Task.Delay(150);Check(sent.Count==0,"Live placeholder echoes cannot send radio text");
                await inject("test/all/send","no sender yet",false);await Until(()=>manager.GetStatuses().First(s=>s.Name=="all").LastError.Contains("No known sender"),"Cannot reply before a direct message is published");Check(sent.Count==0,"No fallback to arbitrary target");
                manager.PublishAnnouncement(NodeAnnouncementTests.Sample(false),false);
                await Until(()=>received.Any(p=>p.Topic=="test/nodes/json"),"Known advert exported");
                Check(!received.Any(p=>p.Topic=="test/newnodes/json"||p.Topic=="test/favnodes/json"),"New-node and favorite filters suppress old non-favorites");
                manager.PublishAnnouncement(NodeAnnouncementTests.Sample(true),true);
                await Until(()=>received.Any(p=>p.Topic=="test/newnodes/json")&&received.Any(p=>p.Topic=="test/favnodes/json"),"New favorite advert reaches both filtered clients");
                Check(!received.Any(p=>p.Topic=="test/channel/kind"&&p.Payload=="Advert"),"Announcements do not leak into chat bindings");
                var m=Message();manager.PublishMessage(m,new string('a',64),"DM2TK",null,false);
                await Until(()=>received.Any(p=>p.Topic=="test/readonly/json"),"channel message export");Check(!received.Any(p=>p.Topic=="test/channel/json"),"Favorites-only wire filter");
                manager.PublishMessage(m,new string('a',64),"DM2TK",null,true);
                await Until(()=>received.Any(p=>p.Topic=="test/channel/json"),"metadata and JSON exported");
                Check(received.Any(p=>p.Topic=="test/channel/message"&&p.Payload==m.Text)&&received.Any(p=>p.Topic=="test/channel/sender/name"&&p.Payload=="DM2TK"),"Real MQTT payloads and subtopics");
                manager.PublishMessage(Message(StoredMessageKind.Direct),new string('a',64),"DM2TK","self",true);
                await Until(()=>received.Any(p=>p.Topic=="test/direct/json")&&received.Any(p=>p.Topic=="test/all/json"),"direct subscriptions independent");
                var t=MqttTelemetry.From(123,new Position { LatitudeI=480000000,LongitudeI=120000000 },DateTime.UtcNow);manager.PublishTelemetry(t,new string('a',64),"DM2TK",true);
                await Until(()=>received.Any(p=>p.Topic=="test/telemetry/telemetry/latitude"&&p.Payload=="48"),"position telemetry wire export");
                await inject("test/channel/send","hello channel",false);await inject("test/direct/send","hello direct",false);await inject("test/all/send","hello all",false);
                await inject("test/readonly/send","must not send",false);await inject("test/telemetry/send","must not send",false);await inject("test/all/send/"+new string('b',64),"arbitrary target must not send",false);
                await Until(()=>sent.Count==3,"three explicit sends");await Task.Delay(200);
                Check(sent.Any(s=>s=="channel||hello channel")&&sent.Any(s=>s=="direct||hello direct")&&sent.Any(s=>s=="all|"+new string('a',64)+"|hello all")&&sent.Count==3,"Opt-in, fixed binding and replies restricted to last published direct sender");
                await inject("test/direct/send","fail",false);await Until(()=>manager.GetStatuses().First(s=>s.Name=="direct").LastError.Contains("simulated"),"send error reported");
                await inject("test/direct/send","recovered",false);await Until(()=>sent.Count==4,"send worker survives errors");
                var nextDirect=Message(StoredMessageKind.Direct);nextDirect.Text="new sender";
                manager.PublishMessage(nextDirect,new string('b',64),"Other","self",true);
                await Until(()=>received.Any(p=>p.Topic=="test/all/message"&&p.Payload=="new sender"),"Second direct sender published");
                await inject("test/all/send","reply to second",false);await Until(()=>sent.Count==5,"Reply follows new sender");
                Check(sent.Any(p=>p=="all|"+new string('b',64)+"|reply to second"),"Reply targets latest sender");
                var unknown=Message(StoredMessageKind.Direct);unknown.Text="unknown sender";manager.PublishMessage(unknown,null,"Unknown","self",true);
                await Until(()=>received.Any(p=>p.Topic=="test/all/message"&&p.Payload==unknown.Text),"Unknown sender exported");
                await inject("test/all/send","must not use previous sender",false);await Task.Delay(150);Check(sent.Count==5,"Unknown sender clears previous reply destination");
                // Force all connections down and verify queued data survives automatic reconnect.
                await server.StopAsync();await Until(()=>manager.GetStatuses().Any(s=>s.State=="Reconnecting"),"disconnect detected");
                int statusBeforeReconnect=received.Count(p=>p.Topic=="test/system/json");
                var offline=Message();offline.Text="queued while broker offline";manager.PublishMessage(offline,new string('a',64),"DM2TK",null,true);
                await server.StartAsync();await Until(()=>received.Any(p=>p.Topic=="test/channel/message"&&p.Payload==offline.Text),"offline event delivered after reconnect",18000);
                await Until(()=>manager.GetStatuses().Count(s=>s.State=="Connected")==9,"all clients reconnect");
                await Until(()=>received.Count(p=>p.Topic=="test/system/json")>statusBeforeReconnect,"System snapshot refreshed immediately after reconnect");
                retained=await server.GetRetainedMessagesAsync();Check(retained.Count(p=>p.Topic.Contains("/send"))==3&&sent.Count==5,"Reconnect restores retained markers without transmitting them");
            }
            finally { await manager.StopAsync();await server.StopAsync(); }
            Check(manager.GetStatuses().All(s=>s.State=="Stopped"),"Orderly shutdown of all workers");
        }
        var unavailable=Settings(FreePort(),"offline",MqttBinding.Channel);
        var bounded=new MqttClientManager(MeshProtocol.MeshCore,new[] { unavailable },(s,t,m,c)=>Task.CompletedTask);bounded.Start();
        for(int i=0;i<700;i++)bounded.PublishMessage(Message(),"id","name",null,true);
        Check(bounded.GetStatuses()[0].Pending<=500&&bounded.GetStatuses()[0].Dropped>=199,"Disconnected queue is bounded without blocking device reception");
        var stop=bounded.StopAsync();Check(await Task.WhenAny(stop,Task.Delay(5000))==stop,"Disconnected broker shutdown is cancellable");await stop;
        // Meshtastic uses the same broker bridge but its own settings and eight-channel capacity.
        var mesh=unavailable.Copy();mesh.Protocol=MeshProtocol.Meshtastic;mesh.ChannelIndex=7;mesh.Validate();mesh.ChannelIndex=8;bool bad=false;try { mesh.Validate(); }catch(ArgumentException) { bad=true; }Check(bad,"Meshtastic channel capacity respected");
    }
}
