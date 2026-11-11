using System;
using System.Collections.Generic;
using System.Linq;
namespace MeshTerminal.Client
{
    public static class MqttSystemStatus
    {
        private static Dictionary<string,object> Available(Dictionary<string,object> values) { return values.Where(p=>p.Value!=null).ToDictionary(p=>p.Key,p=>p.Value); }
        public static Dictionary<string,object> Create(MeshProtocol protocol,DeviceInfo device,ConnectionState state,ConnectionInfo connection,string target,string deviceInfoText)
        {
            bool connected=state==ConnectionState.Connected;
            var metrics=device.LocalNode?.Value.DeviceMetrics;
            var voltage=protocol==MeshProtocol.MeshCore?device.MeshCoreBatteryVoltage:metrics!=null&&metrics.HasVoltage?(double?)metrics.Voltage:null;
            var level=device.BatteryLevel;
            var data=new Dictionary<string,object> { {"event_id",Guid.NewGuid().ToString("D")}, {"protocol",protocol.ToString()}, {"kind","SystemStatus"}, {"timestamp",DateTime.UtcNow.ToString("o")}, {"connected",connected}, {"message",deviceInfoText??""} };
            data["device"]=Available(new Dictionary<string,object> { {"name",device.NodeName}, {"id",device.NodeId}, {"number",device.MyNode==null?null:(object)device.MyNode.MyNodeNum},
                {"hardware",device.HardwareName??device.Metadata?.HwModel.ToString()}, {"firmware",device.Metadata?.FirmwareVersion}, {"firmware_build",device.FirmwareBuildDate}, {"meshcore_protocol",device.MeshCoreProtocolVersion},
                {"latitude",device.Latitude}, {"longitude",device.Longitude} });
            data["connection"]=Available(new Dictionary<string,object> { {"state",state.ToString()}, {"transport",connection.Transport}, {"endpoint",connection.Endpoint}, {"target",target},
                {"connected_since",connection.ConnectedSinceUtc?.ToUniversalTime().ToString("o")}, {"current_duration_seconds",(long)connection.CurrentConnectionDuration.TotalSeconds},
                {"total_duration_seconds",(long)connection.TotalConnectedDuration.TotalSeconds}, {"reconnects",connection.ReconnectCount}, {"rx_frames",connection.ReceivedFrameCount}, {"tx_frames",connection.SentFrameCount},
                {"last_rx",connection.LastReceivedUtc?.ToUniversalTime().ToString("o")}, {"last_tx",connection.LastSentUtc?.ToUniversalTime().ToString("o")} });
            data["battery"]=Available(new Dictionary<string,object> { {"percent",level.HasValue&&level<=100?level:null}, {"voltage",voltage},
                {"external_power",level.HasValue?(object)(level>100):null}, {"state",level.HasValue?(level>100?"ExternalPower":"Battery"):voltage.HasValue?"VoltageOnly":"Unknown"} });
            var radio=Available(new Dictionary<string,object> { {"noise_floor_dbm",device.NoiseFloorDbm}, {"rx_packets",device.RadioReceivedPackets}, {"tx_packets",device.RadioSentPackets},
                {"rx_errors",device.RadioReceiveErrors}, {"statistics_received_at",device.RadioStatisticsReadUtc?.ToUniversalTime().ToString("o")} });
            if(connected && device.TxUtilization!=null)
            {
                var tx=device.TxUtilization;
                foreach(var value in Available(new Dictionary<string,object> { {"tx_utilization_percent",tx.Percent},{"tx_utilization_window_seconds",tx.WindowSeconds},{"tx_utilization_estimated",tx.Estimated},{"tx_utilization_received_at",tx.ReceivedUtc.ToUniversalTime().ToString("o")} })) radio[value.Key]=value.Value;
            }
            if(protocol==MeshProtocol.MeshCore)
            {
                foreach(var p in Available(new Dictionary<string,object> { {"routing_hash_bytes",device.RoutingHashLengthBytes}, {"frequency_mhz",device.RadioFrequencyMHz}, {"bandwidth_khz",device.RadioBandwidthKHz},
                    {"spreading_factor",device.RadioSpreadingFactor}, {"coding_rate",device.RadioCodingRate.HasValue?"4/"+device.RadioCodingRate.Value:null}, {"tx_power_dbm",device.RadioTxPowerDbm} }))radio[p.Key]=p.Value;
                data["clock"]=Available(new Dictionary<string,object> { {"device_time_last_read",connected?device.DeviceClockUtc?.ToUniversalTime().ToString("o"):null},
                    {"read_at",connected?device.DeviceClockReadAtUtc?.ToUniversalTime().ToString("o"):null}, {"synchronization",connected?device.DeviceClockSyncStatus:"Disconnected"} });
            }
            else if(device.LoRaConfig!=null)
            {
                var r=device.LoRaConfig;
                radio["region"]=r.Region.ToString();radio["preset"]=r.UsePreset?r.ModemPreset.ToString():"Custom";
                radio["frequency_mode"]=r.OverrideFrequency>0?"Override":"Automatic";
                if(r.OverrideFrequency>0)radio["frequency_mhz"]=r.OverrideFrequency;
                if(!r.UsePreset) { radio["bandwidth_khz"]=r.Bandwidth==31?31.25:r.Bandwidth==62?62.5:r.Bandwidth;radio["spreading_factor"]=r.SpreadFactor;radio["coding_rate"]="4/"+r.CodingRate; }
                radio["frequency_slot"]=r.ChannelNum;radio["tx_power_mode"]=r.TxPower==0?"Automatic":"Configured";
                if(r.TxPower!=0)radio["tx_power_dbm"]=r.TxPower;
                radio["tx_enabled"]=r.TxEnabled;radio["hop_limit"]=r.HopLimit;
            }
            data["radio"]=radio;return data;
        }
    }
}
