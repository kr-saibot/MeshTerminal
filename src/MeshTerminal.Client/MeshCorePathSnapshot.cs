using System;
namespace MeshTerminal.Client
{
    public sealed class MeshCorePathSnapshot : EventArgs
    {
        public string DeviceIdentity { get; set; }
        public string TargetIdentity { get; set; }
        public bool Incoming { get; set; }
        public byte EncodedPath { get; set; }
        public byte[] Path { get; set; }
        public DateTime ObservedUtc { get; set; }
        public DateTime? AdvertReceivedUtc { get; set; }
    }
}
