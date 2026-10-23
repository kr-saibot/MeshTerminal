using System.IO;
namespace MeshTerminal.Client
{
    public sealed class MeshCoreCommandException : IOException
    {
        public byte Command { get; private set; }
        public byte? ErrorCode { get; private set; }
        public MeshCoreCommandException(byte command, byte? errorCode)
            : base("MeshCore rejected command 0x" + command.ToString("X2") + " (error " + (errorCode.HasValue ? errorCode.Value.ToString() : "disabled") + ").")
        { Command = command; ErrorCode = errorCode; }
    }
}