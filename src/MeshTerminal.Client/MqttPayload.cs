using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
namespace MeshTerminal.Client
{
    public sealed class MqttPublication
    {
        public string Topic { get; private set; }
        public string Payload { get; private set; }
        public MqttPublication(string topic, string payload) { Topic = topic; Payload = payload; }
    }
    public static class MqttPayload
    {
        public static bool Matches(MqttClientSettings settings, StoredMeshMessage message, string senderId, bool favorite)
        {
            if (message == null || message.Direction != MessageDirection.Incoming || !settings.IsChatBinding || (settings.FavoritesOnly && !favorite)) return false;
            if (settings.Binding == MqttBinding.Channel) return message.Kind == StoredMessageKind.Channel && message.ChannelIndex == settings.ChannelIndex;
            return message.Kind == StoredMessageKind.Direct && (settings.Binding == MqttBinding.AllDirect || String.Equals(settings.NodeId, senderId, StringComparison.OrdinalIgnoreCase));
        }
        private static Dictionary<string, object> Node(uint? number, string id, string name)
        {
            var result = new Dictionary<string, object>();
            if (number.HasValue) result["number"] = number.Value;
            if (!String.IsNullOrEmpty(id)) result["id"] = id;
            if (!String.IsNullOrEmpty(name)) result["name"] = name;
            return result;
        }
        public static Dictionary<string, object> Message(MeshProtocol protocol, StoredMeshMessage m, string senderId, string senderName, string recipientId)
        {
            var data = new Dictionary<string, object> {
                {"event_id", m.Id.ToString("D")}, {"protocol", protocol.ToString()}, {"kind", m.Kind.ToString()}, {"direction", "incoming"},
                {"timestamp", m.OccurredUtc.ToUniversalTime().ToString("o")}, {"received_at", m.CreatedUtc.ToUniversalTime().ToString("o")},
                {"message", m.Text ?? ""}, {"sender", Node(m.FromNode, senderId, senderName)}, {"recipient", Node(m.ToNode, recipientId, null)}
            };
            if (m.PacketId.HasValue) data["packet_id"] = m.PacketId.Value;
            if (m.ChannelIndex.HasValue) data["channel"] = new Dictionary<string, object> { {"index",m.ChannelIndex.Value}, {"name",m.ChannelName ?? ""} };
            var radio = new Dictionary<string, object>();
            if (m.MeshCoreHops.HasValue) radio["hops"] = m.MeshCoreHops.Value;
            if (m.ReceivedRssi.HasValue) radio["rssi_dbm"] = m.ReceivedRssi.Value;
            if (m.ReceivedSnr.HasValue) radio["snr_db"] = m.ReceivedSnr.Value;
            if (radio.Count > 0) data["radio"] = radio;
            if (m.Latitude.HasValue) data["latitude"] = m.Latitude.Value;
            if (m.Longitude.HasValue) data["longitude"] = m.Longitude.Value;
            if (!String.IsNullOrEmpty(m.RawMetadata)) data["raw_metadata"] = m.RawMetadata;
            return data;
        }
        public static Dictionary<string, object> Telemetry(MeshProtocol protocol, StoredTelemetry t, string nodeId, string name)
        {
            var data = new Dictionary<string, object> { {"event_id",t.Id.ToString("D")}, {"protocol",protocol.ToString()}, {"kind","Telemetry"},
                {"received_at",t.ReceivedAtUtc.ToUniversalTime().ToString("o")}, {"sender",Node(t.NodeNumber,nodeId,name)}, {"type",t.Type ?? "Telemetry"} };
            if (t.TelemetryTimeUtc.HasValue) data["timestamp"] = t.TelemetryTimeUtc.Value.ToUniversalTime().ToString("o");
            var values = new Dictionary<string, object> {
                {"battery_percent",t.BatteryLevel}, {"voltage",t.Voltage}, {"channel_utilization",t.ChannelUtilization}, {"air_util_tx",t.AirUtilTx},
                {"uptime_seconds",t.UptimeSeconds}, {"temperature_c",t.Temperature}, {"relative_humidity",t.RelativeHumidity}, {"barometric_pressure_hpa",t.BarometricPressure},
                {"latitude",t.Latitude}, {"longitude",t.Longitude}, {"altitude_m",t.Altitude}, {"noise_floor_dbm",t.NoiseFloorDbm}, {"rssi_dbm",t.LastRssiDbm}, {"snr_db",t.LastSnrDb}
            };
            data["telemetry"] = values.Where(p => p.Value != null).ToDictionary(p => p.Key,p => p.Value);
            data["message"] = t.RawTelemetry ?? "";
            return data;
        }
        public static Dictionary<string, object> Announcement(NodeAnnouncementEventArgs announcement)
        {
            var n=announcement.Node;var name=n.User?.LongName??n.User?.ShortName;
            var node=Node(n.Num,n.User?.Id,name);
            if(!String.IsNullOrEmpty(n.User?.ShortName))node["short_name"]=n.User.ShortName;
            var type=announcement.Protocol==MeshProtocol.MeshCore?
                (announcement.ContactType==1?"Companion":announcement.ContactType==2?"Repeater":announcement.ContactType==3?"Room server":"Unknown"):
                n.User==null?"Unknown":n.User.Role.ToString();
            node["type"]=type;
            if(announcement.ContactType.HasValue)node["type_code"]=announcement.ContactType.Value;
            if(announcement.Protocol==MeshProtocol.Meshtastic&&n.User!=null&&(int)n.User.HwModel!=0)node["hardware"]=n.User.HwModel.ToString();
            if(n.Position!=null)
            {
                if(n.Position.HasLatitudeI)node["latitude"]=n.Position.LatitudeI/10000000d;
                if(n.Position.HasLongitudeI)node["longitude"]=n.Position.LongitudeI/10000000d;
                if(n.Position.HasAltitude)node["altitude_m"]=n.Position.Altitude;
            }
            if(n.DeviceMetrics!=null&&n.DeviceMetrics.HasBatteryLevel)node["battery_percent"]=n.DeviceMetrics.BatteryLevel;
            var radio=new Dictionary<string,object>();
            if(n.HasHopsAway)radio["hops"]=n.HopsAway;
            if(announcement.Rssi.HasValue)radio["rssi_dbm"]=announcement.Rssi.Value;
            if(announcement.Snr.HasValue)radio["snr_db"]=announcement.Snr.Value;
            var data=new Dictionary<string,object> { {"event_id",announcement.Id.ToString("D")}, {"protocol",announcement.Protocol.ToString()},
                {"kind",announcement.Protocol==MeshProtocol.MeshCore?"Advert":"NodeInfo"}, {"is_new",announcement.IsNew}, {"received_at",announcement.ReceivedUtc.ToUniversalTime().ToString("o")},
                {"node",node}, {"message",(announcement.IsNew?"New node: ":"Node announcement: ")+(name??n.User?.Id??n.Num.ToString())}, {"raw_metadata",n.ToString()} };
            if(radio.Count>0)data["radio"]=radio;
            return data;
        }
        public static IList<MqttPublication> Publications(string baseTopic, Dictionary<string, object> data)
        {
            var result = new List<MqttPublication>(); Flatten(baseTopic.TrimEnd('/'),data,result);
            result.Add(new MqttPublication(baseTopic.TrimEnd('/') + "/json",new JavaScriptSerializer().Serialize(data))); return result;
        }
        private static void Flatten(string topic, Dictionary<string, object> data, List<MqttPublication> result)
        {
            foreach (var pair in data)
            {
                var nested = pair.Value as Dictionary<string, object>;
                if (nested != null) Flatten(topic + "/" + pair.Key,nested,result);
                else if (pair.Value != null) result.Add(new MqttPublication(topic + "/" + pair.Key, pair.Value is bool?((bool)pair.Value?"true":"false"):Convert.ToString(pair.Value,CultureInfo.InvariantCulture)));
            }
        }
    }
}
