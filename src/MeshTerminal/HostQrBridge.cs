using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeshTerminal
{
    internal sealed class HostQrBridge : IDisposable
    {
        internal const string EnvironmentKey = "MESHTERMINAL_QR_PIPE";
        internal const int MaximumBytes = 16384;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        internal static bool ValidName(string name)
        {
            if (name == null || !name.StartsWith("meshterminal-qr-", StringComparison.Ordinal) || name.Length != 48) return false;
            for (int i = 16; i < name.Length; i++) if (!Uri.IsHexDigit(name[i])) return false;
            return true;
        }
        internal static byte[] Encode(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new InvalidDataException("The QR code contains no text.");
            foreach (char c in text) if (Char.IsControl(c) && c != '\r' && c != '\n' && c != '\t') throw new InvalidDataException("The QR code contains unsupported control characters.");
            var bytes = new UTF8Encoding(false, true).GetBytes(text);
            if (bytes.Length > MaximumBytes) throw new InvalidDataException("QR text is too long.");
            return bytes;
        }
        internal HostQrBridge(string name, Action<string> received)
        {
            if (!ValidName(name)) throw new ArgumentException("Invalid QR connection name.");
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        var security = new PipeSecurity();
                        security.SetAccessRuleProtection(true, false);
                        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                        using (var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                        using (_stop.Token.Register(() => pipe.Dispose()))
                        {
                            await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
                            using (deadline.Token.Register(() => pipe.Dispose()))
                            {
                                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                                var size = await ReadAsync(pipe, 4, deadline.Token).ConfigureAwait(false);
                                int length = BitConverter.ToInt32(size, 0);
                                if (length < 1 || length > MaximumBytes) throw new InvalidDataException();
                                var payload = await ReadAsync(pipe, length, deadline.Token).ConfigureAwait(false);
                                var text = new UTF8Encoding(false, true).GetString(payload); Encode(text);
                                received(text);
                                await pipe.WriteAsync(new byte[] { 1 }, 0, 1, deadline.Token).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception) { if (!_stop.IsCancellationRequested) await Task.Delay(100).ConfigureAwait(false); }
                }
            });
        }
        private static async Task<byte[]> ReadAsync(Stream stream, int count, CancellationToken token)
        {
            var bytes = new byte[count]; int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(bytes, offset, count - offset, token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return bytes;
        }
        internal static async Task SendAsync(string name, string text)
        {
            var bytes = Encode(text);
            if (!ValidName(name)) throw new InvalidOperationException("QR connection is unavailable.");
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
            {
                await pipe.ConnectAsync(3000, timeout.Token).ConfigureAwait(false);
                var header = BitConverter.GetBytes(bytes.Length);
                await pipe.WriteAsync(header, 0, header.Length, timeout.Token).ConfigureAwait(false);
                await pipe.WriteAsync(bytes, 0, bytes.Length, timeout.Token).ConfigureAwait(false);
                var ack = await ReadAsync(pipe, 1, timeout.Token).ConfigureAwait(false);
                if (ack[0] != 1) throw new IOException("MeshTerminal did not accept the QR text.");
            }
        }
        public void Dispose() { _stop.Cancel(); }
    }
}
