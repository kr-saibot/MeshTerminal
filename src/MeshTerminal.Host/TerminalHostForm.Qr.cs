using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace MeshTerminal.Host
{
    internal sealed partial class TerminalHostForm
    {
        private readonly string _qrPipeName = "meshterminal-qr-" + Guid.NewGuid().ToString("N");
        private bool _qrBusy;
        private async void ScanScreenQr(bool fullScreen)
        {
            if (_qrBusy) return;
            _qrBusy = true;
            bool wasVisible = Visible, wasHiddenToTray = _hiddenToTray;
            var previousState = WindowState;
            try
            {
                // Capture a frozen desktop after the host and native menu disappear.
                Hide(); await Task.Delay(300);
                string[] texts;
                var bounds = SystemInformation.VirtualScreen;
                using (var screen = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(screen)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    if (fullScreen) texts = await Task.Run(() => ScreenQrReader.Decode(screen));
                    else
                    {
                        using (var selection = new QrCaptureSelection(screen, bounds))
                        {
                            if (selection.ShowDialog() != DialogResult.OK) return;
                            using (var crop = screen.Clone(selection.Selection, PixelFormat.Format32bppArgb))
                                texts = await Task.Run(() => ScreenQrReader.Decode(crop));
                        }
                    }
                }
                RestoreFromTray(); Show(); WindowState = FormWindowState.Normal; Activate();
                if (texts.Length == 0) { MessageBox.Show(this, "Kein QR-Code erkannt. Bitte den Code vollständig einschließlich des hellen Randes auswählen.", "QR-Code lesen", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                string selected = SelectQrText(texts);
                if (selected == null) return;
                await MeshTerminal.HostQrBridge.SendAsync(_qrPipeName, selected);
                // Let the terminal show its import/insert confirmation.
                wasVisible = true; wasHiddenToTray = false; previousState = FormWindowState.Normal;
            }
            catch (Exception ex) { MessageBox.Show(this, "QR-Code konnte nicht übernommen werden:\r\n" + ex.Message, "QR-Code lesen", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally
            {
                _qrBusy = false;
                if (!IsDisposed)
                {
                    if (wasHiddenToTray) HideToTray();
                    else if (!wasVisible) Hide();
                    else { Show(); WindowState = previousState; Activate(); _terminal.Focus(); }
                }
            }
        }
        private string SelectQrText(string[] texts)
        {
            using (var dialog = new Form { Text = "QR-Code – erkannten Inhalt prüfen", StartPosition = FormStartPosition.CenterParent, Size = new Size(760, 440), MinimumSize = new Size(500, 300), MinimizeBox = false, MaximizeBox = true })
            {
                var choices = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
                for (int i = 0; i < texts.Length; i++) choices.Items.Add("QR-Code " + (i + 1) + " von " + texts.Length);
                var preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
                var cancel = new Button { Text = "Abbrechen", AutoSize = true, DialogResult = DialogResult.Cancel };
                var send = new Button { Text = "An MeshTerminal übergeben", AutoSize = true, DialogResult = DialogResult.OK };
                var copy = new Button { Text = "Kopieren", AutoSize = true };
                copy.Click += (sender, e) => { try { Clipboard.SetText(texts[choices.SelectedIndex]); } catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Zwischenablage"); } };
                choices.SelectedIndexChanged += (sender, e) => preview.Text = texts[choices.SelectedIndex];
                choices.SelectedIndex = 0;
                buttons.Controls.Add(cancel); buttons.Controls.Add(send); buttons.Controls.Add(copy);
                dialog.Controls.Add(preview); dialog.Controls.Add(choices); dialog.Controls.Add(buttons);
                dialog.CancelButton = cancel; dialog.AcceptButton = cancel;
                return dialog.ShowDialog(this) == DialogResult.OK ? texts[choices.SelectedIndex] : null;
            }
        }
    }
}
