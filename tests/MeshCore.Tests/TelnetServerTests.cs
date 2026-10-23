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
internal static class TelnetServerTests
{
    internal static async Task RunAsync()
    {
        var emojiFormatter = new EmojiNameFormatter(new System.Collections.Generic.Dictionary<string, string> {
            { "😀", "[:grinning face:]" }, { "👩", "[:woman:]" }, { "👩‍💻", "[:woman technologist:]" }, { "🇩🇪", "[:flag Germany:]" }
        });
        Check(emojiFormatter.ToNames("😀 👩‍💻 🇩🇪 [:unknown:]") == "[:grinning face:] [:woman technologist:] [:flag Germany:] [:unknown:]", "Full emoji sequences use their names");
        Check(emojiFormatter.ToEmojis("[:grinning face:] [:woman technologist:] [:flag Germany:] [:unknown:]") == "😀 👩‍💻 🇩🇪 [:unknown:]", "Emoji names convert back and unknown names remain intact");
        Check(!new TelnetServerSettings().ReplaceEmojisWithNames, "Emoji conversion is opt-in");
        var smallArt = ReadArt("emoji-blocks-12line-full.xml", "😀");
        var largeArt = ReadArt("emoji-blocks-full.xml", "😀");
        Check(smallArt.Split('\n').Length == 12 && smallArt.Split('\n').All(row => row.Length == 24), "Small existing emoji art is 24 x 12");
        Check(largeArt.Split('\n').Length == 24 && largeArt.Split('\n').All(row => row.Length == 48), "Large existing emoji art is 48 x 24");
        var smallArtFormatter = new EmojiNameFormatter(new System.Collections.Generic.Dictionary<string, string> { { "😀", "\n" + smallArt + "\n" } });
        var largeArtFormatter = new EmojiNameFormatter(new System.Collections.Generic.Dictionary<string, string> { { "😀", "\n" + largeArt + "\n" } });
        Func<string, TelnetEmojiArtSize, string> renderArt = (text, size) => (size == TelnetEmojiArtSize.Large ? largeArtFormatter : smallArtFormatter).ToNames(text);
        Check(!new TelnetServerSettings().SendEmojiArt && new TelnetServerSettings().EmojiArtSize == TelnetEmojiArtSize.Small, "ASCII emoji output is opt-in with small default size");
        var formatting = new TelnetServerSettings();
        var time = new DateTime(2026, 10, 13, 12, 34, 56, DateTimeKind.Utc);
        Check(formatting.ShowTimestamps && formatting.ShowSender && !formatting.ReadOnly, "Existing display and sending defaults preserved");
        Check(TelnetMessageFormatter.Format(time, "Alice", "hello: [text]", formatting).EndsWith("] Alice: hello: [text]"), "Default timestamp and sender format");
        formatting.ShowTimestamps = false;
        Check(TelnetMessageFormatter.Format(time, "Alice", "hello: [text]", formatting) == "Alice: hello: [text]", "Timestamp can be hidden");
        formatting.ShowSender = false;
        Check(TelnetMessageFormatter.Format(time, "Alice", "hello: [text]", formatting) == "hello: [text]", "Both prefixes can be hidden without changing message text");
        formatting.ShowTimestamps = true;
        Check(TelnetMessageFormatter.Format(time, "Alice", "hello", formatting).EndsWith("] hello"), "Sender can be hidden independently");
        var portReservation = new TcpListener(IPAddress.Loopback, 0); portReservation.Start();
        int port = ((IPEndPoint)portReservation.LocalEndpoint).Port; portReservation.Stop();
        var settings = new TelnetServerSettings { Enabled = true, Protocol = MeshProtocol.MeshCore, Name = "Test", Port = port, ChannelIndex = 39, HistoryCount = 2 };
        var received = new ConcurrentQueue<string>();
        TelnetServerManager manager = null;
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        using (manager = new TelnetServerManager(MeshProtocol.MeshCore, new[] { settings, new TelnetServerSettings { Protocol = MeshProtocol.Meshtastic, Enabled = true, Port = port } },
            (channel, text, token) =>
            {
                Check(channel == 39, "Telnet input stays on selected MeshCore channel > 8");
                if (text == "reject") throw new InvalidOperationException("offline test");
                received.Enqueue(text); manager.Publish(channel, "You: " + text, true);
                return Task.CompletedTask;
            },
            channel => "Test channel",
            (channel, count, showOwn) => new[] { "old1", "old2", "old3" }))
        {
            manager.Start();
            Check(manager.GetStatuses().Count == 1 && manager.GetStatuses()[0].State == "Listening", "Protocol isolation and listening status");
            using (var client = new TcpClient())
            using (var second = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port); await second.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024, true))
                using (var reader2 = new StreamReader(second.GetStream(), new UTF8Encoding(false), false, 1024, true))
                {
                    foreach (var r in new[] { reader, reader2 })
                    {
                        Check(await ReadLine(r, deadline.Token) == "Welcome to Test channel", "Welcome includes channel name");
                        Check(await ReadLine(r, deadline.Token) == "old2" && await ReadLine(r, deadline.Token) == "old3", "Last messages delivered oldest first");
                    }
                    await stream.WriteAsync(new byte[] { 255 }, 0, 1, deadline.Token);
                    await stream.WriteAsync(new byte[] { 251, 1 }, 0, 2, deadline.Token);
                    var response = new byte[3]; var offset = 0;
                    while (offset < 3) offset += await BlePlatform.AwaitAsync(stream.ReadAsync(response, offset, 3 - offset, deadline.Token), deadline.Token);
                    Check(response.SequenceEqual(new byte[] { 255, 254, 1 }) && received.Count == 0, "Telnet option negotiation does not reach mesh");
                    var text = Encoding.UTF8.GetBytes("Grüße\r\0");
                    await stream.WriteAsync(text, 0, 3, deadline.Token); await stream.WriteAsync(text, 3, text.Length - 3, deadline.Token);
                    Check(await ReadLine(reader, deadline.Token) == "You: Grüße" && await ReadLine(reader2, deadline.Token) == "You: Grüße", "UTF-8 input forwarded and broadcast to both clients");
                    Check(received.Single() == "Grüße", "CR NUL creates only one outgoing message");
                    manager.Publish(0, "wrong channel"); manager.Publish(39, "live message");
                    Check(await ReadLine(reader, deadline.Token) == "live message" && await ReadLine(reader2, deadline.Token) == "live message", "Live channel forwarding and channel isolation");
                    var reject = Encoding.UTF8.GetBytes("reject\r\n"); await stream.WriteAsync(reject, 0, reject.Length, deadline.Token);
                    Check((await ReadLine(reader, deadline.Token)).Contains("ERROR: offline test"), "Mesh send failures reported to client");
                    Check(manager.GetStatuses()[0].Clients.Length == 2 && manager.GetStatuses()[0].LastError == "offline test", "Debug status includes connected clients and errors");
                    using (var third = new TcpClient())
                    {
                        await third.ConnectAsync(IPAddress.Loopback, port);
                        using (var r = new StreamReader(third.GetStream(), Encoding.UTF8, false, 1024, true))
                        {
                            await ReadLine(r, deadline.Token);
                            Check(await ReadLine(r, deadline.Token) == "You: Grüße" && await ReadLine(r, deadline.Token) == "live message", "Reconnect history includes new messages and stays bounded");
                        }
                    }
                    settings.ShowOwnMessages = false;
                    manager.Publish(39, "hidden own message", true);
                    manager.Publish(39, "visible incoming message");
                    Check(await ReadLine(reader, deadline.Token) == "visible incoming message" && await ReadLine(reader2, deadline.Token) == "visible incoming message", "Own messages can be hidden while incoming messages reach all clients");
                    using (var fourth = new TcpClient())
                    {
                        await fourth.ConnectAsync(IPAddress.Loopback, port);
                        using (var r = new StreamReader(fourth.GetStream(), Encoding.UTF8, false, 1024, true))
                        {
                            await ReadLine(r, deadline.Token);
                            Check(await ReadLine(r, deadline.Token) == "live message" && await ReadLine(r, deadline.Token) == "visible incoming message", "Hidden outgoing messages do not enter reconnect history");
                        }
                    }
                    settings.ShowTimestamps = false;
                    settings.ShowSender = false;
                    manager.Publish(39, routeSettings => TelnetMessageFormatter.Format(time, "Alice", "body: [unchanged]", routeSettings));
                    Check(await ReadLine(reader, deadline.Token) == "body: [unchanged]" && await ReadLine(reader2, deadline.Token) == "body: [unchanged]", "Live output uses the server display settings");
                    using (var conflict = new TelnetServerManager(MeshProtocol.MeshCore, new[] { settings }, (c,t,k) => Task.CompletedTask, c => "Conflict", (c,n,showOwn) => new string[0]))
                    {
                        conflict.Start(); Check(conflict.GetStatuses().Single().State == "Error", "Port conflict is visible and does not crash other servers");
                    }
                }
            }
            await manager.StopAsync();
            Check(manager.GetStatuses().Single().State == "Stopped", "Listeners stop cleanly");
            using (var reuse = new TcpListenerLease(port)) { }
            TelnetServerManager quiet = null;
            var quietSettings = new TelnetServerSettings { Enabled = true, Protocol = MeshProtocol.Meshtastic, Port = port, ChannelIndex = 7, SendWelcome = false, HistoryCount = 0, ReplaceEmojisWithNames = true, SendBell = true, RequirePassword = true, PasswordHash = TelnetPassword.Hash("test-password") };
            using (quiet = new TelnetServerManager(MeshProtocol.Meshtastic,
                new[] { quietSettings },
                (channel, text, token) => { Check(channel == 7, "Meshtastic channel route"); Check(text == "quiet test 😀", "Telnet emoji names become actual emoji before mesh send"); quiet.Publish(channel, text, true); return Task.CompletedTask; },
                channel => "Meshtastic test", (channel, count, showOwn) => new string[0], emojiFormatter.ToNames, emojiFormatter.ToEmojis, renderArt))
            {
                quiet.Start();
                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8, false, 1024, true))
                    {
                        Check((await ReadBytes(client.GetStream(), 3, deadline.Token)).SequenceEqual(new byte[] { 255, 251, 1 }), "Disable local password echo using Telnet ECHO negotiation");
                        Check(await ReadLine(reader, deadline.Token) == "Password:", "Password required before channel data");
                        quiet.Publish(7, "must not be visible before login");
                        var wrong = Encoding.UTF8.GetBytes("wrong\r\n");
                        await client.GetStream().WriteAsync(wrong, 0, wrong.Length, deadline.Token);
                        Check((await ReadLine(reader, deadline.Token)).StartsWith("Incorrect password."), "Incorrect password cannot read channel messages");
                        var login = Encoding.UTF8.GetBytes("test-password\r\n");
                        await client.GetStream().WriteAsync(login, 0, login.Length, deadline.Token);
                        Check((await ReadBytes(client.GetStream(), 3, deadline.Token)).SequenceEqual(new byte[] { 255, 252, 1 }), "Restore normal local echo after login");
                        var input = Encoding.UTF8.GetBytes("discard this\x1b\r\nquiet test [:grinning face:]\r\n");
                        await client.GetStream().WriteAsync(input, 0, input.Length, deadline.Token);
                        Check(await ReadLine(reader, deadline.Token) == "", "Escape moves cursor to a new line");
                        Check(await ReadLine(reader, deadline.Token) == "quiet test [:grinning face:]", "Own message has no bell and Escape discards unsent text");
                        quietSettings.ReadOnly = true;
                        var blocked = Encoding.UTF8.GetBytes("must not reach mesh\r\n");
                        await client.GetStream().WriteAsync(blocked, 0, blocked.Length, deadline.Token);
                        Check((await ReadLine(reader, deadline.Token)).Contains("read-only"), "Read-only input is rejected with a clear message");
                        quiet.Publish(7, "Incoming message 😀");
                        Check(await ReadLine(reader, deadline.Token) == "Incoming message [:grinning face:]\a", "Incoming message still emits bell");
                        quietSettings.SendEmojiArt = true;
                        foreach (var size in new[] { TelnetEmojiArtSize.Small, TelnetEmojiArtSize.Large })
                        {
                            quietSettings.EmojiArtSize = size;
                            quiet.Publish(7, "before 😀 after");
                            Check(await ReadLine(reader, deadline.Token) == "before ", "Text before ASCII emoji remains unchanged");
                            foreach (var row in (size == TelnetEmojiArtSize.Large ? largeArt : smallArt).Split('\n'))
                                Check(await ReadLine(reader, deadline.Token) == row, "Selected ASCII block rows and whitespace survive Telnet transport");
                            Check(await ReadLine(reader, deadline.Token) == " after\a", "Text after ASCII emoji and one incoming bell survive");
                        }
                        Check(quiet.GetStatuses().Single().ForwardedToMesh == 1, "Password and discarded input never forwarded to mesh");
                    }
                }
            }
        }
        using (var historyDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        using (var historyServer = new TelnetServerManager(MeshProtocol.MeshCore,
            new[] { new TelnetServerSettings { Enabled = true, Protocol = MeshProtocol.MeshCore, Port = port, ChannelIndex = 39, HistoryCount = 1, SendWelcome = false, SendEmojiArt = true, EmojiArtSize = TelnetEmojiArtSize.Large, ReplaceEmojisWithNames = true } },
            (c, t, k) => Task.CompletedTask, c => "Art history", (c, n, config) => new[] { "saved 😀 done" },
            emojiFormatter.ToNames, emojiFormatter.ToEmojis, renderArt))
        {
            historyServer.Start();
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8, false, 1024, true))
                {
                    Check(await ReadLine(reader, historyDeadline.Token) == "saved ", "Initial history uses ASCII output instead of names");
                    foreach (var row in largeArt.Split('\n'))
                        Check(await ReadLine(reader, historyDeadline.Token) == row, "Initial history uses selected large ASCII size");
                    Check(await ReadLine(reader, historyDeadline.Token) == " done", "History retains following text without bell");
                }
            }
        }
        var file = Path.Combine(Path.GetTempPath(), "telnet-settings-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            settings.SendEmojiArt = true; settings.EmojiArtSize = TelnetEmojiArtSize.Large; settings.ShowTimestamps = false; settings.ShowSender = false; settings.ReadOnly = true; settings.ReplaceEmojisWithNames = true; settings.ShowOwnMessages = false; settings.SendBell = true; settings.RequirePassword = true; settings.PasswordHash = TelnetPassword.Hash("saved-password");
            var app = new MeshtasticApplicationSettings(); app.TelnetServers.Add(settings);
            MeshtasticSettingsStore.Save(file, app); var loaded = MeshtasticSettingsStore.Load(file).TelnetServers.Single();
            Check(loaded.SendEmojiArt && loaded.EmojiArtSize == TelnetEmojiArtSize.Large && !loaded.ShowTimestamps && !loaded.ShowSender && loaded.ReadOnly && loaded.ReplaceEmojisWithNames && !loaded.ShowOwnMessages && loaded.SendBell && loaded.RequirePassword && TelnetPassword.Verify("saved-password", loaded.PasswordHash) && !TelnetPassword.Verify("wrong", loaded.PasswordHash) && !loaded.PasswordHash.Contains("saved-password"), "Password hash and bell configuration roundtrip");
            Check(loaded.Port == port && loaded.ChannelIndex == 39 && loaded.BindAddress == "127.0.0.1" && loaded.HistoryCount == 2 && loaded.Protocol == MeshProtocol.MeshCore, "Telnet settings roundtrip");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
        var db = Path.Combine(Path.GetTempPath(), "telnet-history-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var store = MeshtasticMessageStore.CreateSqlite(db))
            {
                store.Initialize(); int events = 0; store.MessageStored += message => events++;
                for (int i = 0; i < 3; i++) { store.AddOutgoing((uint)i + 1, 42, UInt32.MaxValue, "history" + i, 39); await Task.Delay(20); }
                Check(events == 3 && store.GetRecentChannelMessages(39, 2).Select(m => m.Text).SequenceEqual(new[] { "history1", "history2" }), "Stored-message events and limited channel history");
                Check(store.GetRecentChannelMessages(39, 2, false).Count == 0, "Outgoing messages excluded from initial history");
                Check(store.GetRecentChannelMessages(39, 0).Count == 0 && store.GetRecentChannelMessages(0, 2).Count == 0, "History off and channel filtering");
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(db)) File.Delete(db); }
        Console.WriteLine("Telnet server tests passed: welcome/history, multiple clients, UTF-8, negotiation, channel isolation, errors, configuration and shutdown.");
    }
    private static string ReadArt(string file, string emoji)
    {
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file));
        return document.Root.Elements("glyph").Single(g => (string)g.Attribute("character") == emoji).Element("text").Value.Replace("\r\n", "\n").Trim('\r', '\n');
    }
    private static async Task<byte[]> ReadBytes(NetworkStream stream, int count, CancellationToken token)
    {
        var bytes = new byte[count]; int offset = 0;
        while (offset < count)
        {
            int read = await BlePlatform.AwaitAsync(stream.ReadAsync(bytes, offset, count - offset, token), token);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return bytes;
    }
    private static async Task<string> ReadLine(StreamReader reader, CancellationToken token)
    { return await BlePlatform.AwaitAsync(reader.ReadLineAsync(), token); }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
    private sealed class TcpListenerLease : IDisposable
    {
        private readonly TcpListener _listener;
        internal TcpListenerLease(int port) { _listener = new TcpListener(IPAddress.Loopback, port); _listener.Start(); }
        public void Dispose() { _listener.Stop(); }
    }
}