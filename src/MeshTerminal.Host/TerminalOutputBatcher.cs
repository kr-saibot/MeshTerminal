using System;
using System.Text;
using System.Threading;

namespace MeshTerminal.Host
{
    // ConPTY pipe reads are transport chunks, not complete screen updates.
    internal sealed class TerminalOutputBatcher : IDisposable
    {
        private readonly object _sync = new object();
        private readonly StringBuilder _pending = new StringBuilder();
        private readonly Action<string> _write;
        private readonly Action<Exception> _error;
        private readonly Timer _timer;
        private bool _disposed;
        private bool _reportedError;
        internal TerminalOutputBatcher(Action<string> write, Action<Exception> error = null)
        {
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _error = error;
            _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        }
        internal void Append(ReadOnlySpan<char> text)
        {
            if (text.Length == 0) return;
            lock (_sync)
            {
                if (_disposed) return; // The retired connection must never write into its replacement.
                bool first = _pending.Length == 0;
                _pending.Append(text.ToString());
                if (_pending.Length >= 65536) FlushLocked();
                else if (first) _timer.Change(8, Timeout.Infinite);
            }
        }
        internal void Flush()
        {
            lock (_sync) { if (!_disposed) FlushLocked(); }
        }
        private void FlushLocked()
        {
            if (_pending.Length == 0) return;
            try
            {
                _write(_pending.ToString());
                _pending.Clear();
            }
            catch (Exception ex)
            {
                // Keep the text for retry; never silently discard the startup screen.
                if (!_reportedError) { _reportedError = true; _error?.Invoke(ex); }
                if (!_disposed) _timer.Change(100, Timeout.Infinite);
            }
        }
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                FlushLocked();
                _disposed = true;
                _timer.Dispose();
            }
        }
    }
}
