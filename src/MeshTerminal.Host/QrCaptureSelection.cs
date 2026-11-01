using System;
using System.Drawing;
using System.Windows.Forms;
namespace MeshTerminal.Host
{
    internal sealed class QrCaptureSelection : Form
    {
        private readonly Bitmap _screen;
        private Point _start;
        private bool _dragging;
        internal Rectangle Selection { get; private set; }
        internal QrCaptureSelection(Bitmap screen, Rectangle bounds)
        {
            _screen = screen; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; Bounds = bounds;
            TopMost = true; ShowInTaskbar = false; KeyPreview = true;
            Cursor = Cursors.Cross; DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
            KeyDown += (sender, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(_screen, 0, 0);
            using (var dim = new SolidBrush(Color.FromArgb(110, Color.Black))) e.Graphics.FillRectangle(dim, ClientRectangle);
            if (Selection.Width > 0 && Selection.Height > 0)
            {
                e.Graphics.DrawImage(_screen, Selection, Selection, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.Yellow, 2)) e.Graphics.DrawRectangle(pen, Selection);
            }
            using (var background = new SolidBrush(Color.Black)) e.Graphics.FillRectangle(background, 10, 10, 550, 30);
            e.Graphics.DrawString("QR-Bereich mit der Maus markieren. Esc: Abbrechen", SystemFonts.MessageBoxFont, Brushes.White, 18, 17);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _start = e.Location; Selection = Rectangle.Empty; _dragging = true; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (!_dragging) return;
            Selection = Rectangle.Intersect(ClientRectangle, Rectangle.FromLTRB(Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y), Math.Max(_start.X, e.X), Math.Max(_start.Y, e.Y)));
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (!_dragging || e.Button != MouseButtons.Left) return;
            OnMouseMove(e); _dragging = false; Capture = false;
            if (Selection.Width < 8 || Selection.Height < 8) { Invalidate(); return; }
            DialogResult = DialogResult.OK; Close();
        }
    }
}
