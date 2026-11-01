using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MeshTerminal.Client;
using Meshtastic.Protobufs;
using Terminal.Gui;
using Dialog = MeshTerminal.SafeDialog;
using MessageBox = MeshTerminal.SafeMessageBox;
namespace MeshTerminal
{
    internal static partial class Program
    {
        private static void ShowMessageInsertMenu()
        {
            if (_input == null) return;
            var cursor = _input.CursorPosition;
            try
            {
                var dialog = new Dialog("Insert into message", 60, 24);
                int selected = -1;
                var labels = new[] { "Clipboard contents", "Emojis", "Current position", "MeshCore / Meshtastic channel link", "Contact link", "Date and time", "Map cursor position", "Position marker", "Symbols" };
                for (int i = 0; i < labels.Length; i++)
                {
                    int choice = i;
                    var button = new Button(labels[i]) { X = 2, Y = 1 + i * 2 };
                    button.Clicked += () => { selected = choice; Application.RequestStop(); };
                    dialog.Add(button);
                }
                var cancel = new Button("Cancel", true); cancel.Clicked += () => Application.RequestStop();
                dialog.AddButton(cancel);
                Application.Run(dialog, HandleTerminalGuiException);
                _input.CursorPosition = cursor;
                if (selected == 0)
                {
                    var text = MessageClipboard.Read();
                    InsertEmojiIntoMessage(text);
                }
                else if (selected == 1) ShowEmojiPicker();
                else if (selected == 2)
                {
                    var device = _mesh.Device;
                    if (!device.Latitude.HasValue || !device.Longitude.HasValue)
                        MessageBox.Query("Current position", "No position is available from the connected device.", "OK");
                    else InsertEmojiIntoMessage("[LOC]" + device.Latitude.Value.ToString("F6", CultureInfo.InvariantCulture) + "," + device.Longitude.Value.ToString("F6", CultureInfo.InvariantCulture));
                }
                else if (selected == 3)
                {
                    var link = SelectMessageChannelLink();
                    if (link != null) InsertEmojiIntoMessage(link);
                }
                else if (selected == 4)
                {
                    var link = SelectMessageContactLink();
                    if (link != null) InsertEmojiIntoMessage(link);
                }
                else if (selected == 5)
                {
                    var value = SelectInsertionValue("Insert date and time", new List<Tuple<string, Func<string>>>
                    {
                        Tuple.Create("Local date and time", (Func<string>)(() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))),
                        Tuple.Create("UTC date and time", (Func<string>)(() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)))
                    });
                    if (value != null) InsertEmojiIntoMessage(value);
                }
                else if (selected == 6)
                {
                    if (_nodeMap == null) MessageBox.Query("Map cursor position", "The map is not available.", "OK");
                    else InsertEmojiIntoMessage(InsertionCoordinates(_nodeMap.CenterLatitude, _nodeMap.CenterLongitude));
                }
                else if (selected == 8)
                {
                    var symbol = SelectMapPointSymbol();
                    if (symbol != null) InsertEmojiIntoMessage(symbol);
                }
                else if (selected == 7)
                {
                    var markers = _nodeMap == null ? new List<MapPositionMarker>() : _nodeMap.GetPositionMarkers().OrderBy(m => m.CallSign).ToList();
                    var values = markers.Select(m => Tuple.Create(m.CallSign + " | " + InsertionCoordinates(m.Latitude, m.Longitude),
                        (Func<string>)(() => m.CallSign + ": " + InsertionCoordinates(m.Latitude, m.Longitude)))).ToList();
                    var value = SelectInsertionValue("Insert position marker", values);
                    if (value != null) InsertEmojiIntoMessage(value);
                }
            }
            catch (Exception ex) { MessageBox.ErrorQuery("Insert into message", ex.Message, "OK"); }
            finally { _input.SetFocus(); }
        }
        private static string InsertionCoordinates(double latitude, double longitude)
        { return latitude.ToString("F6", CultureInfo.InvariantCulture) + "," + longitude.ToString("F6", CultureInfo.InvariantCulture); }

        private static string SelectInsertionValue(string title, List<Tuple<string, Func<string>>> values)
        {
            if (values.Count == 0) { MessageBox.Query(title, "No entries are available.", "OK"); return null; }
            var dialog = new Dialog(title, 90, 20);
            var list = new ListView(values.Select(v => v.Item1).ToList()) { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3) };
            string result = null;
            Action select = () =>
            {
                if (list.SelectedItem < 0 || list.SelectedItem >= values.Count) return;
                try { result = values[list.SelectedItem].Item2(); Application.RequestStop(); }
                catch (Exception ex) { MessageBox.ErrorQuery(title, ex.Message, "OK"); }
            };
            var insert = new Button("Insert"); insert.Clicked += select;
            list.OpenSelectedItem += args => select();
            var cancel = new Button("Cancel", true); cancel.Clicked += () => Application.RequestStop();
            dialog.Add(list); dialog.AddButton(insert); dialog.AddButton(cancel);
            Application.Run(dialog, HandleTerminalGuiException);
            return result;
        }

        private static string SelectMessageContactLink()
        {
            var stored = _store.GetNodes(StoredNodeSort.Name).ToDictionary(n => n.NodeNumber);
            var live = _mesh.Nodes.ToDictionary(n => n.Number);
            var choices = new List<Tuple<string, Func<string>>>();
            foreach (var number in stored.Keys.Union(live.Keys).OrderByDescending(n => stored.ContainsKey(n) && stored[n].IsFavorite)
                .ThenBy(n => live.ContainsKey(n) ? live[n].LongName : stored[n].LongName))
            {
                var actual = live.ContainsKey(number) ? live[number] : null;
                var saved = stored.ContainsKey(number) ? stored[number] : null;
                var name = actual?.LongName ?? saved?.LongName ?? "";
                var id = actual?.Id ?? saved?.NodeId;
                if (_mesh.Protocol == MeshProtocol.MeshCore && (id == null || id.Length != 64 || !id.All(Uri.IsHexDigit))) continue;
                if (number == 0 || number == UInt32.MaxValue) continue;
                var protocol = _mesh.Protocol;
                var key = id;
                var user = actual?.Value.User?.Clone() ?? new User { Id = id ?? "!" + number.ToString("x8"), LongName = name, ShortName = saved?.ShortName ?? "" };
                int type = actual?.MeshCoreContactType ?? saved?.MeshCoreContactType ?? 1;
                if (type < 1 || type > 4) type = 1;
                var nodeNumber = number;
                var label = (saved != null && saved.IsFavorite ? "* " : "") + name + " | " + (protocol == MeshProtocol.MeshCore ? key.Substring(0, 8) : id);
                choices.Add(Tuple.Create(label, (Func<string>)(() => protocol == MeshProtocol.MeshCore
                    ? ContactSharing.CreateMeshCoreUri(name, Enumerable.Range(0, 32).Select(i => Convert.ToByte(key.Substring(i * 2, 2), 16)).ToArray(), type)
                    : ContactSharing.CreateMeshtasticUri(nodeNumber, user))));
            }
            return SelectInsertionValue("Insert " + _mesh.Protocol + " contact link", choices);
        }

        private static string SelectMessageChannelLink()
        {
            var channels = new List<Tuple<string, MeshProtocol, Channel>>();
            foreach (var channel in _mesh.Channels.Where(c => c.Role != Channel.Types.Role.Disabled && c.Value.Settings != null))
                channels.Add(Tuple.Create(_mesh.Protocol + " | Device | " + ChannelName(channel.Value) + " | Slot " + channel.Index, _mesh.Protocol, channel.Value.Clone()));
            foreach (var local in _settings.LocalChannels ?? new List<LocalChannel>())
            {
                try { var channel = local.Read(); channels.Add(Tuple.Create(local.Protocol + " | Local | " + ChannelName(channel), local.Protocol, channel)); }
                catch { /* A corrupt local entry must not hide other available channels. */ }
            }
            if (channels.Count == 0) { MessageBox.Query("Channel link", "No device or locally saved channel is available. Add a channel in the channel editor first.", "OK"); return null; }
            var dialog = new Dialog("Insert channel link", 88, 21);
            var list = new ListView(channels.Select(c => c.Item1).ToList()) { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3) };
            string result = null;
            Action select = () =>
            {
                if (list.SelectedItem < 0 || list.SelectedItem >= channels.Count) return;
                var chosen = channels[list.SelectedItem];
                try { result = ChannelSharing.CreateUri(chosen.Item2, chosen.Item3); Application.RequestStop(); }
                catch (Exception ex) { MessageBox.ErrorQuery("Channel link", ex.Message, "OK"); }
            };
            var insert = new Button("Insert"); insert.Clicked += select;
            list.OpenSelectedItem += args => select();
            var cancel = new Button("Cancel", true); cancel.Clicked += () => Application.RequestStop();
            dialog.Add(list); dialog.AddButton(insert); dialog.AddButton(cancel);
            Application.Run(dialog, HandleTerminalGuiException);
            return result;
        }
    }
}
