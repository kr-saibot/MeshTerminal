using System;
using System.IO;
using System.Linq;
using MeshTerminal.Client;
internal static class StoredMeshCorePathTests
{
    static void Check(bool condition,string text) { if(!condition) throw new Exception("Stored paths: "+text); }
    internal static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"paths-"+Guid.NewGuid().ToString("N")+".db");
        var now=DateTime.UtcNow;
        try
        {
            using(var store=MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize();
                store.SaveMeshCorePath(new MeshCorePathSnapshot { DeviceIdentity="selfA",TargetIdentity="peer",Incoming=true,EncodedPath=66,Path=new byte[]{1,2,3,4},ObservedUtc=now,AdvertReceivedUtc=now.AddMinutes(-5) });
                store.SaveMeshCorePath(new MeshCorePathSnapshot { DeviceIdentity="selfA",TargetIdentity="peer",EncodedPath=0,Path=new byte[0],ObservedUtc=now });
                Check(store.GetMeshCorePath("selfA","peer",true).Path.SequenceEqual(new byte[]{1,2,3,4}),"Multi-byte repeater hashes survive storage");
                Check(store.GetMeshCorePath("selfA","peer",false).EncodedPath==0,"Zero-hop TO path is stored independently");
                Check(store.GetMeshCorePath("selfB","peer",true)==null,"Paths are isolated by own-device identity");
                store.SaveMeshCorePath(new MeshCorePathSnapshot { DeviceIdentity="selfA",TargetIdentity="peer",Incoming=true,EncodedPath=1,Path=new byte[]{9},ObservedUtc=now.AddMinutes(-1) });
                Check(store.GetMeshCorePath("selfA","peer",true).EncodedPath==66,"Delayed older writes cannot replace a newer path");
                store.SaveMeshCorePath(new MeshCorePathSnapshot { DeviceIdentity="selfA",TargetIdentity="peer",Incoming=true,EncodedPath=255,Path=new byte[0],ObservedUtc=now.AddMinutes(1) });
                Check(store.GetMeshCorePath("selfA","peer",true).EncodedPath==66,"Missing paths do not erase the last known valid route");
            }
            using(var store=MeshtasticMessageStore.CreateSqlite(file))
            {
                store.Initialize();
                var saved=store.GetMeshCorePath("selfA","peer",true);
                Check(saved.ObservedUtc==now && saved.AdvertReceivedUtc==now.AddMinutes(-5),"Both clocks survive database reopen without substituting current time");
                using(var client=new MeshtasticClient())
                {
                    var restored=client.RestoreMeshCoreNodePaths(123,store.GetMeshCorePath("selfA","peer",false),saved);
                    Check(restored.OutgoingCached && restored.IncomingCached && restored.OutgoingSteps.Count==2 && restored.IncomingAdvertSteps.Count==4,"Stored raw routes can be decoded for both the dialog and map without a connection");
                }
            }
            Console.WriteLine("Stored MeshCore path tests passed: reopen, TO/FROM separation, device isolation, multi-byte hashes, zero hops, timestamps and stale/invalid write protection.");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if(File.Exists(file)) File.Delete(file); }
    }
}
