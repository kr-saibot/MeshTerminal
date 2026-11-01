using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
namespace MeshTerminal.Client
{
    public sealed class MqttClientStatus
    {
        public string Name { get; internal set; }
        public string State { get; internal set; }
        public string LastError { get; internal set; }
        public int Pending { get; internal set; }
        public long Published { get; internal set; }
        public long SentToMesh { get; internal set; }
        public long Dropped { get; internal set; }
    }
    /// <summary>Each broker has independent bounded queues. Device/database callbacks never wait for a broker.</summary>
    public sealed class MqttClientManager
    {
        public const string SendTopicPlaceholder = "{\"meshterminal\":\"send-placeholder\"}";
        private readonly Runner[] _clients;
        public MqttClientManager(MeshProtocol protocol, IEnumerable<MqttClientSettings> settings, Func<MqttClientSettings,string,string,CancellationToken,Task> send, Func<MqttClientSettings,IEnumerable<string>> knownTargets = null, Func<Dictionary<string,object>> systemStatus = null)
        {
            _clients=settings.Where(s=>s!=null&&s.Protocol==protocol).Select(s=>new Runner(s.Copy(),send,knownTargets,systemStatus)).ToArray();
        }
        public bool HasTelemetryClients { get { return _clients.Any(c=>c.Settings.Enabled&&c.Settings.Binding==MqttBinding.Telemetry); } }
        public bool HasMessageClients { get { return _clients.Any(c=>c.Settings.Enabled&&c.Settings.IsChatBinding); } }
        public void Start() { foreach(var c in _clients) c.Start(); }
        public async Task StopAsync() { await Task.WhenAll(_clients.Select(c=>c.StopAsync())).ConfigureAwait(false); }
        public IList<MqttClientStatus> GetStatuses() { return _clients.Select(c=>c.Status()).ToList(); }
        public void PublishMessage(StoredMeshMessage message,string senderId,string senderName,string recipientId,bool favorite)
        {
            foreach(var c in _clients) if(c.Settings.Enabled&&MqttPayload.Matches(c.Settings,message,senderId,favorite))
                c.Enqueue(MqttPayload.Publications(c.Settings.BaseTopic,MqttPayload.Message(c.Settings.Protocol,message,senderId,senderName,recipientId)));
        }
        public void PublishTelemetry(StoredTelemetry telemetry,string id,string name,bool favorite)
        {
            foreach(var c in _clients) if(c.Settings.Enabled&&c.Settings.Binding==MqttBinding.Telemetry&&(!c.Settings.FavoritesOnly||favorite))
                c.Enqueue(MqttPayload.Publications(c.Settings.BaseTopic,MqttPayload.Telemetry(c.Settings.Protocol,telemetry,id,name)));
        }
        public bool HasAnnouncementClients { get { return _clients.Any(c=>c.Settings.Enabled&&c.Settings.Binding==MqttBinding.AdvertsAndNodes); } }
        public void PublishAnnouncement(NodeAnnouncementEventArgs announcement,bool favorite)
        {
            foreach(var c in _clients)if(c.Settings.Enabled&&c.Settings.Binding==MqttBinding.AdvertsAndNodes&&(!c.Settings.FavoritesOnly||favorite)&&(!c.Settings.OnlyNewNodes||announcement.IsNew))
                c.Enqueue(MqttPayload.Publications(c.Settings.BaseTopic,MqttPayload.Announcement(announcement)));
        }
        private sealed class Command { public string Target; public string Text; }
        private sealed class Runner
        {
            internal readonly MqttClientSettings Settings;
            private readonly Func<MqttClientSettings,string,string,CancellationToken,Task> _send;
            private readonly Func<MqttClientSettings,IEnumerable<string>> _knownTargets;
            private readonly Func<Dictionary<string,object>> _systemStatus;
            private readonly BlockingCollection<IList<MqttPublication>> _outbox=new BlockingCollection<IList<MqttPublication>>(500);
            private readonly BlockingCollection<Command> _inbox=new BlockingCollection<Command>(32);
            private readonly CancellationTokenSource _stop=new CancellationTokenSource();
            private Task _worker=Task.CompletedTask, _sender=Task.CompletedTask;
            private string _state="Disabled",_error="";
            private string _lastDirectSenderId;
            private long _published,_sent,_dropped;
            private int _started;
            internal Runner(MqttClientSettings settings,Func<MqttClientSettings,string,string,CancellationToken,Task> send,Func<MqttClientSettings,IEnumerable<string>> knownTargets,Func<Dictionary<string,object>> systemStatus) { Settings=settings;_send=send;_knownTargets=knownTargets;_systemStatus=systemStatus; }
            internal MqttClientStatus Status() { return new MqttClientStatus { Name=Settings.Name,State=_state,LastError=_error,Pending=_outbox.Count,Published=Interlocked.Read(ref _published),SentToMesh=Interlocked.Read(ref _sent),Dropped=Interlocked.Read(ref _dropped) }; }
            internal void Start()
            {
                if(!Settings.Enabled||Interlocked.Exchange(ref _started,1)!=0)return;
                try { Settings.Validate(); } catch(Exception e) { _state="Invalid configuration";_error=e.Message;return; }
                _state="Connecting";_worker=Task.Run(RunAsync);_sender=Task.Run(SendAsync);
            }
            internal async Task StopAsync()
            {
                _stop.Cancel();
                await Task.WhenAll(_worker,_sender).ConfigureAwait(false); _state="Stopped";
            }
            internal void Enqueue(IList<MqttPublication> publications)
            {
                if(_stop.IsCancellationRequested||_state=="Invalid configuration")return;
                if(!_outbox.TryAdd(publications)) Interlocked.Increment(ref _dropped);
            }
            private void Error(Exception error)
            {
                var message=error.GetBaseException().Message;
                if(!String.IsNullOrEmpty(Settings.Password))message=message.Replace(Settings.Password,"***");
                if(!String.IsNullOrEmpty(Settings.Username))message=message.Replace(Settings.Username,"***");
                _error=DateTime.UtcNow.ToString("HH:mm:ss")+" UTC: "+message;
            }
            private Task ReceiveAsync(MqttApplicationMessageReceivedEventArgs args)
            {
                var message=args.ApplicationMessage;
                if(_stop.IsCancellationRequested||!Settings.AllowSending||!Settings.IsChatBinding)return Task.CompletedTask;
                var initialPayload=message.PayloadSegment;
                if(initialPayload.Count==SendTopicPlaceholder.Length && Encoding.UTF8.GetString(initialPayload.Array,initialPayload.Offset,initialPayload.Count)==SendTopicPlaceholder)return Task.CompletedTask;
                if(message.Retain) { _error="Retained send request ignored.";return Task.CompletedTask; }
                var prefix=Settings.BaseTopic+"/send";
                if(message.Topic!=prefix)return Task.CompletedTask;
                string target=null;
                if(Settings.Binding==MqttBinding.AllDirect)
                {
                    target=Volatile.Read(ref _lastDirectSenderId);
                    if(String.IsNullOrEmpty(target)) { _error="No known sender to reply to. Wait for a direct message on MQTT.";return Task.CompletedTask; }
                }
                var segment=message.PayloadSegment;
                if(segment.Count==0||segment.Count>4096) { _error="Send request must contain 1-4096 UTF-8 bytes (radio limits also apply).";return Task.CompletedTask; }
                try
                {
                    var text=new UTF8Encoding(false,true).GetString(segment.Array,segment.Offset,segment.Count);
                    if(String.IsNullOrWhiteSpace(text)||text.Contains("\0"))throw new ArgumentException("Empty or invalid send request.");
                    if(!_inbox.TryAdd(new Command { Target=target,Text=text })) { Interlocked.Increment(ref _dropped);_error="Send queue full; request discarded."; }
                }
                catch(Exception e) { Error(e); }
                return Task.CompletedTask;
            }
            private async Task SendAsync()
            {
                try
                {
                    while(!_stop.IsCancellationRequested)
                    {
                        Command command;
                        if(!_inbox.TryTake(out command,250,_stop.Token))continue;
                        try { _stop.Token.ThrowIfCancellationRequested(); await _send(Settings,command.Target,command.Text,_stop.Token).ConfigureAwait(false);Interlocked.Increment(ref _sent); }
                        catch(OperationCanceledException) when(_stop.IsCancellationRequested) { break; }
                        catch(Exception e) { Error(e); }
                    }
                }
                catch(OperationCanceledException) { }
            }
            private async Task RunAsync()
            {
                IList<MqttPublication> pending=null;
                var clientId="MeshTerminal-"+Guid.NewGuid().ToString("N");
                while(!_stop.IsCancellationRequested)
                {
                    using(var client=new MqttFactory().CreateMqttClient())
                    {
                        client.ApplicationMessageReceivedAsync+=ReceiveAsync;
                        try
                        {
                            _state="Connecting";
                            Volatile.Write(ref _lastDirectSenderId,null);
                            var options=new MqttClientOptionsBuilder().WithTcpServer(Settings.Server,Settings.Port).WithClientId(clientId).WithCleanSession().WithTimeout(TimeSpan.FromSeconds(10));
                            if(!String.IsNullOrEmpty(Settings.Username)||!String.IsNullOrEmpty(Settings.Password))options.WithCredentials(Settings.Username,Settings.Password);
                            if(Settings.UseTls)options.WithTlsOptions(o=>o.WithSslProtocols(SslProtocols.Tls12));
                            await client.ConnectAsync(options.Build(),_stop.Token).ConfigureAwait(false);
                            if(Settings.AllowSending&&Settings.IsChatBinding)
                            {
                                var topic=Settings.BaseTopic+"/send";
                                // Commands use QoS 0: replaying a broker request must not silently repeat a radio transmission.
                                var sub=new MqttClientSubscribeOptionsBuilder().WithTopicFilter(f=>f.WithTopic(topic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)).Build();
                                var ack=await client.SubscribeAsync(sub,_stop.Token).ConfigureAwait(false);
                                if(ack.Items.Any(i=>(int)i.ResultCode>=128))throw new InvalidOperationException("Broker rejected send-topic subscription.");
                                // A zero-byte retained payload would delete the topic. Use a reserved,
                                // nonempty marker which ReceiveAsync ignores, including live broker echoes.
                                if(Settings.Binding==MqttBinding.AllDirect)
                                {
                                    // Remove retained per-contact markers created by previous versions.
                                    foreach(var id in (_knownTargets?.Invoke(Settings) ?? Enumerable.Empty<string>())
                                        .Where(id=>!String.IsNullOrWhiteSpace(id)&&id.IndexOfAny(new[] {'/','+','#','\0'})<0).Distinct(StringComparer.OrdinalIgnoreCase))
                                    {
                                        var obsolete=new MqttApplicationMessageBuilder().WithTopic(topic+"/"+id).WithPayload("").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).WithRetainFlag(true).Build();
                                        var deleted=await client.PublishAsync(obsolete,_stop.Token).ConfigureAwait(false);
                                        if((int)deleted.ReasonCode>=128)throw new InvalidOperationException("Broker rejected obsolete send-topic cleanup.");
                                    }
                                }
                                var topics = new[] { topic };
                                foreach(var sendTopic in topics)
                                {
                                    var marker=new MqttApplicationMessageBuilder().WithTopic(sendTopic).WithPayload(SendTopicPlaceholder).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).WithRetainFlag(true).Build();
                                    var markerResult=await client.PublishAsync(marker,_stop.Token).ConfigureAwait(false);
                                    if((int)markerResult.ReasonCode>=128)throw new InvalidOperationException("Broker rejected retained send-topic placeholder.");
                                }
                            }
                            _state="Connected";
                            var nextSystemStatusUtc=DateTime.MinValue;
                            if(Settings.Binding==MqttBinding.SystemStatus)pending=null;
                            while(!_stop.IsCancellationRequested&&client.IsConnected)
                            {
                                if(Settings.Binding==MqttBinding.SystemStatus)
                                {
                                    if(DateTime.UtcNow<nextSystemStatusUtc) { await Task.Delay(250,_stop.Token).ConfigureAwait(false);continue; }
                                    if(_systemStatus==null)throw new InvalidOperationException("System status provider is unavailable.");
                                    pending=MqttPayload.Publications(Settings.BaseTopic,_systemStatus());
                                    nextSystemStatusUtc=DateTime.UtcNow.AddSeconds(Settings.StatusIntervalSeconds);
                                }
                                else if(pending==null&&!_outbox.TryTake(out pending,250,_stop.Token))continue;
                                foreach(var item in pending)
                                {
                                    // Set the reply destination before publishing the visible text/JSON.
                                    // Capture it when a command arrives, so a newer message cannot redirect a queued reply.
                                    if(Settings.Binding==MqttBinding.AllDirect&&(item.Topic==Settings.BaseTopic+"/message"||item.Topic==Settings.BaseTopic+"/json"))
                                        Volatile.Write(ref _lastDirectSenderId,pending.FirstOrDefault(p=>p.Topic==Settings.BaseTopic+"/sender/id")?.Payload);
                                    var packet=new MqttApplicationMessageBuilder().WithTopic(item.Topic).WithPayload(item.Payload).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).WithRetainFlag(false).Build();
                                    var result=await client.PublishAsync(packet,_stop.Token).ConfigureAwait(false);
                                    if((int)result.ReasonCode>=128)throw new InvalidOperationException("Broker rejected publication.");
                                }
                                pending=null;Interlocked.Increment(ref _published);
                            }
                        }
                        catch(OperationCanceledException) when(_stop.IsCancellationRequested) { }
                        catch(Exception e) { Error(e); }
                        finally
                        {
                            if(client.IsConnected) try { using(var timeout=new CancellationTokenSource(1000)) await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(),timeout.Token).ConfigureAwait(false); } catch { }
                        }
                    }
                    if(!_stop.IsCancellationRequested)
                    {
                        _state="Reconnecting";
                        try { await Task.Delay(3000,_stop.Token).ConfigureAwait(false); } catch(OperationCanceledException) { }
                    }
                }
                _state="Stopped";
            }
        }
    }
}
