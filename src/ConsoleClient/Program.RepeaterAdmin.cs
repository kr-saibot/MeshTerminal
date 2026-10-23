using MessageBox = ConsoleClient.SafeMessageBox;
using Dialog = ConsoleClient.SafeDialog;
using System;
using System.Linq;
using System.Threading.Tasks;
using Meshtastic.Client;
using Terminal.Gui;

namespace ConsoleClient
{
    internal static partial class Program
    {
        private static void ShowMeshCoreRepeaterAdmin() { ShowMeshCoreRepeaterAdmin(null); }
        private static void ShowMeshCoreRepeaterAdmin(uint? targetNode)
        {
            if (_mesh.Protocol != MeshProtocol.MeshCore || !_mesh.IsConnected)
            { MessageBox.Query("Repeater administration", "Connect to MeshCore first.", "OK"); return; }
            var favorites = new System.Collections.Generic.HashSet<uint>(_store.GetNodes().Where(n => n.IsFavorite).Select(n => n.NodeNumber));
            var nodes = _mesh.Nodes.Where(n => n.MeshCoreContactType == 2).OrderByDescending(n => favorites.Contains(n.Number)).ThenBy(n => n.LongName).ToList();
            if (nodes.Count == 0) { MessageBox.Query("Repeater administration", "No repeater is known yet.", "OK"); return; }
            var chooser = new Dialog("Select repeater", 70, 17);
            var list = new ListView(nodes.Select(n => n.LongName).ToList()) { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3) };
            int selected = targetNode.HasValue ? nodes.FindIndex(n => n.Number == targetNode.Value) : -1;
            var open = new Button("Open", true); open.Clicked += delegate { selected = list.SelectedItem; Application.RequestStop(); };
            var cancel = new Button("Cancel"); cancel.Clicked += delegate { Application.RequestStop(); };
            chooser.Add(list); chooser.AddButton(open); chooser.AddButton(cancel); if (!targetNode.HasValue) Application.Run(chooser);
            if (selected < 0 || selected >= nodes.Count) return;
            var node = nodes[selected];
            var dialog = new Dialog("Repeater administration: " + node.LongName, 100, 30);
            var loginFrame = new FrameView("Login") { X = 1, Y = 0, Width = Dim.Fill(2), Height = 7 };
            var shortcutFrame = new FrameView("Command buttons") { X = Pos.AnchorEnd(27), Y = 12, Width = 26, Height = 11 };
            var password = new TextField("") { X = 16, Y = 0, Width = 35, Secret = true };
            var login = new Button("Log in") { X = 53, Y = 0 };
            var status = new Label("Log in with the repeater administrator password. Passwords are saved only when explicitly selected.") { X = 0, Y = 4, Width = Dim.Fill() };
            var output = new TextView { X = 1, Y = 12, Width = Dim.Fill(29), Height = 11, ReadOnly = true, WordWrap = true, AllowsTab = false };
            var command = new TextField("") { X = 11, Y = 10, Width = Dim.Fill(13) };
            var send = new Button("Send") { X = Pos.AnchorEnd(11), Y = 10, Enabled = false };
            var quick = new CommandDropdown { X = 1, Y = 8, Width = 66, Height = 1 };
            var commands = RepeaterCommands.All;
            quick.SetSource(commands.Select(c => c.ToString()).ToList()); quick.SelectedItem = 0;
            var run = new Button("Run selected") { X = 71, Y = 8, Enabled = false };
            var help = new Button("Help") { X = 87, Y = 8 };
            var remember = new CheckBox("Save password on this computer") { X = 1, Y = 2 };
            var forget = new Button("Forget password") { X = 65, Y = 0 };
            if (_settings.RepeaterCredentials == null) _settings.RepeaterCredentials = new System.Collections.Generic.List<RepeaterCredential>();
            var credential = _settings.RepeaterCredentials.FirstOrDefault(c => String.Equals(c.PublicKey, node.Id, StringComparison.OrdinalIgnoreCase));
            bool showingSavedPassword = false;
            Action showSavedPassword = () => { password.Secret = false; password.Text = "********"; showingSavedPassword = true; password.SetNeedsDisplay(); };
            password.KeyPress += args =>
            {
                if (!showingSavedPassword) return;
                var key = args.KeyEvent.Key;
                if (key == Key.Tab || key == Key.BackTab || key == Key.Enter || key == Key.Esc || key == Key.CursorLeft || key == Key.CursorRight || key == Key.Home || key == Key.End) return;
                showingSavedPassword = false; password.Text = ""; password.Secret = true;
            };
            if (credential != null)
            {
                try { credential.Read(); showSavedPassword(); remember.Checked = true; }
                catch { status.Text = "Saved password cannot be decrypted. Enter it again or select Forget password."; }
            }
            forget.Clicked += delegate
            {
                _settings.RepeaterCredentials.RemoveAll(c => String.Equals(c.PublicKey, node.Id, StringComparison.OrdinalIgnoreCase));
                try { MeshtasticSettingsStore.Save("meshtastic-settings.xml", _settings); showingSavedPassword = false; password.Secret = true; password.Text = ""; remember.Checked = false; status.Text = "Saved password removed."; }
                catch (Exception ex) { MessageBox.ErrorQuery("Password", ex.Message, "OK"); }
            };
            help.Clicked += delegate
            {
                if (quick.SelectedItem < 0 || quick.SelectedItem >= commands.Count) return;
                var entry = commands[quick.SelectedItem];
                var info = new Dialog("Command help", 88, 20);
                info.Add(new TextView { X = 1, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(3), ReadOnly = true, AllowsTab = false, WordWrap = true,
                    Text = entry.Template + "\n\n" + entry.Help + "\n\n" + entry.Availability + (entry.RemoteAllowed ? "\nAvailability depends on firmware and hardware." : "\nSerial access required; cannot run through this remote dialog.") + "\n\n" + RepeaterCommands.Reference });
                var done = new Button("Close", true); done.Clicked += delegate { Application.RequestStop(); }; info.AddButton(done); Application.Run(info);
            };
            if (_settings.RepeaterQuickCommands == null) _settings.RepeaterQuickCommands = new string[6];
            var shortcuts = new Button[6];
            var clearOutput = new Button("Clear output") { X = 1, Y = 24 };
            var copyOutput = new Button("Copy output") { X = Pos.Right(clearOutput) + 2, Y = 24 };
            copyOutput.Clicked += () => CopyTextWithFallback(output.Text.ToString(), "Repeater output copied.");
            clearOutput.Clicked += () => { output.Text = ""; output.TopRow = 0; output.SetNeedsDisplay(); };
            var editShortcuts = new Button("Edit buttons") { X = Pos.Center(), Y = 8 };
            Action refreshShortcuts = () => { for (int i = 0; i < 6; i++) { var value = i < _settings.RepeaterQuickCommands.Length ? _settings.RepeaterQuickCommands[i] : ""; shortcuts[i].Text = String.IsNullOrWhiteSpace(value) ? (i + 1) + ": (empty)" : value; shortcuts[i].Enabled = send.Enabled && !String.IsNullOrWhiteSpace(value); } };
            editShortcuts.Clicked += () =>
            {
                var editor = new Dialog("Edit repeater command buttons", 88, 21);
                var fields = new TextField[6];
                for (int i = 0; i < 6; i++)
                {
                    fields[i] = new TextField(i < _settings.RepeaterQuickCommands.Length ? _settings.RepeaterQuickCommands[i] : "") { X = 8, Y = 1 + i * 2, Width = Dim.Fill(2) };
                    editor.Add(new Label((i + 1) + ":") { X = 2, Y = 1 + i * 2 }, fields[i]);
                }
                editor.Add(new Label("Commands apply to all repeaters. Empty fields disable a button.") { X = 1, Y = 14 });
                var save = new Button("Save"); save.Clicked += () =>
                {
                    var previous = _settings.RepeaterQuickCommands;
                    _settings.RepeaterQuickCommands = fields.Select(f => f.Text.ToString().Trim()).ToArray();
                    try { MeshtasticSettingsStore.Save("meshtastic-settings.xml", _settings); refreshShortcuts(); Application.RequestStop(); }
                    catch (Exception ex) { _settings.RepeaterQuickCommands = previous; MessageBox.ErrorQuery("Command buttons", ex.Message, "OK"); }
                };
                var cancelEdit = new Button("Cancel"); cancelEdit.Clicked += () => Application.RequestStop();
                editor.AddButton(save); editor.AddButton(cancelEdit); Application.Run(editor);
            };
            var close = new Button("Close", true); close.Clicked += delegate { Application.RequestStop(); };
            bool closed = false, busy = false, authorized = false;
            Action<bool> setBusy = value => { busy = value; login.Enabled = !value; password.Enabled = !value; remember.Enabled = !value; forget.Enabled = !value; send.Enabled = !value && authorized; run.Enabled = !value && authorized; command.Enabled = !value; quick.Enabled = !value; for (int i = 0; i < 6; i++) shortcuts[i].Enabled = !value && authorized && i < _settings.RepeaterQuickCommands.Length && !String.IsNullOrWhiteSpace(_settings.RepeaterQuickCommands[i]); };
            Action<string> execute = text =>
            {
                if (busy || !authorized || String.IsNullOrWhiteSpace(text)) return;
                var entry = RepeaterCommands.Find(text);
                if (RepeaterCommands.ContainsPlaceholders(text)) { MessageBox.Query("Command parameters", "Replace the parameter placeholders before sending.", "OK"); return; }
                if (entry != null && !entry.RemoteAllowed) { MessageBox.Query("Serial command", "This command requires a direct serial connection to the repeater.", "OK"); return; }
                if (entry != null && entry.Confirm && MessageBox.Query("Confirm repeater command", "Execute " + entry.Template + " on " + node.LongName + "?", "Execute", "Cancel") != 0) return;
                setBusy(true); status.Text = "Waiting for repeater reply...";
                // Password-changing commands may echo the new secret in the reply.
                bool secret = text.TrimStart().StartsWith("password ", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith("set guest.password ", StringComparison.OrdinalIgnoreCase) || text.StartsWith("set prv.key ") || text.StartsWith("set bridge.secret ");
                output.Text += "\n> " + (secret ? "[password command]" : text) + "\n"; output.MoveEnd();
                command.Text = "";
                Task.Run(async delegate
                {
                    string result;
                    try { result = await _mesh.SendMeshCoreRepeaterCommandAsync(node.Number, text); if (secret) result = "Password command completed. Reply hidden."; }
                    catch (Exception ex) { result = "Error: " + ex.Message; }
                    Ui(delegate { if (closed) return; output.Text += result + "\n"; output.MoveEnd(); status.Text = "Ready. A timeout does not prove that a command was not executed."; setBusy(false); command.SetFocus(); });
                });
            };
            for (int i = 0; i < 6; i++)
            {
                int index = i;
                shortcuts[i] = new Button("") { X = 0, Y = i, Width = Dim.Fill(), Enabled = false };
                shortcuts[i].Clicked += () =>
                {
                    if (index >= _settings.RepeaterQuickCommands.Length) return;
                    var text = _settings.RepeaterQuickCommands[index];
                    if (RepeaterCommands.ContainsPlaceholders(text)) { command.Text = text; command.SetFocus(); }
                    else execute(text);
                };
                shortcutFrame.Add(shortcuts[i]);
            }
            refreshShortcuts();
            login.Clicked += delegate
            {
                if (busy) return;
                string secret;
                try { secret = showingSavedPassword ? _settings.RepeaterCredentials.First(c => String.Equals(c.PublicKey, node.Id, StringComparison.OrdinalIgnoreCase)).Read() : password.Text.ToString(); }
                catch (Exception ex) { MessageBox.ErrorQuery("Password", ex.Message, "OK"); return; }
                bool savePassword = remember.Checked; if (!savePassword) { showingSavedPassword = false; password.Secret = true; password.Text = ""; } authorized = false; setBusy(true); status.Text = "Logging in...";
                Task.Run(async delegate
                {
                    string result;
                    try { await _mesh.LoginMeshCoreRepeaterAsync(node.Number, secret); authorized = true; result = "Login accepted. Administrator commands are available.";
                        try
                        {
                            var saved = savePassword ? RepeaterCredential.Create(node.Id, secret) : null;
                            _settings.RepeaterCredentials.RemoveAll(c => String.Equals(c.PublicKey, node.Id, StringComparison.OrdinalIgnoreCase));
                            if (saved != null) _settings.RepeaterCredentials.Add(saved);
                            MeshtasticSettingsStore.Save("meshtastic-settings.xml", _settings);
                        }
                        catch (Exception ex) { result += " Password settings could not be saved: " + ex.Message; } }
                    catch (Exception ex) { result = "Login failed: " + ex.Message; }
                    finally { secret = null; }
                    Ui(delegate { if (closed) return; status.Text = result; if (authorized && savePassword && _settings.RepeaterCredentials.Any(c => String.Equals(c.PublicKey, node.Id, StringComparison.OrdinalIgnoreCase))) showSavedPassword(); setBusy(false); if (authorized) command.SetFocus(); });
                });
            };
            send.Clicked += delegate { execute(command.Text.ToString()); };
            run.Clicked += delegate { if (quick.SelectedItem >= 0 && quick.SelectedItem < commands.Count)
                {
                    var entry = commands[quick.SelectedItem];
                    if (entry.NeedsParameters) { command.Text = entry.PreparedText; command.SetFocus(); status.Text = "Replace parameters in <...>, then send. Help explains the command."; }
                    else execute(entry.PreparedText);
                } };
            command.KeyPress += args => { if (args.KeyEvent.Key == Key.Enter) { args.Handled = true; execute(command.Text.ToString()); } };
            loginFrame.Add(new Label("Admin password:") { X = 0, Y = 0 }, password, login, remember, forget, status);
            shortcutFrame.Add(editShortcuts);
            dialog.Add(loginFrame, shortcutFrame, quick, run, help, output,
                new Label("Command:") { X = 1, Y = 10 }, command, send, clearOutput, copyOutput);
            var scrollbar = new ScrollBarView(output, true, false) { AutoHideScrollBars = false };
            scrollbar.ChangedPosition += () => { output.TopRow = scrollbar.Position; output.SetNeedsDisplay(); };
            output.DrawContent += args => { scrollbar.Size = output.Lines; scrollbar.Position = output.TopRow; scrollbar.Refresh(); };
            dialog.AddButton(close);
            dialog.Ready += () => { if (showingSavedPassword) showSavedPassword(); };
            try { Application.Run(dialog); }
            finally { closed = true; password.Text = ""; }
        }
    }
}
