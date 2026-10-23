using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MeshTerminal.Client;

namespace MeshTerminal
{
    internal sealed class CommunicationLogEntry
    {
        internal readonly CommunicationFrameEventArgs Frame;
        private string _decoded, _hex;
        internal CommunicationLogEntry(CommunicationFrameEventArgs frame) { Frame = frame; }
        internal string Decoded { get { return _decoded ?? (_decoded = Frame.Decode()); } }
        internal string Hex { get { return _hex ?? (_hex = BitConverter.ToString(Frame.RawData).Replace('-', ' ')); } }
    }

    internal sealed class CommunicationLogBuffer
    {
        private readonly object _gate = new object();
        private readonly Queue<CommunicationLogEntry> _entries = new Queue<CommunicationLogEntry>();
        private int _capacity = 500;
        private bool _paused, _loRaOnly;
        private long _version;
        internal long Version { get { lock (_gate) return _version; } }
        internal int Capacity { get { lock (_gate) return _capacity; } }
        internal bool Paused { get { lock (_gate) return _paused; } set { lock (_gate) { _paused = value; _version++; } } }
        internal bool LoRaOnly
        {
            get { lock (_gate) return _loRaOnly; }
            set
            {
                lock (_gate)
                {
                    _loRaOnly = value;
                    if (value)
                    {
                        var keep = _entries.Where(e => e.Frame.IsLoRaRelated()).ToArray();
                        _entries.Clear(); foreach (var entry in keep) _entries.Enqueue(entry);
                    }
                    _version++;
                }
            }
        }
        internal void SetCapacity(int capacity)
        {
            if (capacity < 1 || capacity > 10000) throw new ArgumentOutOfRangeException("capacity", "Enter 1-10000 frames.");
            lock (_gate) { _capacity = capacity; while (_entries.Count > _capacity) _entries.Dequeue(); _version++; }
        }
        internal void Add(CommunicationFrameEventArgs frame)
        {
            lock (_gate)
            {
                if (_paused || _loRaOnly && !frame.IsLoRaRelated()) return;
                _entries.Enqueue(new CommunicationLogEntry(frame));
                while (_entries.Count > _capacity) _entries.Dequeue();
                _version++;
            }
        }
        internal CommunicationLogEntry[] Snapshot() { lock (_gate) return _entries.ToArray(); }
        internal void Clear() { lock (_gate) { _entries.Clear(); _version++; } }
        internal string CopyText()
        {
            var text = new StringBuilder();
            foreach (var entry in Snapshot())
            {
                var f = entry.Frame;
                text.AppendLine(f.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + " " + f.Direction + " " + f.Protocol + " / " + f.Transport);
                text.AppendLine("RAW: " + entry.Hex); text.AppendLine("DECODED: " + entry.Decoded); text.AppendLine();
            }
            return text.ToString();
        }
    }
}
