using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Meshtastic.Client
{
    public sealed class TelnetServerStatus
    {
        public string Name, ChannelName, Endpoint, State, LastError;
        public int ChannelIndex;
        public long ForwardedToMesh;
        public string[] Clients;
    }
    public sealed class TelnetServerManager : IDisposable
    {
        private sealed class Route
        {
            internal TelnetServerSettings Settings;
            internal TelnetServerStatus Status;
            internal TcpListener Listener;
            internal readonly List<Peer> Peers = new List<Peer>();
            internal readonly Queue<string> Recent = new Queue<string>();
            internal Task AcceptTask;
        }
        private sealed class Peer
        {
            internal TcpClient Client;
            internal string Endpoint;
            internal bool Authenticated;
            internal bool EchoRefused;
            private readonly Queue<byte[]> _output = new Queue<byte[]>();
            private readonly SemaphoreSlim _ready = new SemaphoreSlim(0);
            private readonly int _capacity;
            private bool _closed;
            internal Peer(TcpClient client, int capacity) { Client = client; Endpoint = client.Client.RemoteEndPoint.ToString(); _capacity = capacity; }
            internal void Line(string value) { Enqueue(Encoding.UTF8.GetBytes((value ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n") + "\r\n")); }
            internal void Enqueue(byte[] bytes)
            {
                lock (_output)
                {
                    if (_closed) return;
                    if (_output.Count >= _capacity) { Close(); return; }
                    _output.Enqueue(bytes); _ready.Release();
                }
            }
            internal void Close() { lock (_output) { if (_closed) return; _closed = true; Client.Close(); _ready.Release(); } }
            internal async Task WriteAsync(CancellationToken token)
            {
                try
                {
                    var stream = Client.GetStream();
                    while (true)
                    {
                        await _ready.WaitAsync(token).ConfigureAwait(false);
                        byte[] bytes;
                        lock (_output) { if (_closed) return; if (_output.Count == 0) continue; bytes = _output.Dequeue(); }
                        var write = stream.WriteAsync(bytes, 0, bytes.Length, token);
                        if (await Task.WhenAny(write, Task.Delay(5000, token)).ConfigureAwait(false) != write) { Close(); try { await write.ConfigureAwait(false); } catch { } return; }
                        await write.ConfigureAwait(false);
                    }
                }
                catch { Close(); }
            }
        }
        private readonly object _gate = new object();
        private readonly List<Route> _routes = new List<Route>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Func<int, string, CancellationToken, Task> _send;
        private readonly Func<int, string> _channelName;
        private readonly Func<int, int, TelnetServerSettings, IList<string>> _history;
        private readonly Func<string, string> _emojiNames;
        private readonly Func<string, string> _emojiValues;
        private readonly Func<string, TelnetEmojiArtSize, string> _emojiArt;
        private readonly MeshProtocol _protocol;
        private bool _started;
        public TelnetServerManager(MeshProtocol protocol, IEnumerable<TelnetServerSettings> settings,
            Func<int, string, CancellationToken, Task> send, Func<int, string> channelName, Func<int, int, TelnetServerSettings, IList<string>> history, Func<string, string> emojiNames = null, Func<string, string> emojiValues = null, Func<string, TelnetEmojiArtSize, string> emojiArt = null)
        {
            _protocol = protocol; _send = send; _channelName = channelName; _history = history; _emojiNames = emojiNames ?? (text => text); _emojiValues = emojiValues ?? (text => text); _emojiArt = emojiArt ?? ((text, size) => text);
            foreach (var setting in (settings ?? Enumerable.Empty<TelnetServerSettings>()).Where(s => s != null && s.Protocol == protocol))
                _routes.Add(new Route { Settings = setting, Status = new TelnetServerStatus { Name = setting.Name, ChannelIndex = setting.ChannelIndex, State = "Disabled", Clients = new string[0] } });
        }
        public void Start()
        {
            lock (_gate) { if (_started) return; _started = true; }
            foreach (var route in _routes)
            {
                if (!route.Settings.Enabled) continue;
                try
                {
                    var settings = route.Settings;
                    if (settings.Port < 1 || settings.Port > 65535 || settings.ChannelIndex < 0 || settings.ChannelIndex >= (_protocol == MeshProtocol.MeshCore ? 256 : 8) ||
                        settings.HistoryCount < 0 || settings.HistoryCount > 1000) throw new ArgumentException("Invalid port, channel or history count.");
                    var address = IPAddress.Parse(settings.BindAddress ?? "127.0.0.1");
                    route.Status.ChannelName = _channelName(settings.ChannelIndex);
                    var history = _history(settings.ChannelIndex, settings.HistoryCount, settings);
                    foreach (var line in history.Skip(Math.Max(0, history.Count - settings.HistoryCount))) route.Recent.Enqueue(FormatOutput(line, settings));
                    route.Listener = new TcpListener(address, settings.Port);
                    route.Listener.Start();
                    route.Status.Endpoint = route.Listener.LocalEndpoint.ToString();
                    route.Status.State = "Listening";
                    route.AcceptTask = AcceptAsync(route);
                }
                catch (Exception error) { route.Status.State = "Error"; route.Status.LastError = error.Message; }
            }
        }
        private string FormatOutput(string text, TelnetServerSettings settings)
        {
            if (settings.SendEmojiArt) text = _emojiArt(text, settings.EmojiArtSize);
            return settings.ReplaceEmojisWithNames ? _emojiNames(text) : text;
        }
        public IList<TelnetServerStatus> GetStatuses()
        {
            lock (_gate) return _routes.Select(r => new TelnetServerStatus { Name = r.Status.Name, ChannelIndex = r.Status.ChannelIndex,
                ChannelName = r.Status.ChannelName, Endpoint = r.Status.Endpoint, State = r.Status.State, LastError = r.Status.LastError,
                ForwardedToMesh = r.Status.ForwardedToMesh, Clients = r.Peers.Select(p => p.Endpoint + (p.Authenticated ? "" : " [waiting for password]")).ToArray() }).ToList();
        }
        public void Publish(int channel, string line, bool ownMessage = false) { Publish(channel, settings => line, ownMessage); }
        public void Publish(int channel, Func<TelnetServerSettings, string> format, bool ownMessage = false)
        {
            lock (_gate)
            foreach (var route in _routes.Where(r => r.Settings.Enabled && r.Settings.ChannelIndex == channel))
            {
                if (ownMessage && !route.Settings.ShowOwnMessages) continue;
                var line = format(route.Settings);
                var output = FormatOutput(line, route.Settings);
                if (route.Settings.HistoryCount > 0) { route.Recent.Enqueue(output); while (route.Recent.Count > route.Settings.HistoryCount) route.Recent.Dequeue(); }
                foreach (var peer in route.Peers.Where(p => p.Authenticated).ToArray()) peer.Line(output + (route.Settings.SendBell && !ownMessage ? "\a" : ""));
            }
        }
        private async Task AcceptAsync(Route route)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await route.Listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    client.NoDelay = true;
                    Peer peer;
                    lock (_gate)
                    {
                        if (_stop.IsCancellationRequested || route.Peers.Count >= 16) { client.Close(); continue; }
                        peer = new Peer(client, Math.Max(256, route.Settings.HistoryCount + 16));
                        peer.Authenticated = !route.Settings.RequirePassword;
                        if (peer.Authenticated) { if (route.Settings.EchoInput) peer.Enqueue(new byte[] { 255, 251, 1 }); Welcome(route, peer); } else { peer.Enqueue(new byte[] { 255, 251, 1 }); peer.Line("Password:"); }
                        route.Peers.Add(peer);
                    }
                    _ = RunPeerAsync(route, peer);
                }
            }
            catch (Exception error) { if (!_stop.IsCancellationRequested) lock (_gate) { route.Status.State = "Error"; route.Status.LastError = error.Message; } }
        }
        private void Welcome(Route route, Peer peer)
        {
            route.Status.ChannelName = _channelName(route.Settings.ChannelIndex);
            if (route.Settings.SendWelcome)
            {
                var welcome = route.Settings.WelcomeMessage ?? "";
                welcome = welcome.Contains("{channel}") ? welcome.Replace("{channel}", route.Status.ChannelName) : "[" + route.Status.ChannelName + "] " + welcome;
                peer.Line(welcome);
            }
            foreach (var line in route.Recent) peer.Line(line);
        }
        private async Task RunPeerAsync(Route route, Peer peer)
        {
            var writer = peer.WriteAsync(_stop.Token);
            try
            {
                var stream = peer.Client.GetStream(); var bytes = new byte[1024]; var line = new List<byte>();
                int telnet = 0, verb = 0, escape = 0, failedPasswords = 0; bool afterCr = false, subIac = false;
                var echo = new List<byte>();
                Action flushEcho = delegate { if (echo.Count > 0) { peer.Enqueue(echo.ToArray()); echo.Clear(); } };
                Func<bool> echoEnabled = () => peer.Authenticated && route.Settings.EchoInput && !peer.EchoRefused;
                while (!_stop.IsCancellationRequested)
                {
                    int count = await stream.ReadAsync(bytes, 0, bytes.Length, _stop.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    for (int i = 0; i < count; i++)
                    {
                        byte value = bytes[i];
                        if (telnet == 3) { if (subIac && value == 240) { telnet = 0; subIac = false; } else subIac = value == 255; continue; }
                        if (telnet == 2)
                        {
                            flushEcho();
                            if (value == 1 && verb == 253 && (!peer.Authenticated || route.Settings.EchoInput)) { if (peer.EchoRefused) peer.Enqueue(new byte[] { 255, 251, 1 }); peer.EchoRefused = false; telnet = 0; continue; }
                            if (value == 1 && verb == 254) { peer.EchoRefused = true; peer.Enqueue(new byte[] { 255, 252, 1 }); }
                            else if (verb == 251 || verb == 253) peer.Enqueue(new byte[] { 255, (byte)(verb == 251 ? 254 : 252), value });
                            telnet = 0; continue;
                        }
                        if (telnet == 1)
                        {
                            telnet = 0;
                            if (value >= 251 && value <= 254) { verb = value; telnet = 2; continue; }
                            if (value == 250) { telnet = 3; continue; }
                            if (value != 255) continue;
                        }
                        else if (value == 255) { telnet = 1; continue; }
                        if (escape == 2) { if (value >= 64 && value <= 126) escape = 0; continue; }
                        if (escape == 1) { escape = 0; if (value == 91 || value == 79) { escape = 2; continue; } }
                        if (value == 27) { flushEcho(); line.Clear(); afterCr = false; escape = 1; peer.Line(""); continue; }
                        if (afterCr) { afterCr = false; if (value == 10 || value == 0) continue; }
                        if (value == 13 || value == 10)
                        {
                            if (echoEnabled()) { echo.Add(13); echo.Add(10); }
                            flushEcho();
                            afterCr = value == 13;
                            var text = Encoding.UTF8.GetString(line.ToArray()); line.Clear();
                            if (!peer.Authenticated)
                            {
                                if (!TelnetPassword.Verify(text, route.Settings.PasswordHash)) { if (++failedPasswords >= 3) return; peer.Line("Incorrect password. Password:"); continue; }
                                lock (_gate) { peer.Authenticated = true; if (!route.Settings.EchoInput || peer.EchoRefused) peer.Enqueue(new byte[] { 255, 252, 1 }); Welcome(route, peer); }
                                continue;
                            }
                            if (String.IsNullOrWhiteSpace(text)) continue;
                            if (route.Settings.ReadOnly) { peer.Line("ERROR: This server is read-only. Sending messages is disabled."); continue; }
                            try { await _send(route.Settings.ChannelIndex, route.Settings.ReplaceEmojisWithNames ? _emojiValues(text) : text, _stop.Token).ConfigureAwait(false); lock (_gate) route.Status.ForwardedToMesh++; }
                            catch (Exception error) { lock (_gate) route.Status.LastError = error.Message; peer.Line("ERROR: " + error.Message); }
                        }
                        else if (value == 8 || value == 127)
                        {
                            if (line.Count > 0) { int last = line.Count - 1; while (last > 0 && (line[last] & 192) == 128) last--; line.RemoveRange(last, line.Count - last); if (echoEnabled()) { echo.Add(8); echo.Add(32); echo.Add(8); } }
                        }
                        else if (value >= 32) { if (line.Count >= 4096) { peer.Line("ERROR: input line too long."); return; } line.Add(value); if (echoEnabled()) { echo.Add(value); if (value == 255) echo.Add(255); } }
                    }
                    flushEcho();
                }
            }
            catch (Exception error) { if (!_stop.IsCancellationRequested && !(error is ObjectDisposedException)) lock (_gate) route.Status.LastError = error.Message; }
            finally { peer.Close(); lock (_gate) route.Peers.Remove(peer); await writer.ConfigureAwait(false); }
        }
        public async Task StopAsync()
        {
            _stop.Cancel();
            Task[] tasks;
            lock (_gate)
            {
                foreach (var route in _routes) { route.Listener?.Stop(); foreach (var peer in route.Peers.ToArray()) peer.Close(); route.Status.State = "Stopped"; }
                tasks = _routes.Where(r => r.AcceptTask != null).Select(r => r.AcceptTask).ToArray();
            }
            if (tasks.Length > 0) await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        public void Dispose() { StopAsync().GetAwaiter().GetResult(); }
    }
}