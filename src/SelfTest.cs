using System.Diagnostics;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RectangleRecorder;

internal static class SelfTest
{
    public static async Task Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var report = new List<string>();
        try {
            void Check(bool condition, string message) {
                if (!condition) throw new Exception(message);
                report.Add("PASS " + message);
            }
            var expected = new Rectangle(-200, -100, 500, 350);
            Check(AreaSelector.Between(new(-200, -100), new(300, 250)) == expected, "Forward selection across negative coordinates");
            Check(AreaSelector.Between(new(300, 250), new(-200, -100)) == expected, "Reverse selection across negative coordinates");
            Check(AreaSelector.Between(new(-200, 250), new(300, -100)) == expected, "Upward selection");
            Check(AreaSelector.Between(new(300, -100), new(-200, 250)) == expected, "Downward selection");

            using (var form = new MainForm(Path.Combine(directory, "preview-settings.json"))) {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.ShowInTaskbar = false;
                form.Show();
                Native.SetWindowDisplayAffinity(form.Handle, 0);
                form.PerformLayout();
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(directory, "interface.png"));
                Check(form.Controls.Find("OutputFolder", true).Length == 1 && form.Controls.Find("DesktopAudio", true).Length == 1, "Native interface builds and renders");
                form.Close();
                Check(!form.IsDisposed && !form.Visible, "Closing the recorder hides it without ending the shortcut listener");
            }

            // WinForms installs a UI context when a form is created; these headless
            // encoder checks intentionally run without a UI message loop.
            SynchronizationContext.SetSynchronizationContext(null);

            await Task.Run(() => {
                var name = "test-" + Guid.NewGuid().ToString("N");
                using var primary = new SingleInstance(name);
                using var duplicate = new SingleInstance(name);
                Check(primary.IsPrimary && !duplicate.IsPrimary, "Only one background launcher owns each instance name");
                var received = new System.Collections.Concurrent.ConcurrentQueue<string>();
                primary.Listen(received.Enqueue);
                duplicate.SendAsync("toggle").GetAwaiter().GetResult();
                duplicate.SendAsync("show").GetAwaiter().GetResult();
                Check(received.ToArray().SequenceEqual(new[] { "toggle", "show" }), "Repeated launches forward selection and show commands to the resident app");
            });
            await CheckShortcutWorkflow(directory, Check);

            var desktop = SystemInformation.VirtualScreen;
            foreach (var rate in new[] { 15, 30, 60 }) {
                var file = Path.Combine(directory, $"capture-{rate}-{Guid.NewGuid():N}.mp4");
                using var recorder = new Recorder();
                await recorder.StartAsync(new RecordingOptions(new Rectangle(desktop.Left, desktop.Top, 321, 241), rate, rate == 60 ? 18 : 28, false, file));
                await Task.Delay(1200);
                Check(await recorder.StopAsync() == 0, $"Desktop capture and graceful stop at {rate} fps");
                File.WriteAllText(file + ".log", recorder.ErrorLog);
                using var metadata = JsonDocument.Parse(await Execute(Path.Combine(AppContext.BaseDirectory, "ffprobe.exe"), "-v", "error", "-show_streams", "-show_format", "-of", "json", file));
                var stream = metadata.RootElement.GetProperty("streams")[0];
                Check(stream.GetProperty("codec_name").GetString() == "h264", $"H.264 stream at {rate} fps");
                Check(stream.GetProperty("width").GetInt32() == 322 && stream.GetProperty("height").GetInt32() == 242, $"Odd selection padded correctly at {rate} fps");
                Check(stream.GetProperty("r_frame_rate").GetString() == $"{rate}/1", $"Output frame rate is {rate} fps");
                Check(double.Parse(metadata.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture) >= 1, $"Recording duration at {rate} fps");
                await Execute(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"), "-v", "error", "-i", file, "-f", "null", "-");
                report.Add($"PASS Full MP4 decode at {rate} fps");
            }

            var audioFile = Path.Combine(directory, $"desktop-audio-{Guid.NewGuid():N}.mp4");
            // Avoid 440 Hz: music playing on the desktop can contain a strong,
            // sustained A note and make a valid recording fail the gap check.
            const int testToneFrequency = 7331;
            using (var devices = new MMDeviceEnumerator())
            using (var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            using (var tone = new WasapiOut(device, AudioClientShareMode.Shared, false, 50))
            using (var recorder = new Recorder()) {
                tone.Init(new SignalGenerator(48000, 2) { Gain = 0.025, Frequency = testToneFrequency, Type = SignalGeneratorType.Sin }.ToWaveProvider());
                await recorder.StartAsync(new RecordingOptions(new Rectangle(desktop.Left, desktop.Top, 321, 241), 30, 23, true, audioFile, true));
                await Task.Delay(600);
                tone.Play();
                await Task.Delay(600);
                tone.Stop();
                await Task.Delay(600);
                tone.Play();
                await Task.Delay(600);
                tone.Stop();
                await Task.Delay(600);
                Check(await recorder.StopAsync() == 0, "Desktop audio capture and graceful stop");
                File.WriteAllText(audioFile + ".log", recorder.ErrorLog);
            }
            using (var metadata = JsonDocument.Parse(await Execute(Path.Combine(AppContext.BaseDirectory, "ffprobe.exe"), "-v", "error", "-show_streams", "-of", "json", audioFile))) {
                var streams = metadata.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                var sound = streams.Single(s => s.GetProperty("codec_type").GetString() == "audio");
                var video = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
                Check(sound.GetProperty("codec_name").GetString() == "aac" && sound.GetProperty("sample_rate").GetString() == "48000" && sound.GetProperty("channels").GetInt32() == 2,
                    "MP4 contains 48 kHz stereo AAC desktop audio");
                var audioDuration = double.Parse(sound.GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var videoDuration = double.Parse(video.GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                Check(audioDuration >= 3 && Math.Abs(audioDuration - videoDuration) < 0.3, "Audio preserves quiet gaps and matches video duration within 300 ms");
            }
            var pcmFile = audioFile + ".f32";
            await Execute(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"), "-v", "error", "-i", audioFile, "-map", "0:a:0", "-f", "f32le", "-ac", "1", "-y", pcmFile);
            var pcm = File.ReadAllBytes(pcmFile);
            var samples = new float[pcm.Length / 4];
            Buffer.BlockCopy(pcm, 0, samples, 0, pcm.Length);
            // Detect the injected frequency rather than relying on a silent desktop.
            var toneWindows = new List<int>();
            for (var start = 0; start + 4800 < samples.Length; start += 4800) {
                double sine = 0, cosine = 0;
                for (var i = 0; i < 4800; i++) {
                    var phase = 2 * Math.PI * testToneFrequency * i / 48000;
                    sine += samples[start + i] * Math.Sin(phase);
                    cosine += samples[start + i] * Math.Cos(phase);
                }
                if (2 * Math.Sqrt(sine * sine + cosine * cosine) / 4800 > 0.001) toneWindows.Add(start / 4800);
            }
            Check(toneWindows.Count >= 6, "Played test tone is present in decoded system-audio track");
            Check(toneWindows.Zip(toneWindows.Skip(1)).Any(pair => pair.Second - pair.First >= 4), "Two test-tone bursts remain separated on the audio timeline");
            var invalid = false;
            try { Recorder.Arguments(new RecordingOptions(new Rectangle(0, 0, 1, 1), 30, 20, true, "unused.mp4")); }
            catch (ArgumentException) { invalid = true; }
            Check(invalid, "Reject undersized selections");

            var protectedFile = Path.Combine(directory, "do-not-overwrite.mp4");
            File.WriteAllText(protectedFile, "keep me");
            var refused = false;
            using (var recorder = new Recorder()) {
                try { await recorder.StartAsync(new RecordingOptions(new Rectangle(desktop.Left, desktop.Top, 64, 64), 15, 23, true, protectedFile)); }
                catch (InvalidOperationException) { refused = true; }
            }
            Check(refused && File.ReadAllText(protectedFile) == "keep me", "Refuse to overwrite existing files and report encoder failure");
            report.Add("ALL CHECKS PASSED");
        }
        catch (Exception e) { report.Add("FAIL " + e); Environment.ExitCode = 1; }
        File.WriteAllLines(Path.Combine(directory, "report.txt"), report);
    }

    private static Task CheckShortcutWorkflow(string directory, Action<bool, string> check)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            try {
                using var instance = new SingleInstance("workflow-" + Guid.NewGuid().ToString("N"));
                using var form = new MainForm(Path.Combine(directory, "workflow-settings.json"));
                using var context = new LauncherContext(instance, "background", form);
                var recordings = Path.Combine(directory, "shortcut-recordings");
                Directory.CreateDirectory(recordings);
                ((TextBox)form.Controls.Find("OutputFolder", true).Single()).Text = recordings;
                ((CheckBox)form.Controls.Find("Countdown", true).Single()).Checked = false;
                ((CheckBox)form.Controls.Find("DesktopAudio", true).Single()).Checked = false;
                using var poll = new System.Windows.Forms.Timer { Interval = 50 };
                var stage = 0;
                var clock = Stopwatch.StartNew();
                long recordingAt = 0;
                void Shortcut() => Native.PostMessage(form.Handle, 0x0312, (IntPtr)1, IntPtr.Zero);
                poll.Tick += (_, _) => {
                    try {
                        if (clock.Elapsed.TotalSeconds > 25) throw new TimeoutException("Shortcut workflow timed out at stage " + stage);
                        var selector = Application.OpenForms.OfType<AreaSelector>().FirstOrDefault();
                        if (stage == 0) {
                            check(!form.Visible, "Background launch does not display the controls");
                            check(form.ShortcutAvailable, ShortcutSettings.Label + " successfully registers globally");
                            Shortcut(); stage = 1;
                        }
                        else if (stage == 1 && selector != null) {
                            check(true, "Shortcut opens rectangle selection from hidden background mode");
                            Native.PostMessage(selector.Handle, 0x0100, (IntPtr)Keys.Escape, IntPtr.Zero);
                            stage = 2;
                        }
                        else if (stage == 2 && selector == null) {
                            check(!form.Visible && !form.IsRecording, "Cancelling selection returns to the background without recording");
                            Shortcut(); stage = 3;
                        }
                        else if (stage == 3 && selector != null) {
                            static IntPtr XY(int x, int y) => (IntPtr)((y << 16) | x);
                            Native.PostMessage(selector.Handle, 0x0201, (IntPtr)1, XY(32, 32));
                            Native.PostMessage(selector.Handle, 0x0200, (IntPtr)1, XY(353, 273));
                            Native.PostMessage(selector.Handle, 0x0202, IntPtr.Zero, XY(353, 273));
                            stage = 4;
                        }
                        else if (stage == 4 && selector == null && form.Visible) {
                            check(!form.IsRecording, "Releasing a shortcut-selected rectangle opens controls without recording");
                            recordingAt = clock.ElapsedMilliseconds; stage = 7;
                        }
                        else if (stage == 7 && clock.ElapsedMilliseconds - recordingAt > 600) {
                            check(!form.IsRecording && form.Indicator == null, "Selection remains idle until the Start recording button is clicked");
                            form.Controls.OfType<Button>().Single(b => b.Text.Contains("Start recording")).PerformClick();
                            stage = 8;
                        }
                        else if (stage == 8 && form.IsRecording) {
                            check(form.Indicator is { Visible: true, ExcludedFromCapture: true }, "Manual start displays the recording indicator excluded from capture");
                            check(form.Frame is { Visible: true, ExcludedFromCapture: true }, "Thin recording frame is visible and excluded from capture");
                            var outline = form.Frame!;
                            check(outline.Region!.IsVisible(1, 20) && !outline.Region.IsVisible(3, 20) && !outline.Region.IsVisible(outline.Width / 2, outline.Height / 2), "Frame has two-pixel edges and a completely hollow interior");
                            check((Native.GetWindowLong(outline.Handle, -20) & 0x08080020) == 0x08080020, "Frame is layered, click-through and does not activate");
                            recordingAt = clock.ElapsedMilliseconds; stage = 5;
                        }
                        else if (stage == 5 && clock.ElapsedMilliseconds - recordingAt > 1200) { Shortcut(); stage = 6; }
                        else if (stage == 6 && form.Controls.OfType<Label>().Any(l => l.Text.StartsWith("Saved"))) {
                            check(!form.IsRecording && Directory.GetFiles(recordings, "*.mp4").Any(p => new FileInfo(p).Length > 0), "Same shortcut stops recording and saves the MP4");
                            check(form.Indicator == null, "Recording indicator is removed after saving");
                            check(form.Frame == null, "Recording frame is removed when capture stops");
                            poll.Stop(); form.RequestExit(); finished.TrySetResult();
                        }
                    }
                    catch (Exception e) { poll.Stop(); form.RequestExit(); finished.TrySetException(e); }
                };
                poll.Start();
                Application.Run(context);
            }
            catch (Exception e) { finished.TrySetException(e); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return finished.Task;
    }

    private static async Task<string> Execute(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception(await stderr);
        return await stdout;
    }
}
