using System;
using Google.Protobuf;
using Meshtastic.Protobufs;
namespace MeshTerminal.Client
{
    public sealed partial class MeshtasticMessageStore
    {
        private void InitializeTraceroutes()
        {
            Execute(_sqlite?"CREATE TABLE IF NOT EXISTS MeshtasticTraceroutes (Origin INTEGER NOT NULL, Destination INTEGER NOT NULL, ReceivedUtc TEXT NOT NULL, Payload TEXT NOT NULL, PRIMARY KEY(Origin,Destination))":"IF OBJECT_ID('MeshtasticTraceroutes','U') IS NULL CREATE TABLE MeshtasticTraceroutes (Origin BIGINT NOT NULL,Destination BIGINT NOT NULL,ReceivedUtc DATETIME2 NOT NULL,Payload NVARCHAR(MAX) NOT NULL,PRIMARY KEY(Origin,Destination))",null);
        }
        public void SaveTraceroute(MeshtasticTracerouteResult result)
        {
            var sql=_sqlite?"INSERT INTO MeshtasticTraceroutes VALUES (@origin,@target,@time,@payload) ON CONFLICT(Origin,Destination) DO UPDATE SET ReceivedUtc=excluded.ReceivedUtc,Payload=excluded.Payload WHERE excluded.ReceivedUtc>=MeshtasticTraceroutes.ReceivedUtc":"IF NOT EXISTS (SELECT 1 FROM MeshtasticTraceroutes WHERE Origin=@origin AND Destination=@target) INSERT INTO MeshtasticTraceroutes VALUES (@origin,@target,@time,@payload) ELSE UPDATE MeshtasticTraceroutes SET ReceivedUtc=@time,Payload=@payload WHERE Origin=@origin AND Destination=@target AND ReceivedUtc<=@time";
            Execute(sql,c=> { Add(c,"@origin",(long)result.Origin);Add(c,"@target",(long)result.Destination);Add(c,"@time",TimeValue(result.ReceivedUtc));Add(c,"@payload",Convert.ToBase64String(result.Route.ToByteArray())); });
        }
        public MeshtasticTracerouteResult GetTraceroute(uint origin,uint destination)
        {
            lock(_sync) using(var connection=Open()) using(var c=connection.CreateCommand())
            {
                c.CommandText="SELECT * FROM MeshtasticTraceroutes WHERE Origin=@origin AND Destination=@target";
                Add(c,"@origin",(long)origin);Add(c,"@target",(long)destination);
                using(var r=c.ExecuteReader()) { if(!r.Read()) return null; return new MeshtasticTracerouteResult { Origin=origin,Destination=destination,ReceivedUtc=ReadTime(r["ReceivedUtc"]),Route=RouteDiscovery.Parser.ParseFrom(Convert.FromBase64String(Convert.ToString(r["Payload"]))) }; }
            }
        }
    }
}
