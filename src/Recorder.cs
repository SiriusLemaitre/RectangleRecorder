using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RectangleRecorder;

internal sealed record RecordingOptions(Rectangle Area, int Fps, int Crf, bool Cursor, string Output, bool DesktopAudio = false);

internal sealed class Recorder : IDisposable
{
    private Process? process;
    private Task? errorReader;
    private SystemAudio? audio;
    private readonly StringBuilder errors = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsRunning => process is { HasExited: false } && audio?.Failure == null;
    public string ErrorLog { get { lock (errors) return errors.ToString(); } }
    public string Executable { get; }

    public Recorder(string? executable = null) => Executable = executable ?? Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");

    public static IReadOnlyList<string> Arguments(RecordingOptions options, string? audioPipe = null)
    {
        var a = options.Area;
        if (a.Width < 2 || a.Height < 2) throw new ArgumentException("Select an area at least 2 × 2 pixels.");
        if (options.Fps is < 1 or > 120) throw new ArgumentException("Frame rate must be between 1 and 120.");
        if (options.Crf is < 0 or > 51) throw new ArgumentException("Quality must be between 0 and 51.");
        string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        var args = new List<string> {
            "-hide_banner", "-loglevel", "info", "-stats_period", "1", "-progress", "pipe:2", "-nostats",
            // Bound raw-frame buffering to roughly 32 MiB, with at least two frames.
            "-thread_queue_size", N((int)Math.Clamp(33554432L / ((long)a.Width * a.Height * 4), 2, 16)), "-probesize", "32", "-analyzeduration", "0",
            "-f", "gdigrab", "-framerate", N(options.Fps), "-draw_mouse", options.Cursor ? "1" : "0",
            "-offset_x", N(a.X), "-offset_y", N(a.Y), "-video_size", $"{N(a.Width)}x{N(a.Height)}", "-i", "desktop"
        };
        if (options.DesktopAudio) {
            if (audioPipe == null) throw new ArgumentException("System audio requires a capture stream.");
            args.AddRange(new[] { "-thread_queue_size", "32", "-f", "f32le", "-ar", "48000", "-ac", "2",
                "-probesize", "32", "-analyzeduration", "0", "-i", audioPipe,
                "-map", "0:v:0", "-map", "1:a:0", "-c:a", "aac", "-b:a", "192k" });
        }
        else args.Add("-an");
        args.AddRange(new[] {
            "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-preset", "ultrafast",
            "-crf", N(options.Crf), "-pix_fmt", "yuv420p", "-r", N(options.Fps), "-fps_mode", "cfr",
            "-movflags", "+faststart", "-n", options.Output
        });
        return args;
    }

    public async Task StartAsync(RecordingOptions options)
    {
        if (process != null) throw new InvalidOperationException("This recording session has already started.");
        if (!File.Exists(Executable)) throw new FileNotFoundException("ffmpeg.exe is missing. Keep it beside RectangleRecorder.exe.", Executable);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.Output))!);
        var info = new ProcessStartInfo(Executable) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true
        };
        if (options.DesktopAudio) audio = new SystemAudio();
        foreach (var arg in Arguments(options, audio?.InputPath)) info.ArgumentList.Add(arg);
        process = new Process { StartInfo = info };
        process.Start();
        errorReader = Task.Run(async () => {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                const string startPrefix = "Duration: N/A, start: ";
                var trimmed = line.TrimStart();
                if (audio != null && trimmed.StartsWith(startPrefix)) {
                    var timestamp = trimmed[startPrefix.Length..].Split(',')[0];
                    if (double.TryParse(timestamp, NumberStyles.Float, CultureInfo.InvariantCulture, out var unixStart)) audio.SetVideoStart(unixStart);
                }
                if (line.StartsWith("frame=") && int.TryParse(line.AsSpan(6), out var frames) && frames > 0) ready.TrySetResult();
                // Progress is consumed live; retaining it for an hours-long recording
                // only adds allocations and provides no extra failure diagnostics.
                if (line.Contains('=') && !line.StartsWith('[') && !line.StartsWith("  ")) continue;
                lock (errors) {
                    errors.AppendLine(line);
                    if (errors.Length > 24000) errors.Remove(0, errors.Length - 16000);
                }
            }
        });
        var exited = process.WaitForExitAsync();
        var winner = await Task.WhenAny(ready.Task, exited, Task.Delay(15000));
        if (winner == ready.Task && !process.HasExited && audio?.Failure == null) return;
        if (!process.HasExited) await StopAsync();
        if (errorReader != null) await errorReader;
        throw new InvalidOperationException("The recording could not start.\n\n" + ErrorLog);
    }

    public async Task<int> StopAsync()
    {
        if (process == null) { if (audio != null) await audio.StopAsync(); return 0; }
        // Keep feeding sound while FFmpeg flushes video, then release WASAPI.
        audio?.ExpectEncoderStop();
        if (!process.HasExited)
        {
            try { await process.StandardInput.WriteLineAsync("q"); await process.StandardInput.FlushAsync(); }
            catch (IOException) { }
            var exit = process.WaitForExitAsync();
            if (await Task.WhenAny(exit, Task.Delay(30000)) != exit)
            {
                process.Kill(true);
                await exit;
                throw new IOException("The encoder stopped responding. The MP4 might be incomplete. See the recording log.");
            }
            await exit;
        }
        if (errorReader != null) await errorReader;
        if (audio != null) await audio.StopAsync();
        if (audio?.Failure is { } audioError) throw new IOException("System audio capture failed. Check your playback device or disable Desktop audio.\n\n" + audioError.Message, audioError);
        return process.ExitCode;
    }

    public void Dispose()
    {
        audio?.Dispose();
        if (process is { HasExited: false }) process.Kill(true);
        process?.Dispose();
    }
}
