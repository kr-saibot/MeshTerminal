using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MeshTerminal.Client;

internal static class TelnetEchoTests
{
    public static async Task RunAsync()
    {
        Check(!new TelnetServerSettings().EchoInput, "Echo is disabled by default");
        foreach (var protocol in new[] { MeshProtocol.MeshCore, MeshProtocol.Meshtastic }) await TestAsync(protocol);
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
        try
        {
            var settings = new MeshtasticApplicationSettings();
            settings.TelnetServers.Add(new TelnetServerSettings { EchoInput = true });
            MeshtasticSettingsStore.Save(file, settings);
            Check(MeshtasticSettingsStore.Load(file).TelnetServers.Single().EchoInput, "Echo option survives restart");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        Console.WriteLine("Telnet echo tests passed: immediate UTF-8 echo, editing, CRLF, escape, client isolation, negotiation, password protection and persistence for both protocols.");
    }

    private static async Task TestAsync(MeshProtocol protocol)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        var settings = new TelnetServerSettings { Protocol = protocol, Enabled = true, Port = port, EchoInput = true, SendWelcome = false, HistoryCount = 0,
            RequirePassword = protocol == MeshProtocol.MeshCore, PasswordHash = TelnetPassword.Hash("secret") };
        var sent = new ConcurrentQueue<string>();
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        using (var manager = new TelnetServerManager(protocol, new[] { settings }, (channel, text, token) => { sent.Enqueue(text); return Task.CompletedTask; }, channel => "Test", (channel, count, own) => new string[0]))
        using (var client = new TcpClient())
        using (var second = new TcpClient())
        {
            manager.Start();
            await client.ConnectAsync(IPAddress.Loopback, port); await Authenticate(client.GetStream(), settings.RequirePassword, deadline.Token);
            await second.ConnectAsync(IPAddress.Loopback, port); await Authenticate(second.GetStream(), settings.RequirePassword, deadline.Token);
            var stream = client.GetStream();
            await Write(stream, new byte[] { 255, 253, 1 }, deadline.Token); // accept server ECHO
            var typed = Encoding.UTF8.GetBytes("hä🙂");
            await Write(stream, typed.Take(3).ToArray(), deadline.Token);
            Check((await Read(stream, 3, deadline.Token)).SequenceEqual(typed.Take(3)), "Input echoed before Enter");
            await Write(stream, typed.Skip(3).ToArray(), deadline.Token);
            Check((await Read(stream, typed.Length - 3, deadline.Token)).SequenceEqual(typed.Skip(3)), "Split UTF-8 remains intact");
            manager.Publish(0, "probe");
            Check(Encoding.UTF8.GetString(await Read(stream, 7, deadline.Token)) == "probe\r\n", "Regular output remains intact");
            Check(Encoding.UTF8.GetString(await Read(second.GetStream(), 7, deadline.Token)) == "probe\r\n", "Input echo is private to its own connection");
            await Write(stream, new byte[] { 27 }, deadline.Token);
            Check((await Read(stream, 2, deadline.Token)).SequenceEqual(new byte[] { 13, 10 }), "Escape clears unfinished input and advances line");
            await Write(stream, Encoding.UTF8.GetBytes("abé\b" + "c\r\n"), deadline.Token);
            var expected = Encoding.UTF8.GetBytes("abé\b \bc\r\n");
            Check((await Read(stream, expected.Length, deadline.Token)).SequenceEqual(expected), "Backspace erases one UTF-8 code point and CRLF echoed once");
            while (!sent.Contains("abc")) await Task.Delay(10, deadline.Token);
            Check(sent.Count == 1, "Only edited completed input reaches mesh");
            var burst = Encoding.UTF8.GetBytes(new string('x', 1800));
            await Write(stream, burst, deadline.Token);
            Check((await Read(stream, burst.Length, deadline.Token)).SequenceEqual(burst), "Pasted input does not overflow per-client output queue");
            await Write(stream, new byte[] { 27 }, deadline.Token); await Read(stream, 2, deadline.Token);
            await Write(stream, new byte[] { 255, 254, 1 }, deadline.Token); // client refuses echo
            Check((await Read(stream, 3, deadline.Token)).SequenceEqual(new byte[] { 255, 252, 1 }), "Client can decline negotiated echo");
            await Write(stream, Encoding.UTF8.GetBytes("silent\r\n"), deadline.Token);
            while (!sent.Contains("silent")) await Task.Delay(10, deadline.Token);
            manager.Publish(0, "done");
            Check(Encoding.UTF8.GetString(await Read(stream, 6, deadline.Token)) == "done\r\n", "Declined echo produces no duplicated local input");
            await Write(stream, new byte[] { 255, 253, 1 }, deadline.Token);
            Check((await Read(stream, 3, deadline.Token)).SequenceEqual(new byte[] { 255, 251, 1 }), "Client can renegotiate echo after declining it");
            await Write(stream, Encoding.UTF8.GetBytes("again"), deadline.Token);
            Check(Encoding.UTF8.GetString(await Read(stream, 5, deadline.Token)) == "again", "Renegotiated echo becomes active immediately");
            Check(!sent.Contains("secret") && sent.Count == 2, "Passwords, negotiation and escape never reach mesh");
        }
    }
    private static async Task Authenticate(NetworkStream stream, bool password, CancellationToken token)
    {
        Check((await Read(stream, 3, token)).SequenceEqual(new byte[] { 255, 251, 1 }), "Server advertises Telnet echo");
        if (!password) return;
        Check(Encoding.UTF8.GetString(await Read(stream, 11, token)) == "Password:\r\n", "Password prompt");
        await Write(stream, Encoding.UTF8.GetBytes("secret\r\n!"), token);
        Check((await Read(stream, 1, token))[0] == (byte)'!', "Password is not echoed; subsequent authenticated input is echoed");
        await Write(stream, new byte[] { 27 }, token); await Read(stream, 2, token);
    }
    private static async Task Write(NetworkStream stream, byte[] bytes, CancellationToken token) { await stream.WriteAsync(bytes, 0, bytes.Length, token); }
    private static async Task<byte[]> Read(NetworkStream stream, int count, CancellationToken token)
    {
        var bytes = new byte[count]; int offset = 0;
        while (offset < count)
        {
            int read = await BlePlatform.AwaitAsync(stream.ReadAsync(bytes, offset, count - offset, token), token);
            if (read == 0) throw new EndOfStreamException(); offset += read;
        }
        return bytes;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
