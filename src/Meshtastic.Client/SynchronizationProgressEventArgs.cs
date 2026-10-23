using System;
using System.Threading;
using System.Threading.Tasks;
namespace Meshtastic.Client
{
    public sealed class SynchronizationTimeoutInfo
    {
        public int Attempt { get; internal set; }
        public int ReceivedNodes { get; internal set; }
        public int ExpectedNodes { get; internal set; }
        public string LastNodeName { get; internal set; }
        public TimeSpan Timeout { get; internal set; }
    }
    public sealed class SynchronizationProgressEventArgs : EventArgs
    {
        public SynchronizationProgressEventArgs(string text) { Text = text; }
        public string Text { get; private set; }
    }
}
