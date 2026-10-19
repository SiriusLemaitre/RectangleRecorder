using System.Drawing.Drawing2D;

namespace RectangleRecorder;

internal static class Appearance
{
    internal static GraphicsPath Round(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
    internal static void DarkWindow(IntPtr handle)
    {
        int enabled = 1, corners = 2;
        Native.DwmSetWindowAttribute(handle, 20, ref enabled, 4);
        Native.DwmSetWindowAttribute(handle, 33, ref corners, 4);
    }
}

internal sealed class PolishedButton : Button
{
    private bool hover;
    public PolishedButton() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(18, 23, 33));
        using var shape = Appearance.Round(new Rectangle(1, 1, Width - 3, Height - 3), 9);
        var color = !Enabled ? Color.FromArgb(34, 42, 55) : hover ? ControlPaint.Light(BackColor, .12f) : BackColor;
        using var brush = new SolidBrush(color);
        e.Graphics.FillPath(brush, shape);
        if (Focused && Enabled) { using var pen = new Pen(Color.FromArgb(160, 205, 255)); e.Graphics.DrawPath(pen, shape); }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(105, 119, 139), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class Card : Panel
{
    public Card() { DoubleBuffered = true; BackColor = Color.FromArgb(18, 23, 33); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Appearance.Round(new Rectangle(1, 1, Width - 3, Height - 3), 12);
        using var fill = new SolidBrush(Color.FromArgb(30, 38, 52));
        using var border = new Pen(Color.FromArgb(46, 58, 77));
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }
}

internal sealed class DarkComboBox : ComboBox
{
    public DarkComboBox() { DrawMode = DrawMode.OwnerDrawFixed; BackColor = Color.FromArgb(39, 49, 65); ForeColor = Color.White; FlatStyle = FlatStyle.Flat; ItemHeight = 25; }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var fill = new SolidBrush(selected ? Color.FromArgb(49, 76, 114) : BackColor);
        e.Graphics.FillRectangle(fill, e.Bounds);
        if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -8, 0), Enabled ? ForeColor : Color.FromArgb(120, 135, 155), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}
