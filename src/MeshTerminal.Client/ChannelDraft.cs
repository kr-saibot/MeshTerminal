namespace MeshTerminal.Client
{
    // Kept only for reading drafts created by earlier versions.
    public sealed class ChannelDraft
    {
        public MeshProtocol Protocol { get; set; }
        public string DeviceId { get; set; }
        public int Slot { get; set; }
        public string Data { get; set; }
    }
}
