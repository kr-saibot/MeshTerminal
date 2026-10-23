using MessageBox = ConsoleClient.SafeMessageBox;
using Dialog = ConsoleClient.SafeDialog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meshtastic.Client;
using Meshtastic.Protobufs;
using Terminal.Gui;

namespace ConsoleClient
{
    internal static partial class Program
    {
        private static void PasteMeshCoreChannelLink() { PasteSharedChannelLink(MeshProtocol.MeshCore); }
        private static void PasteMeshtasticChannelLink() { PasteSharedChannelLink(MeshProtocol.Meshtastic); }
        private static void PasteSharedChannelLink(MeshProtocol protocol)
        {
            var dialog = new Dialog("Import " + protocol + " channel link", 94, 9);
            var input = new TextField("") { X = 1, Y = 2, Width = Dim.Fill(2) };
            dialog.Add(new Label("Paste a channel share link:") { X = 1, Y = 0 }, input);
            string link = null;
            var next = new Button("Next"); next.Clicked += () => { link = input.Text.ToString(); Application.RequestStop(); };
            var close = new Button("Cancel", true); close.Clicked += () => Application.RequestStop();
            dialog.AddButton(next); dialog.AddButton(close);
            Application.Run(dialog, HandleTerminalGuiException);
            if (link != null) ShowSharedChannelImport(link, protocol);
        }
        private static void ShowMeshCoreChannelImport(string link) { ShowSharedChannelImport(link, MeshProtocol.MeshCore); }
        private static void ShowSharedChannelImport(string link, MeshProtocol protocol)
        {
            Channel imported;
            try
            {
                var channels = protocol == MeshProtocol.MeshCore ? new[] { ChannelSharing.ParseMeshCoreUri(link) } : SharedLinkImport.MeshtasticChannels(link).ToArray();
                var chosen = channels.Length == 1 ? 0 : SelectSharedChannel(channels);
                if (chosen < 0) return; imported = channels[chosen];
            }
            catch (Exception ex) { MessageBox.ErrorQuery("Import channel", ex.Message, "OK"); return; }
            if (!_mesh.IsConnected || _mesh.Protocol != protocol || _mesh.Device.MyNode == null)
            { MessageBox.Query("Import channel", "Connect and synchronize a " + protocol + " device first.", "OK"); return; }
            if (Interlocked.CompareExchange(ref _channelEditorActive, 1, 0) != 0)
            { MessageBox.Query("Import channel", "The channel editor is already active.", "OK"); return; }
            Task.Run(async () =>
            {
                try { var slots = await _mesh.GetChannelSlotsAsync(); Ui(() => RunSharedChannelImport(imported, slots, protocol)); }
                catch (Exception ex) { Interlocked.Exchange(ref _channelEditorActive, 0); Ui(() => MessageBox.ErrorQuery("Import channel", ex.Message, "OK")); }
            });
        }
        private static int SelectSharedChannel(Channel[] channels)
        {
            var dialog = new Dialog("Select channel from link - radio settings are not imported", 90, 17);
            var list = new ListView(channels.Select((c, i) => (i + 1) + ": " + (String.IsNullOrEmpty(c.Settings.Name) ? "[default name]" : c.Settings.Name)).ToList()) { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3) };
            var result = -1;
            var select = new Button("Select"); select.Clicked += () => { result = list.SelectedItem; Application.RequestStop(); };
            var cancel = new Button("Cancel", true); cancel.Clicked += () => Application.RequestStop();
            dialog.Add(list); dialog.AddButton(select); dialog.AddButton(cancel); Application.Run(dialog, HandleTerminalGuiException);
            return result;
        }
        private static void RunSharedChannelImport(Channel imported, IReadOnlyList<Channel> slots, MeshProtocol protocol)
        {
            try
            {
                if (!_mesh.IsConnected || _mesh.Protocol != protocol) { MessageBox.ErrorQuery("Import channel", protocol + " device is no longer connected.", "OK"); return; }
                var dialog = new Dialog("Import " + protocol + " channel", 92, 22);
                dialog.Add(new Label("Channel: " + imported.Settings.Name) { X = 1, Y = 1 },
                    new Label("Select a slot. Existing channels require confirmation. Radio settings are not imported.") { X = 1, Y = 3, Width = Dim.Fill(2) });
                var list = new ListView(slots.Select(c => "Slot " + c.Index + "  " + (c.Role == Channel.Types.Role.Disabled ? "[free]" : c.Settings?.Name ?? "[configured]")).ToList()) { X = 1, Y = 5, Width = Dim.Fill(2), Height = Dim.Fill(5) };
                var existing = slots.ToList().FindIndex(c => c.Role != Channel.Types.Role.Disabled && c.Settings != null && c.Settings.Name == imported.Settings.Name && c.Settings.Psk.Equals(imported.Settings.Psk));
                var free = slots.ToList().FindIndex(c => c.Role == Channel.Types.Role.Disabled);
                if (slots.Count > 0) list.SelectedItem = existing >= 0 ? existing : Math.Max(0, free);
                var status = new Label("The channel is written only after selecting Import.") { X = 1, Y = Pos.AnchorEnd(4), Width = Dim.Fill(2) };
                var saving = false;
                var save = new Button("Import");
                var close = new Button("Close", true); close.Clicked += () => Application.RequestStop();
                save.Clicked += () =>
                {
                    if (saving || list.SelectedItem < 0 || list.SelectedItem >= slots.Count) return;
                    if (!_mesh.IsConnected || _mesh.Protocol != protocol) { MessageBox.ErrorQuery("Import channel", "Connect to " + protocol + " first.", "OK"); return; }
                    var slot = slots[list.SelectedItem];
                    if (slot.Role != Channel.Types.Role.Disabled &&
                        MessageBox.Query("Replace channel", "Slot " + slot.Index + " is occupied by " + (slot.Settings?.Name ?? "a channel") + ".\nReplace it with " + imported.Settings.Name + "?", "Cancel", "Replace") != 1) return;
                    var desired = slot.Clone(); desired.Settings = imported.Settings.Clone(); desired.Role = slot.Role == Channel.Types.Role.Primary ? Channel.Types.Role.Primary : Channel.Types.Role.Secondary;
                    saving = true; save.Enabled = false; close.Enabled = false; list.Enabled = false;
                    status.Text = "Saving and verifying the device reply...";
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _mesh.SaveChannelAsync(desired);
                            Ui(() => { status.Text = "Imported and verified on the device."; RefreshChats(); });
                        }
                        catch (Exception ex) { Ui(() => { status.Text = "Import could not be confirmed. Reload channels to check."; MessageBox.ErrorQuery("Import channel", ex.Message, "OK"); }); }
                        finally { Ui(() => { saving = false; close.Enabled = true; close.SetFocus(); }); }
                    });
                };
                dialog.KeyPress += e => { if (saving && e.KeyEvent.Key == Key.Esc) e.Handled = true; };
                dialog.Add(list, status); dialog.AddButton(save); dialog.AddButton(close);
                Application.Run(dialog, HandleTerminalGuiException);
            }
            finally { Interlocked.Exchange(ref _channelEditorActive, 0); }
        }
    }
}