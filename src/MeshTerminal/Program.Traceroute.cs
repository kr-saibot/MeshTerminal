using Dialog = MeshTerminal.SafeDialog;
using MessageBox = MeshTerminal.SafeMessageBox;
using System;
using System.Linq;
using System.Threading;
using MeshTerminal.Client;
using Terminal.Gui;
namespace MeshTerminal
{
    internal static partial class Program
    {
        private static void ShowMeshtasticTraceroute(StoredMeshNode node)
        {
            var dialog=new Dialog("Traceroute: "+(node.LongName??node.NodeId),Math.Min(110,Application.Driver.Cols),Math.Min(32,Application.Driver.Rows));
            var forwardFrame=new FrameView("TO node") { X=1,Y=1,Width=Dim.Percent(50)-2,Height=Dim.Fill(7) };
            var backFrame=new FrameView("FROM node") { X=Pos.Right(forwardFrame)+1,Y=1,Width=Dim.Fill(1),Height=Dim.Fill(7) };
            var forward=new TextView { Width=Dim.Fill(),Height=Dim.Fill(),ReadOnly=true,WordWrap=false };
            var back=new TextView { Width=Dim.Fill(),Height=Dim.Fill(),ReadOnly=true,WordWrap=false };
            forwardFrame.Add(forward); backFrame.Add(back);
            var channels=_mesh.Channels.Where(c=>c.Role!=Meshtastic.Protobufs.Channel.Types.Role.Disabled).OrderBy(c=>c.Index).ToArray();
            int channel=channels.Length==0?0:channels[0].Index;
            var channelButton=new Button("Channel: "+channel+" "+channels.FirstOrDefault()?.Name) { X=1,Y=Pos.AnchorEnd(6) };
            channelButton.Clicked+=()=> {
                var choose=new Dialog("Traceroute channel",60,16);
                var list=new ListView(channels.Select(c=>c.Index+": "+c.Name).ToList()) { X=1,Y=1,Width=Dim.Fill(2),Height=Dim.Fill(3) };
                var select=new Button("Select"); var cancel=new Button("Cancel");
                select.Clicked+=()=> { if(list.SelectedItem>=0&&list.SelectedItem<channels.Length) { channel=channels[list.SelectedItem].Index; channelButton.Text="Channel: "+channel+" "+channels[list.SelectedItem].Name; Application.RequestStop(); } };
                cancel.Clicked+=()=>Application.RequestStop(); choose.Add(list); choose.AddButton(select);choose.AddButton(cancel); Application.Run(choose,HandleTerminalGuiException);
            };
            var hops=new TextField((_mesh.Device.LoRaConfig?.HopLimit??3).ToString()) { X=14,Y=Pos.AnchorEnd(5),Width=3 };
            var status=new Label("Start traceroute sends a radio request; allow up to 120 seconds.") { X=1,Y=Pos.AnchorEnd(4),Width=Dim.Fill(2) };
            var note=new Label("Snapshot only. TO: white / FROM: yellow. Unknown relays have no map position.") { X=1,Y=Pos.AnchorEnd(3),Width=Dim.Fill(2) };
            var start=new Button("Start traceroute"); var map=new Button("Show on map") { Enabled=false };
            var copy=new Button("Copy"); var close=new Button("Close",true);
            bool closed=false,busy=false,showMap=false; MeshCoreNodePaths paths=null;
            var stop=new CancellationTokenSource();
            Action<MeshtasticTracerouteResult> display=result=> {
                if(result==null) { forward.Text=back.Text=PadPathText("No saved traceroute available."); return; }
                paths=result.ToPaths(_store.GetNodes());
                string stamp="Received (PC): "+result.ReceivedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+"\n\n";
                forward.Text=PadPathText(stamp+(paths.OutgoingSteps==null?paths.Outgoing:MeshtasticClient.FormatMeshCorePathVertical(paths.OutgoingSteps)));
                back.Text=PadPathText(stamp+(paths.IncomingAdvertSteps==null?paths.IncomingAdvert:MeshtasticClient.FormatMeshCorePathVertical(paths.IncomingAdvertSteps)));
                map.Enabled=paths.OutgoingSteps!=null||paths.IncomingAdvertSteps!=null;
                status.Text="Saved traceroute shown. Start a new request to measure the current route.";
            };
            try { display(_mesh.Device.MyNode==null?null:_store.GetTraceroute(_mesh.Device.MyNode.MyNodeNum,node.NodeNumber)); }
            catch(Exception ex) { status.Text="Could not load saved traceroute: "+ex.Message; }
            start.Clicked+=async ()=> {
                if(busy) return;
                int hopLimit; if(!Int32.TryParse(hops.Text.ToString(),out hopLimit)||hopLimit<0||hopLimit>7) { MessageBox.ErrorQuery("Traceroute","Hop limit must be between 0 and 7.","OK"); return; }
                busy=true;start.Enabled=false;channelButton.Enabled=false;hops.Enabled=false;
                status.Text="Waiting for traceroute response (timeout: 120 seconds)...";
                try {
                    var result=await _mesh.RequestTracerouteAsync(node.NodeNumber,channel,hopLimit,stop.Token).ConfigureAwait(false);
                    _store.SaveTraceroute(result);
                    Ui(()=> { if(!closed) { display(result);status.Text="Traceroute received and saved. This is a snapshot of the measured route."; } });
                }
                catch(OperationCanceledException) { Ui(()=> { if(!closed) status.Text="Traceroute cancelled."; }); }
                catch(Exception ex) { Ui(()=> { if(!closed) status.Text=ex.Message+" Previous saved result remains visible."; }); }
                finally { Ui(()=> { if(!closed) { busy=false;start.Enabled=true;channelButton.Enabled=true;hops.Enabled=true; } }); }
            };
            copy.Clicked+=()=>CopyTextWithFallback("TO node\n"+forward.Text.ToString()+"\n\nFROM node\n"+back.Text.ToString(),"Traceroute copied.");
            map.Clicked+=()=> { if(paths==null) return; var points=MeshCorePathMap.Build(paths,"Meshtastic traceroute"); if(!points.Any(p=>p.Shape=="Point")) { MessageBox.Query("Traceroute","No known positions are available for this route.","OK");return; } TemporaryMeshCorePaths.Clear();TemporaryMeshCorePaths.AddRange(points);_temporaryMeshCorePathsVisible=true;showMap=true;Application.RequestStop(); };
            close.Clicked+=()=>Application.RequestStop();
            dialog.Add(forwardFrame,backFrame,channelButton,new Label("Hop limit:") { X=1,Y=Pos.AnchorEnd(5) },hops,status,note);
            dialog.AddButton(start);dialog.AddButton(copy);dialog.AddButton(map);dialog.AddButton(close);
            try { Application.Run(dialog,HandleTerminalGuiException); } finally { closed=true;stop.Cancel();stop.Dispose(); }
            if(showMap) ShowTemporaryPathMap();
        }
        private static void ShowTemporaryPathMap()
        {
            ShowMapPage();
            var points=TemporaryMeshCorePaths.Where(p=>p.Shape=="Point").ToArray();
            var minLat=points.Min(p=>p.Latitude);var maxLat=points.Max(p=>p.Latitude);var minLon=points.Min(p=>p.Longitude);var maxLon=points.Max(p=>p.Longitude);
            var lat=(minLat+maxLat)/2;var lon=(minLon+maxLon)/2;
            var north=MeshtasticClient.GetDistanceMeters(minLat,lon,maxLat,lon).GetValueOrDefault();var east=MeshtasticClient.GetDistanceMeters(lat,minLon,lat,maxLon).GetValueOrDefault();
            var scale=Math.Max(10,Math.Max(north/Math.Max(1,_nodeMap.Frame.Height-6),east/Math.Max(1,(_nodeMap.Frame.Width-12)*.5))*1.3);
            _nodeMap.RestoreView(lat,lon,scale);SaveMapViewState();ShowBriefMapPopup("7: Paths | FROM yellow | TO white");
        }
    }
}
