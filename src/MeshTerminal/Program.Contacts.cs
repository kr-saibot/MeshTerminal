using MessageBox = MeshTerminal.SafeMessageBox;
using Dialog = MeshTerminal.SafeDialog;
using System;
using Google.Protobuf;
using MeshTerminal.Client;
using Terminal.Gui;
namespace MeshTerminal
{
    internal static partial class Program
    {
        private static void ShowOwnMeshtasticContact() { ShowOwnContact(MeshProtocol.Meshtastic); }
        private static void ShowOwnMeshCoreContact() { ShowOwnContact(MeshProtocol.MeshCore); }
        private static void ShowOwnContact(MeshProtocol protocol)
        {
            if (_mesh.Protocol != protocol) return;
            var core = protocol == MeshProtocol.MeshCore;
            var node = _mesh.Device.LocalNode;
            if (!_mesh.IsConnected || node?.Value.User == null || (core && node.Value.User.PublicKey.Length != 32)) { MessageBox.Query("Own contact", "Connect and load the device first.", "OK"); return; }
            var name = node.LongName ?? "";
            var uri = core ? ContactSharing.CreateMeshCoreUri(name, node.Value.User.PublicKey.ToByteArray()) : ContactSharing.CreateMeshtasticUri(node.Number, node.Value.User);
            var key = core ? node.Id : Convert.ToBase64String(node.Value.User.PublicKey.ToByteArray());
            var type = core ? "Type: Companion (1)" : "ID: " + node.Id;
            var details = "Name: " + name + "\n" + type + "\nPublic key: " + key + "\nImport link: " + uri;
            var dialog = new Dialog("Own " + protocol + " contact", 94, 19);
            dialog.Add(new Label("Name: " + name) { X = 1, Y = 1 },
                new Label(type) { X = 1, Y = 2 },
                new Label("Public key:") { X = 1, Y = 4 },
                new TextField(key) { X = 1, Y = 5, Width = Dim.Fill(2), ReadOnly = true },
                new Label("Import link (same data as the QR code):") { X = 1, Y = 7 },
                new TextView { Text = uri, X = 1, Y = 8, Width = Dim.Fill(2), Height = 4, ReadOnly = true, WordWrap = true },
                new Label("Contains your public identity; no private key or channel secret.") { X = 1, Y = 13 });
            var qr = new Button("Show QR"); qr.Clicked += () => ShowQr(uri, protocol + " contact QR", "Scan in the " + protocol + " app to add this contact.", core ? "Name, public key and contact type." : "Node ID, names and public identity.", "contact-qr.png");
            var copyLink = new Button("Copy link"); copyLink.Clicked += () => CopyTextWithFallback(uri, "Contact link copied.");
            var copyKey = new Button("Copy key"); copyKey.Clicked += () => CopyTextWithFallback(key, "Public key copied.");
            var copyAll = new Button("Copy data"); copyAll.Clicked += () => CopyTextWithFallback(details, "Contact data copied.");
            var close = new Button("Close", true); close.Clicked += () => Application.RequestStop();
            dialog.AddButton(qr); dialog.AddButton(copyLink); dialog.AddButton(copyKey); dialog.AddButton(copyAll); dialog.AddButton(close);
            Application.Run(dialog, HandleTerminalGuiException);
        }
    }
}
