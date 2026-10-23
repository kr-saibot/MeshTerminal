using MessageBox = MeshTerminal.SafeMessageBox;
using Dialog = MeshTerminal.SafeDialog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MeshTerminal.Client;
using Terminal.Gui;

namespace MeshTerminal
{
    internal static partial class Program
    {
        private static int _advertActive;

        private static void SendMeshCoreAdvert()
        { if (_mesh.Protocol != MeshProtocol.MeshCore) { MessageBox.Query("MeshCore", "Select MeshCore in the connection settings first.", "OK"); return; } SendAdvert(); }
        private static void SendNodeAnnouncement()
        { if (_mesh.Protocol != MeshProtocol.Meshtastic) { MessageBox.Query("Node information", "Use MeshCore > Send advert for MeshCore devices.", "OK"); return; } SendAdvert(); }
        private static void SendAdvert()
        {
            if (!_mesh.IsConnected) { MessageBox.Query("Advert", "Connect the device first.", "OK"); return; }
            if (Volatile.Read(ref _advertActive) != 0) { MessageBox.Query("Advert", "An advert is already being sent.", "OK"); return; }
            var core = _mesh.Protocol == MeshProtocol.MeshCore;
            var dialog = new Dialog(core ? "Send MeshCore advert" : "Broadcast node information", 67, 12);
            var flood = new CheckBox("Forward through the mesh (flood)") { X = 2, Y = 3, Checked = true, Visible = core };
            var position = new CheckBox("Include the device position") { X = 2, Y = 4, Checked = false, Visible = core };
            dialog.Add(new Label(core ? "Announce this device using its current name and identity.\nWithout flood, only directly reachable devices receive it." : "Broadcast this device's name and identity\non its primary Meshtastic channel.") { X = 2, Y = 1, Width = Dim.Fill(4), Height = 2 }, flood, position);
            var send = new Button("Send", true); var cancel = new Button("Cancel");
            cancel.Clicked += delegate { Application.RequestStop(); };
            send.Clicked += delegate
            {
                var relay = flood.Checked; var includePosition = position.Checked;
                if (Interlocked.CompareExchange(ref _advertActive, 1, 0) != 0) return;
                Application.RequestStop();
                Task.Run(async delegate
                {
                    try { if (core) await _mesh.SendAdvertAsync(relay, includePosition); else await _mesh.SendAdvertAsync(relay); Ui(delegate { MessageBox.Query("Advert", "The announcement was submitted to the device.", "OK"); }); }
                    catch (Exception ex) { Ui(delegate { MessageBox.ErrorQuery("Advert", ex.Message, "OK"); }); }
                    finally { Interlocked.Exchange(ref _advertActive, 0); }
                });
            };
            dialog.AddButton(send); dialog.AddButton(cancel); Application.Run(dialog, HandleTerminalGuiException);
        }

        private static void ShowChannelQr(string uri, MeshProtocol protocol)
        { ShowQr(uri, protocol + " channel QR", "Current editor values; save separately to update the device.", "Scan in the app. The QR code includes the channel key.", "channel-qr.png"); }

        private static void ShowQr(string uri, string title, string introduction, string contents, string defaultFileName)
        {
            using (var data = ChannelQrCode.Create(uri))
            {
                var rows = ChannelQrCode.HalfBlockRows(data.ModuleMatrix);
                var dialog = new Dialog(title, Math.Max(66, rows[0].Length + 6), rows.Length + 8);
                dialog.Add(new Label(introduction) { X = 1, Y = 0 },
                    new Label(contents) { X = 1, Y = 1 });
                var code = new ChannelQrView(rows) { X = Pos.Center(), Y = 3, Width = Dim.Fill(2), Height = Dim.Fill(3) };
                dialog.Add(code);
                var save = new Button("Save PNG");
                save.Clicked += delegate
                {
                    var fileDialog = new Dialog("Save QR as PNG", 76, 8);
                    var path = new TextField(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, defaultFileName)) { X = 1, Y = 1, Width = Dim.Fill(2) };
                    var write = new Button("Save", true); var cancel = new Button("Cancel");
                    cancel.Clicked += delegate { Application.RequestStop(); };
                    write.Clicked += delegate
                    {
                        try
                        {
                            var file = Path.GetFullPath(path.Text.ToString());
                            if (File.Exists(file) && MessageBox.Query("Overwrite PNG", "Replace the existing file?", "Replace", "Cancel") != 0) return;
                            File.WriteAllBytes(file, ChannelQrCode.Png(data)); Application.RequestStop();
                        }
                        catch (Exception ex) { MessageBox.ErrorQuery("Save QR", ex.Message, "OK"); }
                    };
                    fileDialog.Add(path); fileDialog.AddButton(write); fileDialog.AddButton(cancel); Application.Run(fileDialog, HandleTerminalGuiException);
                };
                var close = new Button("Close", true); close.Clicked += delegate { Application.RequestStop(); };
                dialog.AddButton(save); dialog.AddButton(close); Application.Run(dialog, HandleTerminalGuiException);
            }
        }
    }

    internal sealed class ChannelQrView : View
    {
        private readonly string[] rows;
        public ChannelQrView(string[] rows) { this.rows = rows; }
        public override void Redraw(Rect bounds)
        {
            Clear();
            if (Bounds.Width < rows[0].Length || Bounds.Height < rows.Length)
            {
                Move(0, 0); Application.Driver.SetAttribute(ColorScheme.Normal);
                Application.Driver.AddStr("Enlarge terminal to see the full QR, or save PNG."); return;
            }
            Application.Driver.SetAttribute(Application.Driver.MakeAttribute(Color.Black, Color.White));
            var left = (Bounds.Width - rows[0].Length) / 2;
            for (var y = 0; y < rows.Length; y++) { Move(left, y); Application.Driver.AddStr(rows[y]); }
        }
    }
}
