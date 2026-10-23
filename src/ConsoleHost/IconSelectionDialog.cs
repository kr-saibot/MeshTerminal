using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Meshtastic.ConsoleHost
{
    internal sealed class IconSelectionDialog : Form
    {
        private readonly List<Image> _previews = new List<Image>();
        public string SelectedIconId { get; private set; }

        public IconSelectionDialog(string selectedIconId)
        {
            SelectedIconId = selectedIconId;
            Text = "Programm-Icon auswählen";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(760, 550);

            var table = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
            foreach (var choice in ProgramIcons.Choices()) AddChoice(table, choice.Key, choice.Value);
            Controls.Add(table);
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 70 };
            var hint = new Label { Text = "MC = MeshCore     MT = Meshtastic\r\nEigene .ico-Dateien: Host\\assets\\icons (Auswahl erneut öffnen)", AutoSize = true, Location = new Point(14, 10) };
            footer.Controls.Add(hint);
            var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(644, 20), Size = new Size(100, 28), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            footer.Controls.Add(cancel);
            Controls.Add(footer);
            CancelButton = cancel;
        }

        private void AddChoice(FlowLayoutPanel table, string id, string title)
        {
            Image preview;
            using (var icon = ProgramIcons.Load(id)) preview = icon.ToBitmap();
            _previews.Add(preview);
            var button = new Button
            {
                Text = title,
                Image = preview,
                TextImageRelation = TextImageRelation.ImageAboveText,
                Size = new Size(112, 92),
                FlatStyle = FlatStyle.Flat,
                BackColor = id == SelectedIconId ? SystemColors.Highlight : SystemColors.Control,
                ForeColor = id == SelectedIconId ? SystemColors.HighlightText : SystemColors.ControlText,
                AccessibleName = title
            };
            button.Click += delegate { SelectedIconId = id; DialogResult = DialogResult.OK; Close(); };
            table.Controls.Add(button);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) foreach (var image in _previews) image.Dispose();
        }
    }
}
