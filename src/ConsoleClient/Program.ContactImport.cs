using MessageBox = ConsoleClient.SafeMessageBox;
using Dialog = ConsoleClient.SafeDialog;
using System;
using System.Threading.Tasks;
using Meshtastic.Client;
using Terminal.Gui;
namespace ConsoleClient
{
    internal static partial class Program
    {
        private static void PasteSharedContactLink(MeshProtocol protocol)
        {
            var dialog = new Dialog("Import " + protocol + " contact link", 94, 9);
            var input = new TextField("") { X = 1, Y = 2, Width = Dim.Fill(2) };
            dialog.Add(new Label("Paste a contact share link:") { X = 1, Y = 0 }, input);
            string link = null;
            var next = new Button("Next"); next.Clicked += () => { link = input.Text.ToString(); Application.RequestStop(); };
            var cancel = new Button("Cancel", true); cancel.Clicked += () => Application.RequestStop();
            dialog.AddButton(next); dialog.AddButton(cancel); Application.Run(dialog, HandleTerminalGuiException);
            if (link != null) ShowSharedContactImport(link, protocol);
        }
        private static void ShowSharedContactImport(string link, MeshProtocol protocol)
        {
            ImportedContact contact;
            try { contact = protocol == MeshProtocol.MeshCore ? SharedLinkImport.MeshCoreContact(link) : SharedLinkImport.MeshtasticContact(link); }
            catch (Exception ex) { MessageBox.ErrorQuery("Import contact", ex.Message, "OK"); return; }
            if (!_mesh.IsConnected || _mesh.Protocol != protocol || _mesh.Device.MyNode == null)
            { MessageBox.Query("Import contact", "Connect and synchronize a " + protocol + " device first.", "OK"); return; }
            var dialog = new Dialog("Import " + protocol + " contact", 94, 17);
            var name = String.IsNullOrWhiteSpace(contact.Name) ? "[unnamed]" : contact.Name;
            var description = "Name: " + name + "\n" + (protocol == MeshProtocol.Meshtastic ? "Node: !" + contact.MeshtasticContact.NodeNum.ToString("x8") : contact.MeshCoreCard != null ? "Signed advert contact card" : "Type: " + contact.Type) +
                "\n\nPublic key:\n" + contact.PublicKey + "\n\nThe contact is written to the connected device only after Import.";
            dialog.Add(new TextView { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(4), Text = description, ReadOnly = true, WordWrap = true });
            var status = new Label("") { X = 1, Y = Pos.AnchorEnd(4), Width = Dim.Fill(2) }; dialog.Add(status);
            var saving = false;
            var import = new Button("Import"); var close = new Button("Close", true); close.Clicked += () => Application.RequestStop();
            import.Clicked += () =>
            {
                if (saving) return;
                saving = true; import.Enabled = false; close.Enabled = false; status.Text = "Importing contact...";
                Task.Run(async () =>
                {
                    try
                    {
                        await _mesh.ImportSharedContactAsync(protocol, link);
                        Ui(() => { status.Text = "Contact imported."; RefreshChats(); RefreshNodePage(true); });
                    }
                    catch (Exception ex) { Ui(() => { status.Text = "Import could not be confirmed."; MessageBox.ErrorQuery("Import contact", ex.Message, "OK"); }); }
                    finally { Ui(() => { saving = false; close.Enabled = true; close.SetFocus(); }); }
                });
            };
            dialog.KeyPress += e => { if (saving && e.KeyEvent.Key == Key.Esc) e.Handled = true; };
            dialog.AddButton(import); dialog.AddButton(close); Application.Run(dialog, HandleTerminalGuiException);
        }
    }
}