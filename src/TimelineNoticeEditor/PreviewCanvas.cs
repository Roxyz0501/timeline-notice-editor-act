using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
namespace TimelineNoticeEditor
{
    internal sealed class PreviewCanvas : Control
    {
        private Bitmap bitmap;
        private float zoom = 1, originX, originY;
        private Point last;
        private bool dragging;
        internal double X, Y;
        internal string Hint = "";
        internal bool DetailView;
        internal event Action<double, double> Moved;
        internal PreviewCanvas() { DoubleBuffered = true; BackColor = Color.FromArgb(231, 237, 244); Dock = DockStyle.Fill; TabStop = true; ResizeRedraw = true; }
        internal void SetImage(BitmapSource image)
        {
            bitmap?.Dispose(); bitmap = null;
            if (image != null) using (var stream = new MemoryStream()) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream); stream.Position = 0; using (var decoded = new Bitmap(stream)) bitmap = new Bitmap(decoded); }
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var dpi = g.DpiX / 96f;
            var screen = SystemInformation.VirtualScreen;
            var scene = new RectangleF(screen.X / dpi, screen.Y / dpi, screen.Width / dpi, screen.Height / dpi);
            if (bitmap != null) scene = RectangleF.Union(scene, new RectangleF((float)X, (float)Y, bitmap.Width, bitmap.Height));
            if (DetailView && bitmap != null) scene = new RectangleF((float)X - 20, (float)Y - 20, bitmap.Width + 40, bitmap.Height + 40);
            if (!dragging)
            {
                zoom = Math.Max(.001f, Math.Min(Math.Max(1, Width - 32) / scene.Width, Math.Max(1, Height - 44) / scene.Height));
                originX = 16 - scene.Left * zoom; originY = 16 - scene.Top * zoom;
            }
            foreach (var monitor in Screen.AllScreens)
            {
                var b = monitor.Bounds;
                var r = new RectangleF(originX + b.X / dpi * zoom, originY + b.Y / dpi * zoom, b.Width / dpi * zoom, b.Height / dpi * zoom);
                using (var brush = new SolidBrush(Color.FromArgb(52, 63, 79))) g.FillRectangle(brush, r);
                g.DrawRectangle(Pens.SlateGray, r.X, r.Y, r.Width, r.Height);
            }
            if (bitmap != null)
            {
                var r = ImageRect(); g.DrawImage(bitmap, r);
                using (var pen = new Pen(Color.FromArgb(218, 164, 60), 2)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
            using (var brush = new SolidBrush(BackColor)) g.FillRectangle(brush, 0, Height - 28, Width, 28);
            TextRenderer.DrawText(g, Hint, Font, new Rectangle(8, Height - 25, Math.Max(1, Width - 16), 24), Color.FromArgb(48, 61, 80), TextFormatFlags.EndEllipsis);
        }
        private RectangleF ImageRect() => new RectangleF(originX + (float)X * zoom, originY + (float)Y * zoom, (bitmap?.Width ?? 0) * zoom, (bitmap?.Height ?? 0) * zoom);
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button == MouseButtons.Left && bitmap != null && ImageRect().Contains(e.Location)) { dragging = Capture = true; last = e.Location; } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (!dragging) return; X += (e.X - last.X) / zoom; Y += (e.Y - last.Y) / zoom; last = e.Location; Moved?.Invoke(Math.Round(X, 1), Math.Round(Y, 1)); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = Capture = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) == Keys.Left || (keyData & Keys.KeyCode) == Keys.Right || (keyData & Keys.KeyCode) == Keys.Up || (keyData & Keys.KeyCode) == Keys.Down || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e); if (bitmap == null) return; int step = e.Shift ? 10 : 1;
            if (e.KeyCode == Keys.Left) X -= step; else if (e.KeyCode == Keys.Right) X += step; else if (e.KeyCode == Keys.Up) Y -= step; else if (e.KeyCode == Keys.Down) Y += step; else return;
            e.Handled = true; Moved?.Invoke(X, Y); Invalidate();
        }
        protected override void Dispose(bool disposing) { if (disposing) bitmap?.Dispose(); base.Dispose(disposing); }
    }
}
