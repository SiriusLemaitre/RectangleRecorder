using System.Runtime.InteropServices;

namespace RectangleRecorder;

internal sealed class RecordingIndicator : Form
{
    private readonly Label time = new() { AutoSize = false, Text = "Starting…", TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label dot = new() { Text = "●", ForeColor = Color.FromArgb(255, 94, 116), TextAlign = ContentAlignment.MiddleCenter };
    private readonly PolishedButton stop = new() { Text = "■  Stop", BackColor = Color.FromArgb(174, 54, 73), ForeColor = Color.White };
    private bool drag;
    private Point anchor;
    internal bool ExcludedFromCapture { get; private set; }
    internal event Action? StopRequested;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; return p; }
    }

    internal RecordingIndicator()
    {
        Text = "Recording status";
        Font = new Font("Segoe UI", 10, FontStyle.Bold);
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(260, 48);
        BackColor = Color.FromArgb(25, 31, 43);
        ForeColor = Color.White;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        dot.SetBounds(5, 4, 30, 40);
        time.SetBounds(37, 4, 120, 40);
        stop.SetBounds(163, 6, 90, 36);
        stop.FlatStyle = FlatStyle.Flat;
        stop.Cursor = Cursors.Hand;
        stop.AccessibleName = "Stop recording";
        stop.Click += (_, _) => StopRequested?.Invoke();
        Controls.AddRange(new Control[] { dot, time, stop });
        foreach (var control in new Control[] { this, dot, time }) {
            control.Cursor = Cursors.SizeAll;
            control.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { drag = true; anchor = MousePosition; control.Capture = true; } };
            control.MouseMove += (_, _) => { if (!drag) return; var p = MousePosition; Location = new Point(Left + p.X - anchor.X, Top + p.Y - anchor.Y); anchor = p; };
            control.MouseUp += (_, _) => {
                drag = false; control.Capture = false;
                var work = Screen.FromPoint(MousePosition).WorkingArea;
                Location = new Point(Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width)), Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height)));
            };
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Appearance.DarkWindow(Handle);
        ExcludedFromCapture = Native.SetWindowDisplayAffinity(Handle, 0x11);
    }

    internal void ShowFor(Rectangle capture)
    {
        var monitor = Screen.FromPoint(new Point(capture.Left + capture.Width / 2, capture.Top + capture.Height / 2));
        var work = monitor.WorkingArea;
        // Prefer a corner outside the capture; exclusion also covers fullscreen capture.
        var candidates = new[] {
            new Point(work.Right - Width - 16, work.Top + 16), new Point(work.Left + 16, work.Top + 16),
            new Point(work.Right - Width - 16, work.Bottom - Height - 16), new Point(work.Left + 16, work.Bottom - Height - 16)
        };
        Location = candidates.FirstOrDefault(p => !capture.IntersectsWith(new Rectangle(p, Size)), candidates[0]);
        Show();
    }

    internal void SetElapsed(TimeSpan elapsed) { time.Text = $"REC  {elapsed:hh\\:mm\\:ss}"; stop.Enabled = true; }
    internal void SetSaving() { time.Text = "Saving…"; stop.Enabled = false; dot.ForeColor = Color.FromArgb(255, 197, 99); }
}
