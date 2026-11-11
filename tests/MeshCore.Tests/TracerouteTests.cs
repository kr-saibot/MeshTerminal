using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using MeshTerminal.Client;
using Meshtastic.Protobufs;
internal static class TracerouteTests
{
    static void Check(bool value,string message) { if(!value) throw new Exception("Traceroute: "+message); }
    public static async Task RunAsync()
    {
        MeshtasticTracerouteResult saved=null;
        foreach(var mode in new[]{"success","nak","timeout","cancel","disconnect"})
        using(var radio=new Radio(mode))
        using(var client=new MeshtasticClient { MaximumConnectionAttempts=1,TracerouteTimeout=TimeSpan.FromMilliseconds(350) })
        using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await client.ConnectTcpAsync("127.0.0.1",radio.Port,deadline.Token);
            await client.RequestFullStateAsync(deadline.Token);
            int received=0;var receipt=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);client.TracerouteReceived+=(sender,result)=> { received++;receipt.TrySetResult(true); };
            using(var requestStop=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
            {
                if(mode=="cancel") requestStop.CancelAfter(80);
                try {
                    var result=await client.RequestTracerouteAsync(77,2,4,requestStop.Token);
                    Check(mode=="success","A failed or incomplete traceroute must not succeed");
                    Check(result.Origin==42&&result.Destination==77&&result.Route.Route.SequenceEqual(new uint[]{12,UInt32.MaxValue}),"Correlated response, endpoint identity and anonymous relay preserved");
                    await Task.WhenAny(receipt.Task,Task.Delay(1000));
                    Check(received==1,"Unrelated frames and ACK-only responses cannot complete a traceroute");
                    var paths=result.ToPaths(new StoredMeshNode[0]);
                    Check(paths.OutgoingSteps.Count==4&&paths.IncomingAdvertSteps.Count==3,"Separate forward and return routes");
                    Check(paths.OutgoingSteps[1].SnrDb==2&&paths.OutgoingSteps[2].SnrDb==null&&paths.IncomingAdvertSteps[1].SnrDb==-1,"Scaled SNR including unknown sentinel");
                    saved=result;
                    try { await client.RequestTracerouteAsync(77,2,4,deadline.Token); throw new Exception("Rate limit missing"); } catch(InvalidOperationException) { }
                }
                catch(TimeoutException) { Check(mode=="timeout","Timeout reported accurately"); }
                catch(OperationCanceledException) { Check(mode=="cancel","Cancellation reported accurately"); }
                catch(IOException ex) { Check(mode=="nak"?ex.Message.Contains("NoRoute"):mode=="disconnect","Routing rejection or disconnect reported accurately"); }
            }
            await client.DisconnectAsync();
            if(radio.Error!=null) throw radio.Error;
        }
        var file=Path.Combine(Path.GetTempPath(),"traceroute-"+Guid.NewGuid().ToString("N")+".db");
        try {
            using(var store=MeshtasticMessageStore.CreateSqlite(file)) { store.Initialize();store.SaveTraceroute(saved); }
            using(var store=MeshtasticMessageStore.CreateSqlite(file)) {
                store.Initialize();var result=store.GetTraceroute(42,77);
                Check(result.ReceivedUtc==saved.ReceivedUtc&&result.Route.Equals(saved.Route),"Timestamp, both directions and SNR survive reopening");
                Check(store.GetTraceroute(43,77)==null,"Results are isolated by local radio");
                store.SaveTraceroute(new MeshtasticTracerouteResult { Origin=42,Destination=77,ReceivedUtc=saved.ReceivedUtc.AddMinutes(-1),Route=new RouteDiscovery() });
                Check(store.GetTraceroute(42,77).Route.Route.Count==2,"Delayed older response cannot replace newer result");
            }
            var old=new MeshtasticTracerouteResult { Route=new RouteDiscovery() };
            Check(old.ToPaths(new StoredMeshNode[0]).IncomingAdvertSteps==null,"Missing old-firmware return data is unknown rather than zero-hop");
            old.Route.SnrTowards.Add(0);
            Check(old.ToPaths(new StoredMeshNode[0]).OutgoingSteps.Count==2,"Confirmed zero-hop route remains available");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Traceroute tests passed: real TCP request framing, correlation, ACK vs route, two directions/SNR, anonymous hops, rate limit, NAK, timeout, cancellation, disconnect and persistence.");
    }
    sealed class Radio:IDisposable
    {
        readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);readonly string mode;TcpClient connection;bool stopped;
        internal Exception Error;internal int Port;
        internal Radio(string mode) { this.mode=mode;listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;_ = Run(); }
        async Task Run()
        {
            try {
                connection=await listener.AcceptTcpClientAsync();var stream=connection.GetStream();
                while(!stopped) {
                    var header=await Read(stream,4);var request=ToRadio.Parser.ParseFrom(await Read(stream,header[2]*256+header[3]));
                    if(request.WantConfigId!=0) {
                        await Send(stream,new FromRadio { MyInfo=new MyNodeInfo { MyNodeNum=42 } });
                        await Send(stream,new FromRadio { NodeInfo=new NodeInfo { Num=42,User=new User { Id="!0000002a",LongName="Self" } } });
                        await Send(stream,new FromRadio { ConfigCompleteId=request.WantConfigId });continue;
                    }
                    if(request.Packet?.Decoded?.Portnum!=PortNum.TracerouteApp) continue;
                    var packet=request.Packet;
                    Check(packet.To==77&&packet.Channel==2&&packet.HopLimit==4&&packet.Decoded.WantResponse&&packet.Id!=0,"Official traceroute packet fields");
                    Check(RouteDiscovery.Parser.ParseFrom(packet.Decoded.Payload).Route.Count==0,"Request begins with an empty route");
                    if(mode=="disconnect") { connection.Close();return; }
                    if(mode=="nak") { await Reply(stream,packet.Id,42,PortNum.RoutingApp,new Routing { ErrorReason=Routing.Types.Error.NoRoute });continue; }
                    if(mode!="success") continue;
                    var route=new RouteDiscovery();route.Route.Add(new uint[]{12,UInt32.MaxValue});route.RouteBack.Add(13);route.SnrTowards.Add(new[]{8,-128,0});route.SnrBack.Add(new[]{-4,12});
                    await Reply(stream,packet.Id+1,77,PortNum.TracerouteApp,route);
                    await Reply(stream,packet.Id,78,PortNum.TracerouteApp,route);
                    await Reply(stream,packet.Id,42,PortNum.RoutingApp,new Routing { ErrorReason=Routing.Types.Error.None });
                    await Task.Delay(40);await Reply(stream,packet.Id,77,PortNum.TracerouteApp,route);
                }
            }
            catch(Exception ex) { if(!stopped && !(ex is EndOfStreamException) && !(ex is IOException)) Error=ex; }
        }
        static Task Reply(NetworkStream stream,uint request,uint from,PortNum port,IMessage payload) { return Send(stream,new FromRadio { Packet=new MeshPacket { From=from,To=42,Decoded=new Data { RequestId=request,Portnum=port,Payload=payload.ToByteString() } } }); }
        static async Task Send(NetworkStream stream,FromRadio frame) { var payload=frame.ToByteArray();var bytes=new byte[payload.Length+4];bytes[0]=0x94;bytes[1]=0xC3;bytes[2]=(byte)(payload.Length>>8);bytes[3]=(byte)payload.Length;Buffer.BlockCopy(payload,0,bytes,4,payload.Length);await stream.WriteAsync(bytes,0,bytes.Length); }
        static async Task<byte[]> Read(NetworkStream stream,int length) { var bytes=new byte[length];int offset=0;while(offset<length) { int count=await stream.ReadAsync(bytes,offset,length-offset);if(count==0) throw new EndOfStreamException();offset+=count; } return bytes; }
        public void Dispose() { stopped=true;connection?.Dispose();listener.Stop(); }
    }
}
