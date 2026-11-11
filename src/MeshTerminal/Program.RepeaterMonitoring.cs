using Dialog = MeshTerminal.SafeDialog;
using MessageBox = MeshTerminal.SafeMessageBox;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeshTerminal.Client;
using Terminal.Gui;
namespace MeshTerminal
{
    internal static partial class Program
    {
        private static readonly CancellationTokenSource _repeaterMonitorStop=new CancellationTokenSource();
        private static readonly ConcurrentDictionary<uint,bool> _repeaterMonitorCollecting=new ConcurrentDictionary<uint,bool>();
        private static readonly ConcurrentDictionary<string,string> _repeaterMonitorStatus=new ConcurrentDictionary<string,string>();
        private static readonly RepeaterMonitorSchedule _repeaterMonitorSchedule=new RepeaterMonitorSchedule();
        private static int _repeaterMonitorBusy;
        private static async Task EnsureRepeaterMonitoringLogin(uint number,string key,CancellationToken token,bool force=false)
        {
            if(!force && _mesh.IsMeshCoreRepeaterAuthorized(number)) return;
            try { await _mesh.LoginMeshCoreRepeaterAsync(number,"",token).ConfigureAwait(false); }
            catch(OperationCanceledException) { throw; }
            catch
            {
                var saved=_settings.RepeaterCredentials?.FirstOrDefault(c=>String.Equals(c.PublicKey,key,StringComparison.OrdinalIgnoreCase));
                if(saved==null) throw;
                string secret=saved.Read();
                try { await _mesh.LoginMeshCoreRepeaterAsync(number,secret,token).ConfigureAwait(false); }
                finally { secret=null; }
            }
        }
        private static void QueueRepeaterMonitoring()
        {
            if(_mesh.Protocol!=MeshProtocol.MeshCore||!_mesh.IsConnected||_mesh.IsSynchronizingMeshCoreNodes||_repeaterMonitorStop.IsCancellationRequested||Volatile.Read(ref _shutdownStarted)!=0) return;
            if(Interlocked.CompareExchange(ref _repeaterMonitorBusy,1,0)!=0) return;
            var jobs=(_settings.RepeaterMonitors??new System.Collections.Generic.List<RepeaterMonitorSettings>()).ToArray();
            _ = Task.Run(async ()=> {
                try
                {
                    foreach(var job in jobs)
                    {
                        if(_repeaterMonitorStop.IsCancellationRequested||!_mesh.IsConnected||_mesh.Protocol!=MeshProtocol.MeshCore) break;
                        var node=_mesh.Nodes.FirstOrDefault(n=>n.MeshCoreContactType==2&&String.Equals(n.Id,job?.PublicKey,StringComparison.OrdinalIgnoreCase));
                        if(node==null||!_repeaterMonitorSchedule.TryBegin(job,_mesh.Device.NodeId,DateTime.UtcNow)) continue;
                        var device=_mesh.Device.NodeId;
                        using(var stop=CancellationTokenSource.CreateLinkedTokenSource(_repeaterMonitorStop.Token))
                        {
                            stop.CancelAfter(TimeSpan.FromMinutes(3));
                            _repeaterMonitorCollecting[node.Number]=true;
                            try
                            {
                                _repeaterMonitorStatus[job.PublicKey]="Reading status...";
                                await EnsureRepeaterMonitoringLogin(node.Number,node.Id,stop.Token).ConfigureAwait(false);
                                MeshCoreRepeaterStatus status;
                                try { status=await _mesh.RequestMeshCoreRepeaterStatusAsync(node.Number,stop.Token).ConfigureAwait(false); }
                                catch(OperationCanceledException) { throw; }
                                catch(Exception ex) when(ex is System.IO.IOException || ex is TimeoutException)
                                {
                                    await EnsureRepeaterMonitoringLogin(node.Number,node.Id,stop.Token,true).ConfigureAwait(false);
                                    status=await _mesh.RequestMeshCoreRepeaterStatusAsync(node.Number,stop.Token).ConfigureAwait(false);
                                }
                                if(job.IncludeNeighborCount)
                                {
                                    try { status.NeighborCount=await _mesh.RequestMeshCoreRepeaterNeighborCountAsync(node.Number,stop.Token).ConfigureAwait(false); }
                                    catch(OperationCanceledException) when(!_repeaterMonitorStop.IsCancellationRequested && _mesh.IsConnected) { status.NeighborCountError="Neighbor-count request timed out."; }
                                    catch(OperationCanceledException) { throw; }
                                    catch(Exception ex) { status.NeighborCountError=ex.Message; }
                                }
                                if(!String.Equals(device,_mesh.Device.NodeId,StringComparison.OrdinalIgnoreCase)) continue;
                                if(_mqttClients?.HasTelemetryClients==true) PublishMqttTelemetry(MqttTelemetry.From(status));
                                Save(()=> { _store.AddMeshCoreRepeaterStatus(status);RefreshTelemetryViews(status.NodeNumber); });
                                _repeaterMonitorStatus[job.PublicKey]="Last poll: "+status.ReceivedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+(status.NeighborCountError==null?" - saved":" - status saved; neighbor count unavailable");
                            }
                            catch(OperationCanceledException) { _repeaterMonitorStatus[job.PublicKey]="Status poll cancelled or timed out."; }
                            catch(Exception ex) { _repeaterMonitorStatus[job.PublicKey]="Last poll failed: "+ex.Message; }
                            finally { bool ignored;_repeaterMonitorCollecting.TryRemove(node.Number,out ignored);_repeaterMonitorSchedule.Complete(job,DateTime.UtcNow); }
                        }
                    }
                }
                finally { Interlocked.Exchange(ref _repeaterMonitorBusy,0); }
            });
        }
        private static void ShowRepeaterMonitoring(MeshNode node)
        {
            if(String.IsNullOrEmpty(_mesh.Device.NodeId)) { MessageBox.Query("Repeater monitoring","Connect and synchronize the own device first.","OK");return; }
            var device=_mesh.Device.NodeId;
            var existing=_settings.RepeaterMonitors?.FirstOrDefault(c=>String.Equals(c.DevicePublicKey,device,StringComparison.OrdinalIgnoreCase)&&String.Equals(c.PublicKey,node.Id,StringComparison.OrdinalIgnoreCase));
            var dialog=new Dialog("Automatic status: "+node.LongName,94,17);
            var enabled=new CheckBox("Automatically read and store repeater status") { X=1,Y=1,Checked=existing?.Enabled??false };
            var interval=new TextField((existing?.IntervalMinutes??15).ToString()) { X=27,Y=3,Width=7 };
            var neighbors=new CheckBox("Also request the total neighbor count (if supported)") { X=1,Y=5,Checked=existing?.IncludeNeighborCount??true };
            var progress=new Label("") { X=1,Y=11,Width=Dim.Fill(2) };
            Action refresh=()=> { string value;progress.Text=_repeaterMonitorStatus.TryGetValue(node.Id,out value)?value:"No poll in this program session yet."; };
            refresh();
            dialog.Add(enabled,new Label("Interval in minutes:") { X=1,Y=3 },interval,neighbors,
                new Label("Uses the existing login / repeater ACL; a saved password is used if needed.") { X=1,Y=7 },
                new Label("Enabled monitoring saves its status even when general telemetry storage is off.") { X=1,Y=8 },
                new Label("Telemetry MQTT clients receive the same snapshot, subject to their favorite filter.") { X=1,Y=9 },progress);
            var save=new Button("Save");var cancel=new Button("Cancel");
            save.Clicked+=()=> {
                int minutes;if(!Int32.TryParse(interval.Text.ToString(),out minutes)||minutes<1||minutes>10080) { MessageBox.ErrorQuery("Interval","Enter 1 to 10080 minutes.","OK");return; }
                var job=new RepeaterMonitorSettings { DevicePublicKey=device,PublicKey=node.Id,Name=node.LongName,Enabled=enabled.Checked,IntervalMinutes=minutes,IncludeNeighborCount=neighbors.Checked };
                var previous=_settings.RepeaterMonitors;
                _settings.RepeaterMonitors=(previous??new System.Collections.Generic.List<RepeaterMonitorSettings>()).Where(c=>!(String.Equals(c.DevicePublicKey,device,StringComparison.OrdinalIgnoreCase)&&String.Equals(c.PublicKey,node.Id,StringComparison.OrdinalIgnoreCase))).Concat(new[]{job}).ToList();
                try { MeshtasticSettingsStore.Save("meshtastic-settings.xml",_settings);_repeaterMonitorSchedule.Reset(job);Application.RequestStop(); }
                catch(Exception ex) { _settings.RepeaterMonitors=previous;MessageBox.ErrorQuery("Monitoring",ex.Message,"OK"); }
            };
            cancel.Clicked+=()=>Application.RequestStop();dialog.AddButton(save);dialog.AddButton(cancel);Application.Run(dialog);
        }
    }
}
