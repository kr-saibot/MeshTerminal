using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeshTerminal;
using MeshTerminal.Host;
internal static class HostQrTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception("Host QR: " + message); }
    private static Bitmap Code(string text)
    {
        using (var data = ChannelQrCode.Create(text))
        using (var memory = new MemoryStream(ChannelQrCode.Png(data)))
        using (var image = new Bitmap(memory)) return new Bitmap(image);
    }
    internal static async Task RunAsync()
    {
        const string first = "meshcore://channel/add?name=Test&secret=00112233445566778899aabbccddeeff";
        const string second = "Hello from QR – Grüße";
        using (var a = Code(first))
        using (var b = Code(second))
        using (var screen = new Bitmap(a.Width + b.Width + 180, Math.Max(a.Height, b.Height) + 120))
        {
            using (var graphics = Graphics.FromImage(screen))
            { graphics.Clear(Color.White); graphics.DrawImageUnscaled(a, 30, 30); graphics.DrawImageUnscaled(b, a.Width + 120, 30); }
            var multiple = ScreenQrReader.Decode(screen);
            Check(multiple.Contains(first) && multiple.Contains(second), "Multiple screenshot codes and UTF-8 payloads");
            using (var crop = screen.Clone(new Rectangle(20, 20, a.Width + 20, a.Height + 20), PixelFormat.Format32bppArgb))
            {
                crop.RotateFlip(RotateFlipType.Rotate90FlipNone);
                Check(ScreenQrReader.Decode(crop).Single() == first, "Cropped and rotated QR code");
                for (int y = 0; y < crop.Height; y++) for (int x = 0; x < crop.Width; x++)
                { var color = crop.GetPixel(x, y); crop.SetPixel(x, y, Color.FromArgb(255 - color.R, 255 - color.G, 255 - color.B)); }
                Check(ScreenQrReader.Decode(crop).Single() == first, "Inverted QR code");
            }
            using (var blank = new Bitmap(200, 200))
            { using (var graphics = Graphics.FromImage(blank)) graphics.Clear(Color.White); Check(ScreenQrReader.Decode(blank).Length == 0, "No QR results for blank captures"); }
        }
        Check(!HostQrBridge.ValidName("arbitrary") && HostQrBridge.ValidName("meshterminal-qr-" + Guid.NewGuid().ToString("N")), "Private per-host channel names");
        foreach (var bad in new[] { "", "\u001b[31m", "bad\0text", new string('a', HostQrBridge.MaximumBytes + 1) })
        {
            bool rejected = false; try { HostQrBridge.Encode(bad); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Empty, escape/control and oversized QR payloads are rejected");
        }
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var name = "meshterminal-qr-" + Guid.NewGuid().ToString("N");
            var other = "meshterminal-qr-" + Guid.NewGuid().ToString("N");
            var firstResult = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            int received = 0, otherReceived = 0;
            using (var server = new HostQrBridge(name, text => { Interlocked.Increment(ref received); firstResult.TrySetResult(text); }))
            using (var otherServer = new HostQrBridge(other, text => Interlocked.Increment(ref otherReceived)))
            {
                await HostQrBridge.SendAsync(name, second);
                Check(await firstResult.Task == second && received == 1 && otherReceived == 0, "Named pipe delivers Unicode to the correct host instance");
                await HostQrBridge.SendAsync(name, first);
                Check(received == 2 && otherReceived == 0, "The QR channel accepts subsequent scans");
            }
        }
        Console.WriteLine("Host QR tests passed: screenshot decoding, multiple/rotated/inverted codes, empty capture, Unicode, input bounds and per-instance IPC.");
    }
}
