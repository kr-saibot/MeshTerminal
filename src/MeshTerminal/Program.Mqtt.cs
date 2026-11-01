using MessageBox = MeshTerminal.SafeMessageBox;
using Dialog = MeshTerminal.SafeDialog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeshTerminal.Client;
using NStack;
using Terminal.Gui;
namespace MeshTerminal
{
    internal static partial class Program
    {
        private static MqttClientManager _mqttClients;
        private static readonly SemaphoreSlim MqttConfigurationGate=new SemaphoreSlim(1,1);
        private static async Task RestartMqttClientsAsync()
        {
            await MqttConfigurationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var previous=_mqttClients;_mqttClients=null;
                if(previous!=null)await previous.StopAsync().ConfigureAwait(false);
                if(Volatile.Read(ref _shutdownStarted)!=0)return;
                var manager=new MqttClientManager(_mesh.Protocol,_settings.MqttClients.ToArray(),SendMqttToMeshAsync,
                    settings => _store.GetNodes().Where(n => (_mesh.Device.MyNode == null || n.NodeNumber != _mesh.Device.MyNode.MyNodeNum) &&
                        !String.IsNullOrEmpty(n.NodeId) && (_mesh.Protocol != MeshProtocol.MeshCore || (n.NodeId.Length == 64 && n.NodeId.All(Uri.IsHexDigit))))
                        .Select(n => n.NodeId).ToArray(),
                    () => MqttSystemStatus.Create(_mesh.Protocol,_mesh.Device,_mesh.State,_mesh.ConnectionInfo,ConnectionTarget(),OwnDeviceDetails()));
                _mesh.EnablePacketStatisticsPolling=_settings.MqttClients.Any(c=>c!=null&&c.Enabled&&c.Protocol==_mesh.Protocol&&c.Binding==MqttBinding.SystemStatus);
                manager.Start();_mqttClients=manager;
            }
            finally { MqttConfigurationGate.Release(); }
        }
        private static async Task SendMqttToMeshAsync(MqttClientSettings settings,string target,string text,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(!settings.AllowSending||!settings.IsChatBinding)throw new InvalidOperationException("Sending is disabled.");
            if(!_mesh.IsConnected)throw new InvalidOperationException("Mesh device is disconnected; send request discarded.");
            uint destination=UInt32.MaxValue;int? channel=null;
            if(settings.Binding==MqttBinding.Channel)
            {
                channel=settings.ChannelIndex;
                if(!_mesh.Channels.Any(c=>c.Index==channel&&c.Role!=Meshtastic.Protobufs.Channel.Types.Role.Disabled))throw new InvalidOperationException("Channel is not configured on the device.");
            }
            else
            {
                var id=settings.Binding==MqttBinding.DirectNode?settings.NodeId:target;
                var node=_store.GetNodes().FirstOrDefault(n=>String.Equals(n.NodeId,id,StringComparison.OrdinalIgnoreCase));
                if(node==null)throw new InvalidOperationException("Unknown target contact ID.");
                if(settings.FavoritesOnly&&!node.IsFavorite)throw new InvalidOperationException("Target is not a favorite.");
                if(_mesh.Device.MyNode!=null&&node.NodeNumber==_mesh.Device.MyNode.MyNodeNum)throw new InvalidOperationException("Cannot send to the own device.");
                destination=node.NodeNumber;
            }
            token.ThrowIfCancellationRequested();
            // Do not cancel a device write already in progress when restarting the MQTT configuration.
            var packet=channel.HasValue?await _mesh.SendTextToChannelWithIdAsync(channel.Value,text).ConfigureAwait(false):await _mesh.SendTextWithIdAsync(destination,text).ConfigureAwait(false);
            Save(()=>
            {
                _store.AddOutgoing(packet,_mesh.Device.MyNode==null?0:_mesh.Device.MyNode.MyNodeNum,destination,text,channel,channel.HasValue?TelnetChannelName(channel.Value):null);
                var delivery=_mesh.GetMessageDelivery(packet);
                if(delivery!=null)_store.UpdateDeliveryStatus(packet,delivery.State,delivery.Error==Meshtastic.Protobufs.Routing.Types.Error.None?null:delivery.Error.ToString(),delivery.Detail);
                Ui(RefreshChats);
            });
        }
        private static StoredMeshNode MqttSender(uint number,string name=null)
        {
            var nodes=_store.GetNodes();var node=nodes.FirstOrDefault(n=>n.NodeNumber==number);
            if(_mesh.Protocol==MeshProtocol.MeshCore&&(node==null||String.IsNullOrEmpty(node.NodeId)||node.NodeId.Length!=64)&&!String.IsNullOrEmpty(name))
            {
                var matches=nodes.Where(n=>n.NodeId!=null&&n.NodeId.Length==64&&n.NodeId.All(Uri.IsHexDigit)&&String.Equals(n.LongName,name,StringComparison.Ordinal)).ToList();
                if(matches.Count==1)node=matches[0];
            }
            return node;
        }
        private static void PublishMqttMessage(StoredMeshMessage message)
        {
            var manager=_mqttClients;
            if(manager==null||!manager.HasMessageClients||message.Direction!=MessageDirection.Incoming||(_mesh.Device.MyNode!=null&&message.FromNode==_mesh.Device.MyNode.MyNodeNum))return;
            var sender=MqttSender(message.FromNode.GetValueOrDefault(),message.SenderName);
            var recipient=_mesh.Nodes.FirstOrDefault(n=>n.Number==message.ToNode);
            manager.PublishMessage(message,sender?.NodeId,!String.IsNullOrWhiteSpace(message.SenderName)?message.SenderName:sender?.LongName??sender?.ShortName,recipient?.Id,sender!=null&&sender.IsFavorite);
        }
        private static void PublishMqttTelemetry(StoredTelemetry telemetry)
        {
            var manager=_mqttClients;if(manager==null||!manager.HasTelemetryClients)return;
            Save(()=>
            {
                if(manager!=_mqttClients)return;
                var sender=MqttSender(_store.ResolveMeshCorePositionNode(telemetry.NodeNumber));
                manager.PublishTelemetry(telemetry,sender?.NodeId,sender?.LongName??sender?.ShortName,sender!=null&&sender.IsFavorite);
            });
        }
        private static void PublishMqttAnnouncement(NodeAnnouncementEventArgs announcement)
        {
            var manager=_mqttClients;if(manager==null||!manager.HasAnnouncementClients)return;
            Save(()=> { if(manager==_mqttClients)manager.PublishAnnouncement(announcement,IsFavoriteNode(announcement.Node.Num)); });
        }
        private static void ApplyMqttConfiguration()
        {
            MeshtasticSettingsStore.Save("meshtastic-settings.xml",_settings);
            Task.Run(async()=> { try { await RestartMqttClientsAsync(); }catch(Exception e) { Ui(()=>MessageBox.ErrorQuery("MQTT",e.Message,"OK")); } });
        }
        private static void ShowMqttClients()
        {
            var dialog=new Dialog("MQTT clients - "+_mesh.Protocol,110,19);
            var list=new ListView { X=1,Y=1,Width=Dim.Fill(2),Height=Dim.Fill(4) };
            var clients=new List<MqttClientSettings>();
            Action refresh=()=> { clients=_settings.MqttClients.Where(s=>s!=null&&s.Protocol==_mesh.Protocol).ToList();list.SetSource(clients.Select(s=>(s.Enabled?"[on] ":"[off] ")+s.Name+" | "+s.Server+":"+s.Port+" | "+s.Binding+" | "+s.BaseTopic).ToList()); };
            Action edit=()=> { if(list.SelectedItem<0||list.SelectedItem>=clients.Count)return;var selected=clients[list.SelectedItem];var result=EditMqttClient(selected);if(result==null)return;_settings.MqttClients[_settings.MqttClients.IndexOf(selected)]=result;ApplyMqttConfiguration();refresh(); };
            var add=new Button("Add");add.Clicked+=()=> { var result=EditMqttClient(new MqttClientSettings { Protocol=_mesh.Protocol });if(result==null)return;_settings.MqttClients.Add(result);ApplyMqttConfiguration();refresh(); };
            var change=new Button("Edit");change.Clicked+=edit;list.OpenSelectedItem+=e=>edit();
            var delete=new Button("Delete");delete.Clicked+=()=> { if(list.SelectedItem<0||list.SelectedItem>=clients.Count)return;if(MessageBox.Query("Delete MQTT client","Delete "+clients[list.SelectedItem].Name+"?","Delete","Cancel")!=0)return;_settings.MqttClients.Remove(clients[list.SelectedItem]);ApplyMqttConfiguration();refresh(); };
            var status=new Button("Status");status.Clicked+=ShowMqttStatus;
            var close=new Button("Close",true);close.Clicked+=()=>Application.RequestStop();
            dialog.Add(list);dialog.AddButton(add);dialog.AddButton(change);dialog.AddButton(delete);dialog.AddButton(status);dialog.AddButton(close);
            ListViewScrollBar.Attach(list);refresh();Application.Run(dialog,HandleTerminalGuiException);
        }
        private static MqttClientSettings EditMqttClient(MqttClientSettings source)
        {
            var dialog=new Dialog("Edit MQTT client - "+_mesh.Protocol,110,32);
            var enabled=new CheckBox("Enabled") { X=1,Y=1,Checked=source.Enabled };
            var name=new TextField(source.Name??"") { X=15,Y=3,Width=38 };
            var server=new TextField(source.Server??"") { X=15,Y=5,Width=38 };
            var port=new TextField(source.Port.ToString()) { X=15,Y=7,Width=8 };
            var tls=new CheckBox("TLS") { X=28,Y=7,Checked=source.UseTls };
            var username=new TextField(source.Username??"") { X=15,Y=9,Width=38 };
            var password=new TextField(source.Password??"") { X=15,Y=11,Width=38,Secret=true };
            var topic=new TextField(source.BaseTopic??"") { X=15,Y=13,Width=38 };
            var binding=new RadioGroup(new Rect(1,16,52,6),new ustring[] { "Channel","Direct chat with selected node","All direct messages","Telemetry","Adverts / new nodes","System status" }) { SelectedItem=(int)source.Binding };
            var favorites=new CheckBox("Favorites only") { X=1,Y=23,Checked=source.FavoritesOnly };
            var onlyNew=new CheckBox("Only newly discovered nodes") { X=1,Y=24,Checked=source.OnlyNewNodes };
            var sending=new CheckBox("Allow sending via MQTT") { X=1,Y=25,Checked=source.AllowSending };
            var interval=new TextField(source.StatusIntervalSeconds.ToString()) { X=83,Y=25,Width=8 };
            var intervalLabel=new Label("Status interval (s):") { X=57,Y=25 };
            var targetFrame=new FrameView("Channel / target node") { X=56,Y=1,Width=Dim.Fill(1),Height=23 };
            var target=new ListView { X=0,Y=0,Width=Dim.Fill(),Height=Dim.Fill() };targetFrame.Add(target);ListViewScrollBar.Attach(target);
            var nodes=_store.GetNodes().Where(n=>!String.IsNullOrEmpty(n.NodeId)&&(_mesh.Protocol!=MeshProtocol.MeshCore||(n.NodeId.Length==64&&n.NodeId.All(Uri.IsHexDigit)))).OrderByDescending(n=>n.IsFavorite).ThenBy(n=>n.LongName??n.ShortName??n.NodeId).ToList();
            var visibleNodes=new List<StoredMeshNode>();
            var selectedId=source.NodeId;var selectedChannel=source.ChannelIndex;
            bool updatingTarget=false;
            Action update=()=>
            {
                updatingTarget=true;
                if(binding.SelectedItem==(int)MqttBinding.Channel) { targetFrame.Title="Channel";target.SetSource(Enumerable.Range(0,ConfigurationChannelCount(selectedChannel)).Select(TelegramGatewayChannelName).ToList());target.SelectedItem=Math.Max(0,selectedChannel);target.Enabled=true; }
                else if(binding.SelectedItem==(int)MqttBinding.DirectNode)
                {
                    targetFrame.Title="Target node";visibleNodes=nodes.Where(n=>!favorites.Checked||n.IsFavorite).ToList();target.SetSource(visibleNodes.Select(n=>(n.IsFavorite?"* ":"  ")+(n.LongName??n.ShortName??n.NodeId)).ToList());
                    if(visibleNodes.Count>0)target.SelectedItem=Math.Max(0,visibleNodes.FindIndex(n=>n.NodeId==selectedId));target.Enabled=true;
                }
                else { targetFrame.Title="Target";target.SetSource(new List<string> { binding.SelectedItem==(int)MqttBinding.SystemStatus?"Own device / connection / radio":binding.SelectedItem==(int)MqttBinding.AdvertsAndNodes?"Live adverts / node announcements":binding.SelectedItem==(int)MqttBinding.Telemetry?"All received telemetry":"All received direct messages",binding.SelectedItem>=(int)MqttBinding.Telemetry?"Sending is unavailable.":"Reply to last sender: <base>/send" });target.Enabled=false; }
                interval.Enabled=binding.SelectedItem==(int)MqttBinding.SystemStatus;intervalLabel.Enabled=interval.Enabled;favorites.Enabled=binding.SelectedItem!=(int)MqttBinding.SystemStatus;
                onlyNew.Enabled=binding.SelectedItem==(int)MqttBinding.AdvertsAndNodes;
                sending.Enabled=binding.SelectedItem<(int)MqttBinding.Telemetry;if(!sending.Enabled)sending.Checked=false;
                if(binding.SelectedItem==(int)MqttBinding.DirectNode) selectedId=visibleNodes.Count>0?visibleNodes[target.SelectedItem].NodeId:"";
                updatingTarget=false;
            };
            target.SelectedItemChanged+=e=> { if(updatingTarget)return;if(binding.SelectedItem==(int)MqttBinding.Channel)selectedChannel=e.Item;else if(binding.SelectedItem==(int)MqttBinding.DirectNode&&e.Item>=0&&e.Item<visibleNodes.Count)selectedId=visibleNodes[e.Item].NodeId; };
            binding.SelectedItemChanged+=e=>update();favorites.Toggled+=e=>update();
            dialog.Add(enabled,new Label("Name:") { X=1,Y=3 },name,new Label("Server:") { X=1,Y=5 },server,new Label("Port:") { X=1,Y=7 },port,tls,
                new Label("Username:") { X=1,Y=9 },username,new Label("Password:") { X=1,Y=11 },password,new Label("Base topic:") { X=1,Y=13 },topic,
                new Label("Binding:") { X=1,Y=15 },binding,favorites,onlyNew,sending,intervalLabel,interval,targetFrame,new Label("Receive: <base>/message + metadata + <base>/json") { X=1,Y=27 });
            MqttClientSettings result=null;var save=new Button("Save",true);save.Clicked+=()=>
            {
                int parsedPort;if(!Int32.TryParse(port.Text.ToString(),out parsedPort)) { MessageBox.ErrorQuery("MQTT","Enter a valid port.","OK");return; }
                int parsedInterval;
                if(!Int32.TryParse(interval.Text.ToString(),out parsedInterval)) { MessageBox.ErrorQuery("MQTT","Enter a valid status interval.","OK");return; }
                var settings=source.Copy();settings.Name=name.Text.ToString().Trim();settings.Server=server.Text.ToString().Trim();settings.Port=parsedPort;settings.UseTls=tls.Checked;settings.Username=username.Text.ToString();settings.Password=password.Text.ToString();settings.BaseTopic=topic.Text.ToString().Trim();settings.Enabled=enabled.Checked;settings.Binding=(MqttBinding)binding.SelectedItem;settings.FavoritesOnly=favorites.Checked;settings.AllowSending=sending.Checked;settings.OnlyNewNodes=onlyNew.Checked;settings.StatusIntervalSeconds=parsedInterval;settings.ChannelIndex=selectedChannel;settings.NodeId=selectedId;
                try { settings.Validate(); }catch(Exception e) { MessageBox.ErrorQuery("MQTT",e.Message,"OK");return; }
                if(_settings.MqttClients.Any(s=>s!=source&&s.Enabled&&settings.Enabled&&s.Protocol==settings.Protocol&&String.Equals(s.Server,settings.Server,StringComparison.OrdinalIgnoreCase)&&s.Port==settings.Port&&s.BaseTopic==settings.BaseTopic))
                { MessageBox.ErrorQuery("MQTT","Another active client already uses this broker and base topic. Choose a different base topic.","OK");return; }
                if(String.IsNullOrWhiteSpace(settings.Name))settings.Name=settings.Server;result=settings;Application.RequestStop();
            };
            var cancel=new Button("Cancel");cancel.Clicked+=()=>Application.RequestStop();dialog.AddButton(save);dialog.AddButton(cancel);update();Application.Run(dialog,HandleTerminalGuiException);return result;
        }
        private static void ShowMqttStatus()
        {
            var dialog=new Dialog("MQTT client status",110,24);var output=new TextView { ReadOnly=true,WordWrap=true,X=1,Y=1,Width=Dim.Fill(2),Height=Dim.Fill(3) };
            Action refresh=()=> { var text=new StringBuilder();var statuses=_mqttClients?.GetStatuses();if(statuses==null||statuses.Count==0)text.AppendLine("No MQTT clients configured for this protocol.");else foreach(var s in statuses) { text.AppendLine(s.Name+" | "+s.State);text.AppendLine("Published events: "+s.Published+" | Pending: "+s.Pending+" | Sent to mesh: "+s.SentToMesh+" | Dropped: "+s.Dropped);if(!String.IsNullOrEmpty(s.LastError))text.AppendLine("Last error: "+s.LastError);text.AppendLine(); }output.Text=text.ToString(); };
            dialog.Add(output);var close=new Button("Close",true);close.Clicked+=()=>Application.RequestStop();dialog.AddButton(close);refresh();var timer=Application.MainLoop.AddTimeout(TimeSpan.FromSeconds(1),loop=> { refresh();return true; });try { Application.Run(dialog,HandleTerminalGuiException); }finally { Application.MainLoop.RemoveTimeout(timer); }
        }
    }
}
