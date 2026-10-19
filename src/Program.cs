using System.Runtime.InteropServices;

namespace RectangleRecorder;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 2 && args[0] == "--self-test")
        {
            SelfTest.Run(args[1]).GetAwaiter().GetResult();
            return;
        }
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Rectangle Recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try {
            if (args.Contains("--enable-startup")) { WindowsStartup.SetEnabled(true); return; }
            if (args.Contains("--disable-startup")) { WindowsStartup.SetEnabled(false); return; }
            var command = args.Contains("--select") ? "toggle" : args.Contains("--background") ? "background" : args.Contains("--quit") ? "quit" : "show";
            var hosted = ShortcutHost.IsAvailable;
            if (hosted && command != "quit") {
                ShortcutHost.EnsureRunning();
                if (command == "background") return;
            }
            if (hosted && command == "quit") ShortcutHost.Quit();
            using var instance = new SingleInstance();
            if (!instance.IsPrimary) { instance.SendAsync(command).GetAwaiter().GetResult(); return; }
            if (command == "quit") return;
            using var context = new LauncherContext(instance, command, new MainForm(hosted: hosted));
            Application.Run(context);
        }
        catch (Exception e) {
            Environment.ExitCode = 1;
            MessageBox.Show(e.Message, "Rectangle Recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal static class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetPrivateProfileInt(string section, string key, int fallback, string fileName);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string? className, string? windowName);
    [DllImport("user32.dll")]
    internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")]
    internal static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr icon);
}
