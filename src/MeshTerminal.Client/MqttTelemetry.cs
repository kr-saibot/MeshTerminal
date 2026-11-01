using System;
using Meshtastic.Protobufs;
namespace MeshTerminal.Client
{
    public static class MqttTelemetry
    {
        public static StoredTelemetry From(MeshTelemetry t)
        {
            var d=t.Telemetry.DeviceMetrics; var e=t.Telemetry.EnvironmentMetrics;
            return new StoredTelemetry { Id=Guid.NewGuid(), NodeNumber=t.From, ReceivedAtUtc=t.ReceivedAtUtc, TelemetryTimeUtc=t.TelemetryTimeUtc, Type=t.Type,
                BatteryLevel=d!=null&&d.HasBatteryLevel?(uint?)d.BatteryLevel:null,
                Voltage=d!=null&&d.HasVoltage?(double?)d.Voltage:e!=null&&e.HasVoltage?(double?)e.Voltage:null,
                ChannelUtilization=d!=null&&d.HasChannelUtilization?(double?)d.ChannelUtilization:null, AirUtilTx=d!=null&&d.HasAirUtilTx?(double?)d.AirUtilTx:null,
                UptimeSeconds=d!=null&&d.HasUptimeSeconds?(uint?)d.UptimeSeconds:null, Temperature=e!=null&&e.HasTemperature?(double?)e.Temperature:null,
                RelativeHumidity=e!=null&&e.HasRelativeHumidity?(double?)e.RelativeHumidity:null, BarometricPressure=e!=null&&e.HasBarometricPressure?(double?)e.BarometricPressure:null,
                RawTelemetry=t.MeshCoreRawTelemetry??t.Telemetry.ToString() };
        }
        public static StoredTelemetry From(MeshCoreRepeaterStatus s) { return new StoredTelemetry { Id=Guid.NewGuid(),NodeNumber=s.NodeNumber,ReceivedAtUtc=s.ReceivedUtc,Type="MeshCore status",Voltage=s.BatteryVoltage,UptimeSeconds=s.UptimeSeconds,NoiseFloorDbm=s.NoiseFloorDbm,LastRssiDbm=s.LastRssiDbm,LastSnrDb=s.LastSnrDb,RawTelemetry=s.ToString() }; }
        public static StoredTelemetry From(uint node, Position p, DateTime received)
        {
            var seconds=p.Timestamp!=0?p.Timestamp:p.Time;
            return new StoredTelemetry { Id=Guid.NewGuid(),NodeNumber=node,ReceivedAtUtc=received,Type="Position",TelemetryTimeUtc=seconds==0?(DateTime?)null:new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds),
                Latitude=p.HasLatitudeI?(double?)(p.LatitudeI/10000000d):null,Longitude=p.HasLongitudeI?(double?)(p.LongitudeI/10000000d):null,Altitude=p.HasAltitude?(int?)p.Altitude:null,RawTelemetry=p.ToString() };
        }
    }
}
