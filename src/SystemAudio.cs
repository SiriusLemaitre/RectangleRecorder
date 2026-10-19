using System.IO.Pipes;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RectangleRecorder;

/// <summary>Streams the default playback endpoint into FFmpeg without temporary audio files.</summary>
internal sealed class SystemAudio : IDisposable
{
    private readonly NamedPipeServerStream pipe;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Channel<byte[]> packets = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(32) {
        SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
    });
    private readonly Task worker;
    private int stopping;
    private int encoderStopping;
    private Exception? failure;
    private double videoStartUnix;
    public Exception? Failure => Volatile.Read(ref failure);
    public string InputPath { get; }
    public void SetVideoStart(double unixSeconds) => Volatile.Write(ref videoStartUnix, unixSeconds);
    public void ExpectEncoderStop() => Interlocked.Exchange(ref encoderStopping, 1);

    public SystemAudio()
    {
        var name = "RectangleRecorder-" + Guid.NewGuid().ToString("N");
        InputPath = @"\\.\pipe\" + name;
        pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 0, 65536);
        worker = Task.Run(PumpAsync);
    }

    private async Task PumpAsync()
    {
        try {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            using var capture = new WasapiLoopbackCapture(device) { WaveFormat = format };
            // A silent shared-mode stream keeps loopback packets flowing while the
            // desktop is quiet, preserving gaps and the audio/video timeline.
            using var silence = new WasapiOut(device, AudioClientShareMode.Shared, false, 50);
            silence.Init(new SilenceProvider(format));
            var firstPacket = true;
            capture.DataAvailable += (_, e) => {
                if (e.BytesRecorded == 0 || Volatile.Read(ref stopping) != 0) return;
                var packet = e.Buffer.AsSpan(0, e.BytesRecorded).ToArray();
                if (firstPacket) {
                    firstPacket = false;
                    var videoStart = Volatile.Read(ref videoStartUnix);
                    var firstSample = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds - (double)e.BytesRecorded / format.AverageBytesPerSecond;
                    // GDI starts before FFmpeg opens its second input. Preserve that
                    // offset as silence instead of shifting all sound to video time 0.
                    var leadFrames = (int)Math.Round((firstSample - videoStart) * format.SampleRate);
                    if (videoStart == 0 || Math.Abs((long)leadFrames) > 48000 * 10) {
                        Interlocked.CompareExchange(ref failure, new IOException("Could not synchronize system audio with screen capture."), null);
                        packets.Writer.TryComplete(Failure);
                        return;
                    }
                    if (leadFrames > 0) packets.Writer.TryWrite(new byte[leadFrames * format.BlockAlign]);
                    else if (leadFrames < 0) packet = packet.AsSpan(Math.Min(packet.Length, -leadFrames * format.BlockAlign)).ToArray();
                }
                if (!packets.Writer.TryWrite(packet)) {
                    Interlocked.CompareExchange(ref failure, new IOException("The system-audio encoder could not keep up."), null);
                    packets.Writer.TryComplete(Failure);
                }
            };
            capture.RecordingStopped += (_, e) => {
                if (e.Exception != null) Interlocked.CompareExchange(ref failure, e.Exception, null);
                else if (Volatile.Read(ref stopping) == 0 && Volatile.Read(ref encoderStopping) == 0)
                    Interlocked.CompareExchange(ref failure, new IOException("The playback device stopped providing audio."), null);
                packets.Writer.TryComplete(Failure);
            };
            await pipe.WaitForConnectionAsync(cancellation.Token);
            silence.Play();
            capture.StartRecording();
            try {
                await foreach (var packet in packets.Reader.ReadAllAsync(cancellation.Token))
                    await pipe.WriteAsync(packet, cancellation.Token);
            }
            finally { capture.StopRecording(); silence.Stop(); }
        }
        catch (Exception e) {
            if (Volatile.Read(ref stopping) == 0 && Volatile.Read(ref encoderStopping) == 0) Interlocked.CompareExchange(ref failure, e, null);
        }
        finally { pipe.Dispose(); }
    }

    public async Task StopAsync()
    {
        Interlocked.Exchange(ref stopping, 1);
        cancellation.Cancel();
        await worker.ConfigureAwait(false);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref stopping, 1);
        cancellation.Cancel();
        // No capture callback waits on this thread; its worker owns the COM objects.
        worker.GetAwaiter().GetResult();
        cancellation.Dispose();
    }
}
