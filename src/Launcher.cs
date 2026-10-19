using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Win32;

namespace RectangleRecorder;

internal static class WindowsStartup
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RectangleRecorder";
    internal static string Command => $"\"{(ShortcutHost.IsAvailable ? ShortcutHost.Executable : Environment.ProcessPath)}\" --background";
    internal static bool IsEnabled {
        get { using var key = Registry.CurrentUser.OpenSubKey(KeyPath); return key?.GetValue(ValueName) is string value && value == Command; }
    }
    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

internal static class ShortcutHost
{
    internal static string Executable => Path.Combine(AppContext.BaseDirectory, "RectangleRecorderLauncher.exe");
    internal static bool IsAvailable => File.Exists(Executable);
    internal static IntPtr Window => Native.FindWindow("RectangleRecorderShortcutHost", "RectangleRecorderShortcutHost");
    internal static void EnsureRunning()
    {
        if (Window != IntPtr.Zero) return;
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Executable, "--background") { UseShellExecute = false, CreateNoWindow = true });
        for (var i = 0; i < 100 && Window == IntPtr.Zero; i++) Thread.Sleep(20);
        if (Window == IntPtr.Zero) throw new IOException("The background shortcut launcher could not start.");
    }
    internal static void Quit() { if (Window is var window && window != IntPtr.Zero) Native.PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); }
}

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource cancellation = new();
    private Task? listener;
    internal bool IsPrimary { get; }

    internal SingleInstance(string? testName = null)
    {
        var identity = testName ?? $"{WindowsIdentity.GetCurrent().User!.Value}-{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
        pipeName = "RectangleRecorder-Control-" + identity;
        mutex = new Mutex(true, @"Local\" + pipeName, out var primary);
        IsPrimary = primary;
    }

    internal void Listen(Action<string> commandReceived)
    {
        listener = Task.Run(async () => {
            while (!cancellation.IsCancellationRequested) {
                try {
                    using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(cancellation.Token);
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    var command = await reader.ReadLineAsync(requestTimeout.Token);
                    if (command is "show" or "toggle" or "background" or "quit") {
                        commandReceived(command);
                        await writer.WriteLineAsync("OK");
                    }
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
        });
    }

    internal async Task SendAsync(string command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(command);
        if (await reader.ReadLineAsync(timeout.Token) != "OK") throw new IOException("The running recorder did not accept the request.");
    }

    public void Dispose()
    {
        cancellation.Cancel();
        listener?.GetAwaiter().GetResult();
        cancellation.Dispose();
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}

internal sealed class LauncherContext : ApplicationContext
{
    internal MainForm RecorderForm { get; }
    internal LauncherContext(SingleInstance instance, string initialCommand, MainForm? recorderForm = null)
    {
        var form = recorderForm ?? new MainForm();
        RecorderForm = form;
        // Application.Run automatically shows ApplicationContext.MainForm.
        // Keep that property unset so startup genuinely stays in the tray.
        form.FormClosed += (_, _) => ExitThread();
        _ = form.Handle; // Register the hotkey without displaying a window at sign-in.
        void Dispatch(string command) {
            if (form.IsDisposed) return;
            try { form.BeginInvoke(() => {
                if (command == "toggle") _ = form.ToggleRecordingAsync();
                else if (command == "show") form.RestoreWindow();
                else if (command == "quit") form.RequestExit();
            }); }
            catch (InvalidOperationException) { }
        }
        instance.Listen(Dispatch);
        Dispatch(initialCommand);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) RecorderForm.Dispose();
        base.Dispose(disposing);
    }
}
