using System.Drawing.Drawing2D;

namespace RectangleRecorder;

internal sealed class AreaSelector : Form
{
    private readonly Rectangle desktop;
    private readonly Bitmap screenshot;
    private Point? anchor;
    private Rectangle selection;
    public Rectangle SelectedArea { get; private set; }

    public AreaSelector()
    {
        desktop = SystemInformation.VirtualScreen;
        Text = "Select recording area";
        screenshot = new Bitmap(desktop.Width, desktop.Height);
        using (var g = Graphics.FromImage(screenshot))
            g.CopyFromScreen(desktop.Location, Point.Empty, desktop.Size, CopyPixelOperation.SourceCopy);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = desktop;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;
        AccessibleName = "Select recording area";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Reapply physical virtual-desktop coordinates after initial DPI negotiation.
        Bounds = desktop;
        Activate();
    }

    internal static Rectangle Between(Point a, Point b) => Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; return; }
        if (e.Button != MouseButtons.Left) return;
        anchor = e.Location;
        selection = Rectangle.Empty;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (anchor is not { } start) return;
        var point = e.Location;
        point.X = Math.Clamp(point.X, 0, desktop.Width);
        point.Y = Math.Clamp(point.Y, 0, desktop.Height);
        selection = Between(start, point);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || anchor == null) return;
        OnMouseMove(e);
        Capture = false;
        if (selection.Width < 2 || selection.Height < 2) { anchor = null; Invalidate(); return; }
        SelectedArea = new Rectangle(selection.X + desktop.X, selection.Y + desktop.Y, selection.Width, selection.Height);
        DialogResult = DialogResult.OK;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(screenshot, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(155, 10, 15, 24));
        g.FillRectangle(shade, ClientRectangle);
        if (selection.Width > 0 && selection.Height > 0)
        {
            g.DrawImage(screenshot, selection, selection, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.FromArgb(86, 185, 255), 2);
            g.DrawRectangle(pen, selection);
        }
        var message = selection.Width > 0 ? $"{selection.Width} × {selection.Height} px  ·  Release to select" : "Drag to select an area  ·  Esc or right-click to cancel";
        using var font = new Font("Segoe UI", 12, FontStyle.Bold);
        var size = TextRenderer.MeasureText(message, font);
        var monitor = Screen.FromPoint(MousePosition).Bounds;
        var x = Math.Clamp(MousePosition.X - desktop.X + 18, monitor.Left - desktop.X + 8, monitor.Right - desktop.X - size.Width - 28);
        var y = Math.Clamp(MousePosition.Y - desktop.Y + 24, monitor.Top - desktop.Y + 8, monitor.Bottom - desktop.Y - size.Height - 28);
        var box = new Rectangle(x, y, size.Width + 20, size.Height + 16);
        using var background = new SolidBrush(Color.FromArgb(245, 20, 27, 39));
        g.FillRectangle(background, box);
        TextRenderer.DrawText(g, message, font, new Point(x + 10, y + 8), Color.White);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) screenshot.Dispose();
        base.Dispose(disposing);
    }
}
