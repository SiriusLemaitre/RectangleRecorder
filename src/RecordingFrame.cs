namespace RectangleRecorder;

internal sealed class RecordingFrame : Form
{
    private readonly Rectangle capture;
    internal bool ExcludedFromCapture { get; private set; }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; return p; }
    }

    internal RecordingFrame(Rectangle capture)
    {
        this.capture = capture;
        Text = "Recording area outline";
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(255, 94, 116);
        // A layered, transparent-input window passes clicks to the desktop.
        Opacity = 0.99;
        Bounds = capture;
        UpdateOutline();
    }

    private void UpdateOutline()
    {
        var outline = new Region(new Rectangle(Point.Empty, Size));
        if (Width > 4 && Height > 4) outline.Exclude(new Rectangle(2, 2, Width - 4, Height - 4));
        var old = Region;
        Region = outline;
        old?.Dispose();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ExcludedFromCapture = Native.SetWindowDisplayAffinity(Handle, 0x11);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = capture;
        UpdateOutline();
    }
}
