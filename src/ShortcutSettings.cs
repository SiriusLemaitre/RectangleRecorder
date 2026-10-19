namespace RectangleRecorder;

internal static class ShortcutSettings
{
    internal static readonly string[] Labels = { "Alt+Shift+Space", "Ctrl+Alt+Shift+Space", "Ctrl+Shift+F10" };
    internal static readonly uint[] Modifiers = { 0x4005, 0x4007, 0x4006 };
    internal static readonly Keys[] Keys = { System.Windows.Forms.Keys.Space, System.Windows.Forms.Keys.Space, System.Windows.Forms.Keys.F10 };
    internal static int Preset => Math.Clamp(Native.GetPrivateProfileInt("Shortcut", "Preset", 0, Path.Combine(AppContext.BaseDirectory, "launcher.ini")), 0, 2);
    internal static string Label => Labels[Preset];
    internal static string TrayText => "Rectangle Recorder · " + Label;
}
