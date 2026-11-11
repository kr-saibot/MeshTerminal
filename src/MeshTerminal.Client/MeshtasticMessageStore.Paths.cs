using System;
using System.Data.Common;
namespace MeshTerminal.Client
{
    public sealed partial class MeshtasticMessageStore
    {
        public void SaveMeshCorePath(MeshCorePathSnapshot path)
        {
            if (path == null || String.IsNullOrWhiteSpace(path.DeviceIdentity) || String.IsNullOrWhiteSpace(path.TargetIdentity)) return;
            var count = MeshtasticClient.DecodeMeshCoreHops(path.EncodedPath);
            if (!count.HasValue || path.Path == null || path.Path.Length != count.Value * ((path.EncodedPath >> 6) + 1)) return;
            var sql = _sqlite
                ? "INSERT INTO MeshCorePaths VALUES (@device,@target,@incoming,@encoded,@path,@observed,@advert) ON CONFLICT(DeviceIdentity,TargetIdentity,Incoming) DO UPDATE SET EncodedPath=excluded.EncodedPath,Path=excluded.Path,ObservedUtc=excluded.ObservedUtc,AdvertReceivedUtc=excluded.AdvertReceivedUtc WHERE excluded.ObservedUtc >= MeshCorePaths.ObservedUtc"
                : "IF NOT EXISTS (SELECT 1 FROM MeshCorePaths WHERE DeviceIdentity=@device AND TargetIdentity=@target AND Incoming=@incoming) INSERT INTO MeshCorePaths VALUES (@device,@target,@incoming,@encoded,@path,@observed,@advert) ELSE UPDATE MeshCorePaths SET EncodedPath=@encoded,Path=@path,ObservedUtc=@observed,AdvertReceivedUtc=@advert WHERE DeviceIdentity=@device AND TargetIdentity=@target AND Incoming=@incoming AND ObservedUtc<=@observed";
            Execute(sql, c => {
                Add(c,"@device",path.DeviceIdentity); Add(c,"@target",path.TargetIdentity); Add(c,"@incoming",path.Incoming?1:0);
                Add(c,"@encoded",(int)path.EncodedPath); Add(c,"@path",BitConverter.ToString(path.Path).Replace("-",""));
                Add(c,"@observed",TimeValue(path.ObservedUtc)); Add(c,"@advert",path.AdvertReceivedUtc.HasValue?TimeValue(path.AdvertReceivedUtc.Value):null);
            });
        }
        public MeshCorePathSnapshot GetMeshCorePath(string device, string target, bool incoming)
        {
            if (String.IsNullOrWhiteSpace(device) || String.IsNullOrWhiteSpace(target)) return null;
            lock (_sync) using(var connection=Open()) using(var c=connection.CreateCommand())
            {
                c.CommandText="SELECT * FROM MeshCorePaths WHERE DeviceIdentity=@device AND TargetIdentity=@target AND Incoming=@incoming";
                Add(c,"@device",device); Add(c,"@target",target); Add(c,"@incoming",incoming?1:0);
                using(var r=c.ExecuteReader())
                {
                    if(!r.Read()) return null;
                    var hex=Convert.ToString(r["Path"]); var bytes=new byte[hex.Length/2];
                    for(int i=0;i<bytes.Length;i++) bytes[i]=Convert.ToByte(hex.Substring(i*2,2),16);
                    return new MeshCorePathSnapshot { DeviceIdentity=device,TargetIdentity=target,Incoming=incoming,EncodedPath=Convert.ToByte(r["EncodedPath"]),Path=bytes,ObservedUtc=ReadTime(r["ObservedUtc"]),AdvertReceivedUtc=r["AdvertReceivedUtc"]==DBNull.Value?(DateTime?)null:ReadTime(r["AdvertReceivedUtc"]) };
                }
            }
        }
    }
}
