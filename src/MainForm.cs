using System.Diagnostics;
using System.Text.Json;

namespace RectangleRecorder;

internal sealed class Preferences
{
    public int Fps { get; set; } = 30;
    public int Crf { get; set; } = 18;
    public bool Cursor { get; set; } = true;
    public bool Countdown { get; set; } = true;
    public bool DesktopAudio { get; set; } = true;
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Rectangle Recorder");
}

internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(18, 23, 33);
    private static readonly Color Surface = Color.FromArgb(30, 38, 52);
    private static readonly Color Muted = Color.FromArgb(159, 174, 195);
    private static readonly Color Blue = Color.FromArgb(71, 151, 245);
    private readonly Label areaLabel = new();
    private readonly Label status = new();
    private readonly Label fileLabel = new();
    private readonly Label qualityHelp = new();
    private readonly Label shortcutHint = new();
    private readonly NumericUpDown fps = new() { Minimum = 1, Maximum = 120 };
    private readonly NumericUpDown crf = new() { Minimum = 0, Maximum = 51 };
    private readonly ComboBox quality = new DarkComboBox() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox cursor = new() { Text = "Include mouse cursor", AutoSize = true };
    private readonly CheckBox countdown = new() { Text = "3-second countdown", AutoSize = true };
    private readonly CheckBox desktopAudio = new() { Text = "Desktop audio (default playback device)", AutoSize = true };
    private readonly TextBox folder = new() { ReadOnly = true };
    private readonly Button select = new PolishedButton();
    private readonly Button start = new PolishedButton();
    private readonly Button stop = new PolishedButton();
    private readonly Button browse = new PolishedButton();
    private readonly Button openFolder = new PolishedButton();
    private readonly Button openFile = new PolishedButton();
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem trayStop = new("Stop recording");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Stopwatch elapsed = new();
    private readonly List<Control> settingsControls = new();
    private readonly string preferencesPath;
    private Rectangle? area;
    private Recorder? recorder;
    private string? output;
    private string? lastFile;
    private CancellationTokenSource? countdownCancellation;
    private bool busy;
    private bool recording;
    private bool closeAfterStop;
    private bool allowClose;
    private bool hotkeyRegistered;
    private bool exitRequested;
    private readonly bool hosted;
    private RecordingIndicator? indicator;
    private RecordingFrame? recordingFrame;
    private readonly Label stateBadge = new();
    private readonly ToolTip tips = new();

    internal bool IsRecording => recording;
    internal bool ShortcutAvailable => hotkeyRegistered;
    internal RecordingIndicator? Indicator => indicator;
    internal RecordingFrame? Frame => recordingFrame;

    public MainForm(string? preferencesFile = null, bool hosted = false)
    {
        this.hosted = hosted;
        preferencesPath = preferencesFile ?? Path.Combine(AppContext.BaseDirectory, "settings.json");
        Text = "Rectangle Recorder";
        Font = new Font("Segoe UI", 10);
        BackColor = Background;
        ForeColor = Color.White;
        ClientSize = new Size(640, 620);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        Icon = MakeIcon();

        var heading = LabelAt("Rectangle Recorder", 28, 24, 584, 38, 23, Color.White, FontStyle.Bold);
        Controls.Add(heading);
        Controls.Add(LabelAt("Select an area. Set the quality. Hit record.", 30, 68, 570, 26, 10, Muted));
        var areaPanel = new Panel { Bounds = new Rectangle(28, 111, 584, 82), BackColor = Surface };
        areaPanel.Controls.Add(LabelAt("RECORDING AREA", 16, 10, 330, 23, 9, Muted, FontStyle.Bold));
        areaLabel.SetBounds(16, 35, 365, 30);
        areaLabel.Text = "No area selected";
        areaLabel.Font = new Font(Font, FontStyle.Bold);
        areaPanel.Controls.Add(areaLabel);
        StyleButton(select, "Select area…", new Rectangle(408, 20, 158, 42), Blue);
        areaPanel.Controls.Add(select);
        Controls.Add(areaPanel);

        Controls.Add(LabelAt("Frame rate", 28, 212, 140, 25, 10, Muted));
        fps.SetBounds(28, 243, 135, 32);
        Controls.Add(fps);
        Controls.Add(LabelAt("fps", 171, 246, 35, 25, 10, Muted));
        Controls.Add(LabelAt("Quality", 228, 212, 140, 25, 10, Muted));
        quality.SetBounds(228, 243, 230, 32);
        quality.Items.AddRange(new object[] { "High quality", "Balanced", "Smaller files", "Custom" });
        Controls.Add(quality);
        Controls.Add(LabelAt("CRF", 482, 212, 130, 25, 10, Muted));
        crf.SetBounds(482, 243, 130, 32);
        Controls.Add(crf);
        qualityHelp.SetBounds(28, 286, 584, 37);
        qualityHelp.ForeColor = Muted;
        qualityHelp.Font = new Font("Segoe UI", 9);
        Controls.Add(qualityHelp);
        cursor.Location = new Point(28, 332);
        countdown.Location = new Point(314, 332);
        Controls.AddRange(new Control[] { cursor, countdown });
        desktopAudio.Location = new Point(28, 358);
        Controls.Add(desktopAudio);

        Controls.Add(LabelAt("Save recordings to", 28, 379, 500, 24, 10, Muted));
        folder.SetBounds(28, 410, 470, 32);
        folder.BackColor = Surface;
        folder.ForeColor = Color.White;
        folder.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(folder);
        StyleButton(browse, "Browse…", new Rectangle(510, 407, 102, 36), Surface);
        Controls.Add(browse);
        StyleButton(openFolder, "Open folder", new Rectangle(494, 375, 118, 30), Surface);
        Controls.Add(openFolder);
        StyleButton(start, "●  Record", new Rectangle(28, 470, 280, 48), Blue);
        StyleButton(stop, "■  Stop", new Rectangle(324, 470, 288, 48), Color.FromArgb(173, 55, 68));
        stop.Enabled = false;
        Controls.AddRange(new Control[] { start, stop });
        status.SetBounds(28, 532, 584, 25);
        status.ForeColor = Muted;
        status.Text = "Ready · MP4 / H.264 + optional desktop audio";
        Controls.Add(status);
        fileLabel.SetBounds(28, 560, 455, 23);
        fileLabel.AutoEllipsis = true;
        fileLabel.ForeColor = Muted;
        fileLabel.Font = new Font("Segoe UI", 9);
        Controls.Add(fileLabel);
        StyleButton(openFile, "Open video", new Rectangle(494, 554, 118, 30), Surface);
        openFile.Visible = false;
        Controls.Add(openFile);
        shortcutHint.SetBounds(28, 592, 584, 23);
        shortcutHint.ForeColor = Muted;
        shortcutHint.Font = new Font("Segoe UI", 9);
        Controls.Add(shortcutHint);
        settingsControls.AddRange(new Control[] { select, fps, quality, crf, cursor, countdown, desktopAudio, browse });

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show recorder", null, (_, _) => RestoreWindow());
        menu.Items.Add("Select recording area", null, async (_, _) => await ToggleRecordingAsync());
        menu.Items.Add(trayStop);
        menu.Items.Add(new ToolStripSeparator());
        var startup = new ToolStripMenuItem("Start with Windows") { Checked = WindowsStartup.IsEnabled };
        startup.Click += (_, _) => {
            try { WindowsStartup.SetEnabled(!startup.Checked); startup.Checked = WindowsStartup.IsEnabled; }
            catch (Exception e) { ShowError("Could not update Windows startup: " + e.Message); }
        };
        menu.Opening += (_, _) => startup.Checked = WindowsStartup.IsEnabled;
        menu.Items.Add(startup);
        menu.Items.Add("Exit", null, (_, _) => RequestExit());
        tray = new NotifyIcon { Icon = Icon, Text = ShortcutSettings.TrayText, ContextMenuStrip = menu, Visible = !hosted };
        tray.DoubleClick += (_, _) => RestoreWindow();
        trayStop.Enabled = false;
        trayStop.Click += async (_, _) => await StopRecordingAsync();
        select.Click += async (_, _) => await SelectAreaAsync();
        start.Click += async (_, _) => await StartRecordingAsync();
        stop.Click += async (_, _) => await StopRecordingAsync();
        browse.Click += (_, _) => {
            using var dialog = new FolderBrowserDialog { Description = "Choose where to save recordings", UseDescriptionForTitle = true, SelectedPath = folder.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) { folder.Text = dialog.SelectedPath; SavePreferences(); }
        };
        openFile.Click += (_, _) => { if (lastFile != null) OpenPath(lastFile); };
        openFolder.Click += (_, _) => {
            try {
                Directory.CreateDirectory(folder.Text);
                OpenPath(folder.Text);
            }
            catch (Exception e) { ShowError(e.Message); }
        };
        quality.SelectedIndexChanged += (_, _) => {
            if (quality.SelectedIndex < 3) crf.Value = new[] { 18, 23, 28 }[quality.SelectedIndex];
            UpdateQuality();
        };
        crf.ValueChanged += (_, _) => {
            if (quality.SelectedIndex < 3 && (int)crf.Value != new[] { 18, 23, 28 }[Math.Max(0, quality.SelectedIndex)]) quality.SelectedIndex = 3;
            UpdateQuality();
        };
        timer.Tick += async (_, _) => {
            if (!recording) return;
            if (recorder is { IsRunning: false }) { await StopRecordingAsync(); return; }
            var size = output != null && File.Exists(output) ? new FileInfo(output).Length / 1048576d : 0;
            status.Text = $"Recording  {elapsed.Elapsed:hh\\:mm\\:ss}  ·  {size:0.0} MB  ·  {fps.Value} fps";
            tray.Text = $"Recording · {elapsed.Elapsed:hh\\:mm\\:ss}";
            indicator?.SetElapsed(elapsed.Elapsed);
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) countdownCancellation?.Cancel(); };
        FormClosing += OnClosing;
        LoadPreferences();
        start.Enabled = false;
        ApplyPolish();
    }

    private void ApplyPolish()
    {
        ClientSize = new Size(680, 694);
        DoubleBuffered = true;
        foreach (var heading in Controls.OfType<Label>().Where(l => l.Text == "Rectangle Recorder")) {
            heading.SetBounds(28, 23, 490, 40);
            heading.Font = new Font("Segoe UI", 22, FontStyle.Bold);
        }
        var subtitle = Controls.OfType<Label>().Single(l => l.Text.StartsWith("Select an area."));
        subtitle.Text = "Your screen, your settings. Start recording when you're ready.";
        subtitle.SetBounds(30, 70, 620, 26);
        select.Parent!.Bounds = new Rectangle(28, 112, 624, 82);
        select.SetBounds(450, 20, 158, 42);
        areaLabel.Width = 425;
        void MoveLabel(string text, Rectangle bounds) => Controls.OfType<Label>().Single(l => l.Text == text).Bounds = bounds;
        MoveLabel("Frame rate", new Rectangle(44, 230, 140, 25));
        MoveLabel("fps", new Rectangle(162, 266, 35, 25));
        MoveLabel("Quality", new Rectangle(236, 230, 140, 25));
        MoveLabel("CRF", new Rectangle(510, 230, 120, 25));
        fps.SetBounds(44, 262, 110, 30);
        quality.SetBounds(236, 262, 250, 32);
        crf.SetBounds(510, 262, 125, 30);
        foreach (var numeric in new[] { fps, crf }) { numeric.BackColor = Color.FromArgb(39, 49, 65); numeric.ForeColor = Color.White; numeric.BorderStyle = BorderStyle.FixedSingle; }
        qualityHelp.SetBounds(44, 307, 586, 36);
        cursor.Location = new Point(44, 375);
        countdown.Location = new Point(350, 375);
        desktopAudio.Location = new Point(44, 406);
        MoveLabel("Save recordings to", new Rectangle(44, 463, 450, 24));
        openFolder.SetBounds(516, 459, 118, 30);
        folder.SetBounds(44, 497, 470, 28);
        folder.Font = new Font("Segoe UI", 9);
        browse.SetBounds(528, 492, 106, 38);
        start.Text = "●  Start recording";
        start.SetBounds(28, 558, 302, 48);
        stop.SetBounds(346, 558, 306, 48);
        start.Font = stop.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        status.SetBounds(30, 620, 620, 25);
        fileLabel.SetBounds(30, 648, 460, 23);
        openFile.SetBounds(534, 645, 118, 30);
        shortcutHint.SetBounds(30, 674, 620, 20);
        shortcutHint.Font = new Font("Segoe UI", 8.5f);
        stateBadge.SetBounds(548, 34, 104, 28);
        stateBadge.Text = "READY";
        stateBadge.TextAlign = ContentAlignment.MiddleCenter;
        stateBadge.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        stateBadge.ForeColor = Color.FromArgb(139, 192, 255);
        stateBadge.BackColor = Color.FromArgb(30, 47, 69);
        Controls.Add(stateBadge);
        foreach (var bounds in new[] { new Rectangle(28, 214, 624, 140), new Rectangle(28, 362, 624, 84), new Rectangle(28, 454, 624, 90) }) {
            var card = new Card { Bounds = bounds };
            var contents = Controls.Cast<Control>().Where(c => bounds.Contains(c.Bounds)).ToArray();
            Controls.Add(card);
            foreach (var content in contents) {
                var position = content.Location;
                card.Controls.Add(content);
                content.Location = new Point(position.X - bounds.X, position.Y - bounds.Y);
                if (content is Label or CheckBox) content.BackColor = Surface;
            }
        }
        foreach (var option in new[] { cursor, countdown, desktopAudio }) option.BackColor = Surface;
        folder.Name = "OutputFolder";
        countdown.Name = "Countdown";
        desktopAudio.Name = "DesktopAudio";
        tips.SetToolTip(select, "Drag a rectangle. Selection never starts recording automatically.");
        tips.SetToolTip(start, "Start recording the selected area using these settings.");
        tips.SetToolTip(desktopAudio, "Include sound from your default speakers or headphones. Microphone is excluded.");
        tips.SetToolTip(crf, "Lower CRF = higher quality and larger files. Changing fps does not change the selected quality.");
        tips.SetToolTip(folder, folder.Text);
        tips.SetToolTip(openFolder, "Open the recording folder in File Explorer.");
        AccessibleDescription = "Select an area, adjust video and audio settings, then click Start recording.";
    }

    private static Label LabelAt(string text, int x, int y, int width, int height, float size, Color color, FontStyle style = FontStyle.Regular) =>
        new() { Text = text, Bounds = new Rectangle(x, y, width, height), ForeColor = color, Font = new Font("Segoe UI", size, style) };

    private static void StyleButton(Button button, string text, Rectangle bounds, Color color)
    {
        button.Text = text;
        button.Bounds = bounds;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    private static Icon MakeIcon()
    {
        using var image = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(image))
        {
            g.Clear(Background);
            using var pen = new Pen(Blue, 3);
            g.DrawRectangle(pen, 4, 6, 24, 20);
            using var brush = new SolidBrush(Color.FromArgb(248, 85, 102));
            g.FillEllipse(brush, 11, 11, 10, 10);
        }
        var handle = image.GetHicon();
        try { using var original = Icon.FromHandle(handle); return (Icon)original.Clone(); }
        finally { Native.DestroyIcon(handle); }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        hotkeyRegistered = hosted || Native.RegisterHotKey(Handle, 1, ShortcutSettings.Modifiers[ShortcutSettings.Preset], (uint)ShortcutSettings.Keys[ShortcutSettings.Preset]);
        UpdateShortcutHint();
        Appearance.DarkWindow(Handle);
        Native.SetWindowDisplayAffinity(Handle, 0x11);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (!hosted) Native.UnregisterHotKey(Handle, 1);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if ((m.Msg == 0x0312 && m.WParam == (IntPtr)1) || m.Msg == 0x8310)
        {
            _ = ToggleRecordingAsync();
            return;
        }
        if (m.Msg == 0x8311) { RestoreWindow(); return; }
        if (m.Msg == 0x8312) { _ = StopRecordingAsync(); return; }
        if (m.Msg == 0x8313) { RequestExit(); return; }
        if (m.Msg == 0x8314) { UpdateShortcutHint(); return; }
        base.WndProc(ref m);
    }

    private void UpdateQuality()
    {
        qualityHelp.Text = $"Original resolution · MP4 / H.264\nCRF {crf.Value} · Lower values give sharper video and larger files.";
    }

    private void UpdateShortcutHint()
    {
        shortcutHint.Text = hotkeyRegistered ? $"Select / stop: {ShortcutSettings.Label}  ·  Recording starts only with the button" : "Shortcut unavailable · Choose another shortcut in the tray menu";
        if (tray != null && !recording) tray.Text = ShortcutSettings.TrayText;
    }

    private void LoadPreferences()
    {
        Preferences prefs;
        try { prefs = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(preferencesPath)) ?? new(); }
        catch { prefs = new(); }
        fps.Value = Math.Clamp(prefs.Fps, 1, 120);
        quality.SelectedIndex = 3;
        crf.Value = Math.Clamp(prefs.Crf, 0, 51);
        quality.SelectedIndex = prefs.Crf switch { 18 => 0, 23 => 1, 28 => 2, _ => 3 };
        cursor.Checked = prefs.Cursor;
        countdown.Checked = prefs.Countdown;
        desktopAudio.Checked = prefs.DesktopAudio;
        folder.Text = string.IsNullOrWhiteSpace(prefs.Folder) ? new Preferences().Folder : prefs.Folder;
        UpdateQuality();
    }

    private void SavePreferences()
    {
        try {
            File.WriteAllText(preferencesPath, JsonSerializer.Serialize(new Preferences {
                Fps = (int)fps.Value, Crf = (int)crf.Value, Cursor = cursor.Checked,
                Countdown = countdown.Checked, DesktopAudio = desktopAudio.Checked, Folder = folder.Text
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            status.Text = "Settings could not be saved; keep the app in a writable folder.";
        }
    }

    internal async Task ToggleRecordingAsync()
    {
        if (countdownCancellation != null) { countdownCancellation.Cancel(); return; }
        if (recording) { await StopRecordingAsync(); return; }
        if (busy) return;
        await SelectAreaAsync(restoreOnCancel: false);
    }

    private async Task<bool> SelectAreaAsync(bool restoreOnCancel = true)
    {
        if (busy || recording) return false;
        var wasVisible = Visible;
        var selected = false;
        busy = true;
        Hide();
        try {
            await Task.Delay(220);
            using var selector = new AreaSelector();
            if (selector.ShowDialog() == DialogResult.OK) {
                area = selector.SelectedArea;
                areaLabel.Text = $"{area.Value.Width} × {area.Value.Height} px  at  {area.Value.X}, {area.Value.Y}";
                status.Text = "Area selected · Click Start recording when you're ready";
                stateBadge.Text = "READY";
                start.Focus();
                selected = true;
            }
        }
        catch (Exception e) { ShowError(e.Message); }
        finally {
            busy = false;
            if (selected || wasVisible || restoreOnCancel) RestoreWindow();
            start.Enabled = area.HasValue;
            if (hosted && !selected && !wasVisible && !restoreOnCancel) BeginInvoke(RequestExit);
        }
        return selected;
    }

    private void SetRecordingControls(bool active)
    {
        foreach (var control in settingsControls) control.Enabled = !active;
        start.Enabled = !active && area.HasValue;
        stop.Enabled = recording;
        trayStop.Enabled = recording;
    }

    private async Task StartRecordingAsync()
    {
        if (busy || recording || area is not { } selected) return;
        if (!SystemInformation.VirtualScreen.Contains(selected)) { ShowError("The display layout changed. Please select the area again."); return; }
        busy = true;
        SetRecordingControls(true);
        openFile.Visible = false;
        SavePreferences();
        countdownCancellation = new CancellationTokenSource();
        try {
            Directory.CreateDirectory(folder.Text);
            output = Path.Combine(folder.Text, $"Recording-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}-{Guid.NewGuid().ToString("N")[..6]}.mp4");
            if (countdown.Checked)
                for (var i = 3; i > 0; i--) {
                    status.Text = $"Recording starts in {i}…  Esc to cancel";
                    await Task.Delay(1000, countdownCancellation.Token);
                }
            countdownCancellation.Token.ThrowIfCancellationRequested();
            Hide();
            tray.Visible = !hosted;
            await Task.Delay(200);
            status.Text = "Starting encoder…";
            stateBadge.Text = "STARTING";
            indicator = new RecordingIndicator();
            indicator.StopRequested += () => { countdownCancellation?.Cancel(); _ = StopRecordingAsync(); };
            indicator.ShowFor(selected);
            recordingFrame = new RecordingFrame(selected);
            recordingFrame.Show();
            recorder = new Recorder();
            await recorder.StartAsync(new RecordingOptions(selected, (int)fps.Value, (int)crf.Value, cursor.Checked, output, desktopAudio.Checked));
            elapsed.Restart();
            recording = true;
            indicator.SetElapsed(elapsed.Elapsed);
            stateBadge.Text = "RECORDING";
            stateBadge.ForeColor = Color.FromArgb(255, 123, 142);
            busy = false;
            SetRecordingControls(true);
            timer.Start();
            tray.Text = "Rectangle Recorder · Recording";
            if (countdownCancellation.IsCancellationRequested && !closeAfterStop) await StopRecordingAsync();
        }
        catch (OperationCanceledException) { status.Text = "Countdown cancelled · Ready"; RestoreWindow(); }
        catch (Exception e) {
            WriteErrorLog();
            recorder?.Dispose(); recorder = null;
            indicator?.Dispose(); indicator = null;
            recordingFrame?.Dispose(); recordingFrame = null;
            RestoreWindow();
            ShowError(e.Message);
        }
        finally {
            countdownCancellation.Dispose(); countdownCancellation = null;
            busy = false;
            if (!recording) { tray.Text = ShortcutSettings.TrayText; SetRecordingControls(false); if (indicator == null) stateBadge.Text = "READY"; }
            if (closeAfterStop) { if (recording) await StopRecordingAsync(); else { allowClose = true; Close(); } }
        }
    }

    private async Task StopRecordingAsync()
    {
        if (!recording || busy || recorder == null) return;
        busy = true;
        recording = false;
        recordingFrame?.Dispose(); recordingFrame = null;
        timer.Stop(); elapsed.Stop();
        stop.Enabled = false; trayStop.Enabled = false;
        status.Text = "Saving MP4…";
        stateBadge.Text = "SAVING";
        indicator?.SetSaving();
        RestoreWindow();
        try {
            var code = await recorder.StopAsync();
            if (code != 0 || output == null || !File.Exists(output) || new FileInfo(output).Length == 0)
                throw new IOException("The encoder could not complete the recording.\n\n" + recorder.ErrorLog);
            lastFile = output;
            fileLabel.Text = Path.GetFileName(lastFile);
            openFile.Visible = true;
            status.Text = $"Saved · {elapsed.Elapsed:hh\\:mm\\:ss} · {(new FileInfo(lastFile).Length / 1048576d):0.0} MB";
            status.ForeColor = Color.FromArgb(122, 215, 169);
        }
        catch (Exception e) { WriteErrorLog(); ShowError(e.Message); }
        finally {
            recorder.Dispose(); recorder = null;
            indicator?.Dispose(); indicator = null;
            stateBadge.Text = "READY";
            stateBadge.ForeColor = Color.FromArgb(139, 192, 255);
            tray.Text = ShortcutSettings.TrayText;
            busy = false;
            SetRecordingControls(false);
            if (closeAfterStop) { allowClose = true; Close(); }
        }
    }

    private void WriteErrorLog()
    {
        try { if (output != null && recorder != null) File.WriteAllText(output + ".log", recorder.ErrorLog); }
        catch { }
    }

    internal void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ShowError(string message)
    {
        status.Text = "Recording stopped · See error details";
        MessageBox.Show(this, message.Length > 6000 ? message[^6000..] : message, "Rectangle Recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { ShowError(e.Message); }
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (allowClose) return;
        if (!exitRequested && e.CloseReason == CloseReason.UserClosing) {
            if (hosted && !busy && !recording) { SavePreferences(); return; }
            e.Cancel = true;
            SavePreferences();
            Hide();
            return;
        }
        if (busy || recording) {
            e.Cancel = true;
            closeAfterStop = true;
            countdownCancellation?.Cancel();
            if (recording && !busy) await StopRecordingAsync();
            return;
        }
        SavePreferences();
    }

    internal void RequestExit() { exitRequested = true; Close(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); tips.Dispose(); recordingFrame?.Dispose(); indicator?.Dispose(); tray.Dispose(); recorder?.Dispose(); Icon?.Dispose(); }
        base.Dispose(disposing);
    }
}
