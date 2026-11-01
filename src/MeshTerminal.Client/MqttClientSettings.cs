using System;
using System.Text;
namespace MeshTerminal.Client
{
    public enum MqttBinding { Channel, DirectNode, AllDirect, Telemetry, AdvertsAndNodes, SystemStatus }
    public sealed class MqttClientSettings
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "MQTT";
        public bool Enabled { get; set; }
        public MeshProtocol Protocol { get; set; }
        public string Server { get; set; } = "localhost";
        public int Port { get; set; } = 1883;
        public bool UseTls { get; set; }
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string BaseTopic { get; set; } = "meshterminal";
        public MqttBinding Binding { get; set; }
        public int ChannelIndex { get; set; }
        // Persist the actual public key / Meshtastic ID, never a transient MeshCore number.
        public string NodeId { get; set; } = "";
        public bool FavoritesOnly { get; set; }
        public bool AllowSending { get; set; }
        public bool OnlyNewNodes { get; set; }
        public int StatusIntervalSeconds { get; set; } = 30;
        public bool IsChatBinding { get { return Binding==MqttBinding.Channel||Binding==MqttBinding.DirectNode||Binding==MqttBinding.AllDirect; } }
        public MqttClientSettings Copy() { return (MqttClientSettings)MemberwiseClone(); }
        public void Validate()
        {
            if (String.IsNullOrWhiteSpace(Server) || Server.Contains("://") || Server.IndexOfAny(new[] {'/', '\\', '\0', '\r', '\n'}) >= 0)
                throw new ArgumentException("Enter a broker hostname or IP address without a URL scheme.");
            if (Port < 1 || Port > 65535) throw new ArgumentException("Port must be 1-65535.");
            if (!Enum.IsDefined(typeof(MqttBinding), Binding)) throw new ArgumentException("Choose a binding.");
            if (!Enum.IsDefined(typeof(MeshProtocol), Protocol)) throw new ArgumentException("Choose a protocol.");
            if (String.IsNullOrWhiteSpace(BaseTopic) || BaseTopic.StartsWith("$") || BaseTopic.IndexOfAny(new[] {'+', '#', '\0', '\r', '\n'}) >= 0 || Encoding.UTF8.GetByteCount(BaseTopic) > 1024)
                throw new ArgumentException("Enter a base topic without wildcards (maximum 1024 UTF-8 bytes).");
            if (Binding == MqttBinding.DirectNode && String.IsNullOrWhiteSpace(NodeId)) throw new ArgumentException("Choose a direct-chat contact.");
            if (Binding == MqttBinding.Channel && (ChannelIndex < 0 || ChannelIndex >= (Protocol == MeshProtocol.MeshCore ? 256 : 8))) throw new ArgumentException("Invalid channel number.");
            if (!IsChatBinding && AllowSending) throw new ArgumentException("Telemetry, node announcement and system status bindings cannot send chat messages.");
            if(Binding==MqttBinding.SystemStatus&&(StatusIntervalSeconds<5||StatusIntervalSeconds>3600))throw new ArgumentException("Status interval must be 5-3600 seconds.");
            Server = Server.Trim(); BaseTopic = BaseTopic.TrimEnd('/');
            if (BaseTopic.Length == 0) throw new ArgumentException("Enter a base topic.");
        }
    }
}
