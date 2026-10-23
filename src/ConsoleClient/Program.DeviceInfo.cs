using Dialog = ConsoleClient.SafeDialog;
using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Client;
using Terminal.Gui;
namespace ConsoleClient
{
    internal static partial class Program
    {
        private static string OwnDeviceDetails()
        {
            var d = _mesh.Device;
            var text = new StringBuilder();
            text.AppendLine("Protocol: " + _mesh.Protocol);
            text.AppendLine("Name: " + (d.NodeName ?? "Not available"));
            text.AppendLine("ID: " + (d.NodeId ?? "Not available"));
            text.AppendLine("Hardware: " + (d.HardwareName ?? (d.Metadata == null ? "Not available" : d.Metadata.HwModel.ToString())));
            text.AppendLine("Firmware: " + (d.Metadata == null ? "Not available" : d.Metadata.FirmwareVersion));
            if (!String.IsNullOrWhiteSpace(d.FirmwareBuildDate)) text.AppendLine("Firmware build: " + d.FirmwareBuildDate);
            text.AppendLine();
            text.AppendLine("Connection: " + _mesh.State);
            text.AppendLine("Transport: " + (_mesh.ConnectionInfo.Transport ?? "Not available"));
            text.AppendLine("Target: " + ConnectionTarget());
            var connection = _mesh.ConnectionInfo;
            if (connection.ConnectedSinceUtc.HasValue) text.AppendLine("Connected since: " + connection.ConnectedSinceUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine();
            var voltage = d.MeshCoreBatteryVoltage;
            var metrics = d.LocalNode == null ? null : d.LocalNode.Value.DeviceMetrics;
            if (_mesh.Protocol != MeshProtocol.MeshCore && metrics != null && metrics.HasVoltage) voltage = metrics.Voltage;
            var battery = d.BatteryLevel.HasValue ? (d.BatteryLevel.Value > 100 ? "External power" : d.BatteryLevel.Value + " %") : "Not available";
            if (voltage.HasValue) battery = (_mesh.Protocol == MeshProtocol.MeshCore ? "" : battery + ", ") + voltage.Value.ToString("0.000", CultureInfo.InvariantCulture) + " V";
            text.AppendLine("Battery: " + battery);
            if (_mesh.Protocol == MeshProtocol.MeshCore)
            {
                text.AppendLine("Device time (last read): " + (_mesh.IsConnected && d.DeviceClockUtc.HasValue ? d.DeviceClockUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") : "Not available"));
                text.AppendLine("Clock synchronization: " + (_mesh.IsConnected ? d.DeviceClockSyncStatus ?? "Not synchronized" : "Disconnected"));
            }
            text.AppendLine(); text.AppendLine("Radio settings");
            if (_mesh.Protocol == MeshProtocol.MeshCore)
            {
                text.AppendLine("Routing hash length: " + (d.RoutingHashLengthBytes.HasValue ? d.RoutingHashLengthBytes.Value + " byte" + (d.RoutingHashLengthBytes.Value == 1 ? "" : "s") : "Not available"));
                text.AppendLine("Frequency: " + (d.RadioFrequencyMHz.HasValue ? d.RadioFrequencyMHz.Value.ToString("0.###", CultureInfo.InvariantCulture) + " MHz" : "Not available"));
                text.AppendLine("Bandwidth: " + (d.RadioBandwidthKHz.HasValue ? d.RadioBandwidthKHz.Value.ToString("0.###", CultureInfo.InvariantCulture) + " kHz" : "Not available"));
                text.AppendLine("Spreading factor: " + (d.RadioSpreadingFactor?.ToString() ?? "Not available"));
                text.AppendLine("Coding rate: " + (d.RadioCodingRate.HasValue ? "4/" + d.RadioCodingRate.Value : "Not available"));
                text.AppendLine("Transmit power: " + (d.RadioTxPowerDbm.HasValue ? d.RadioTxPowerDbm.Value + " dBm" : "Not available"));
            }
            else if (d.LoRaConfig != null)
            {
                var r = d.LoRaConfig;
                text.AppendLine("Region: " + r.Region);
                text.AppendLine("Preset: " + (r.UsePreset ? r.ModemPreset.ToString() : "Custom"));
                if (!r.UsePreset) { text.AppendLine("Bandwidth: " + (r.Bandwidth == 31 ? "31.25" : r.Bandwidth == 62 ? "62.5" : r.Bandwidth.ToString()) + " kHz"); text.AppendLine("Spreading factor: " + r.SpreadFactor); text.AppendLine("Coding rate: 4/" + r.CodingRate); }
                text.AppendLine("Frequency: " + (r.OverrideFrequency > 0 ? r.OverrideFrequency.ToString("0.###", CultureInfo.InvariantCulture) + " MHz" : "Automatic (region/preset)"));
                text.AppendLine("Frequency slot: " + r.ChannelNum);
                text.AppendLine("Transmit power: " + (r.TxPower == 0 ? "Automatic" : r.TxPower + " dBm"));
                text.AppendLine("Transmit enabled: " + r.TxEnabled); text.AppendLine("Hop limit: " + r.HopLimit);
            }
            else text.AppendLine("Not available; connect and load the device configuration.");
            return text.ToString();
        }

        private static void ShowOwnDeviceInfo()
        {
            var dialog = new Dialog("Device info", 94, 29);
            var details = new TextView { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3), ReadOnly = true, WordWrap = true, Text = OwnDeviceDetails() };
            var status = new Label("") { X = 1, Y = Pos.AnchorEnd(3), Width = Dim.Fill(2) };
            var refresh = new Button("Refresh"); var closed = false;
            var cancellation = new CancellationTokenSource();
            Action update = async delegate
            {
                if (!_mesh.IsConnected) { status.Text = "Connect to the device first."; return; }
                refresh.Enabled = false; status.Text = "Reading device information...";
                string error = null;
                try { await Task.Run(() => _mesh.RefreshOwnDeviceInfoAsync(cancellation.Token)); }
                catch (Exception ex) { error = ex.Message; }
                Application.MainLoop.Invoke(() => { if (closed) return; details.Text = OwnDeviceDetails(); status.Text = error ?? "Device information updated."; refresh.Enabled = true; });
            };
            refresh.Clicked += update;
            var copy = new Button("Copy data"); copy.Clicked += () => CopyTextWithFallback(details.Text.ToString(), "Device information copied.");
            var close = new Button("Close", true); close.Clicked += () => Application.RequestStop();
            dialog.Add(details, status); dialog.AddButton(refresh); dialog.AddButton(copy); dialog.AddButton(close);
            if (_mesh.IsConnected && _mesh.Protocol == MeshProtocol.MeshCore) dialog.Ready += update;
            try { Application.Run(dialog, HandleTerminalGuiException); }
            finally { closed = true; cancellation.Cancel(); }
        }
    }
}
