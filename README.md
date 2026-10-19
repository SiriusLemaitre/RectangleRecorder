# Rectangle Recorder

A portable Windows 11 recorder with rectangle selection, adjustable frame rate and quality, desktop audio, and a visible recording indicator. Recordings stay on your PC.

## Record

Open `app/RectangleRecorder.exe`. If using the ZIP, extract everything into one folder first. Keep `RectangleRecorderLauncher.exe` and `ffmpeg.exe` beside the app.

1. Click **Select area…**, or press **Alt + Shift + Space**, and drag a rectangle. Escape or right-click cancels selection.
2. Review the settings. **Selection never starts recording automatically.**
3. Set **1–120 fps**, choose a quality preset or custom CRF, and enable desktop audio if desired.
4. Click **Start recording**. Only this button starts recording. Escape or the shortcut cancels the optional countdown.
5. A thin **two-pixel frame** outlines the recording area, with a floating **REC** timer and **Stop** button. The outline is hollow and click-through. Drag the timer to move it. Both stay on top and are excluded from capture on Windows 11.
6. Stop with the indicator, the interface, **Alt + Shift + Space**, or the tray menu. Wait for **Saved**, then click **Open video**.

The indicator shows **Starting…** during initialization and **Saving…** during finalization. It disappears when finished. It prefers a corner outside the selected rectangle; capture exclusion also covers fullscreen recording.

Recordings default to your Windows Videos folder, under `Rectangle Recorder`. Click **Open folder** beside **Save recordings to** to open the selected folder in File Explorer, even before your first recording. Frame rate, quality, audio, cursor, countdown, and output folder are remembered in `settings.json` beside the app. A lower CRF gives sharper video and larger files. CRF 0 is lossless within the selected H.264 pixel format; screen RGB is still converted to YUV 4:2:0.

## Shortcut and Windows startup

**Alt + Shift + Space** selects an area while idle and stops/saves while recording. It never starts recording. After selection, click Start recording.

Change it through **tray icon → Shortcut**. Alternatives are **Ctrl + Alt + Shift + Space** and the previous **Ctrl + Shift + F10**. The choice is saved in `launcher.ini` beside the app and takes effect immediately. If another application has registered the requested combination globally, the recorder rejects the change and keeps the current shortcut active.

No global shortcut can be guaranteed unused by all installed apps: application-local bindings are not exposed by Windows' hotkey registration API. This default avoids [Windows' Alt+Space window menu](https://support.microsoft.com/en-us/accessibility/windows/keyboard-shortcuts-in-windows) and the Ctrl+Space / Ctrl+Alt+Space completion commands used by [VS Code](https://code.visualstudio.com/docs/editing/intellisense) and [Visual Studio](https://learn.microsoft.com/en-us/visualstudio/ide/default-keyboard-shortcuts-in-visual-studio). Select an alternative if your keyboard/input method or another tool already uses it.

The tray icon belongs to a tiny native launcher. It handles the shortcut without loading .NET, the interface, or FFmpeg. Double-click it to open the controls. Right-click to select an area, stop recording, toggle **Start with Windows**, or **Exit**.

- Closing idle controls releases the full interface process; the small launcher keeps listening.
- Cancelling selection from background mode also releases the interface process.
- Closing controls during recording hides them; recording continues and the REC indicator stays visible.
- The tray's Exit command saves an active recording and quits the launcher.
- Start with Windows launches the native listener hidden when you sign in. Uncheck it in the tray to disable startup.

Keep the app folder in the same location after enabling startup. If you move it, re-enable startup from its new location.

```text
RectangleRecorder.exe --background       Start only the native shortcut listener
RectangleRecorder.exe --select           Select an area, or stop an active recording
RectangleRecorder.exe --enable-startup   Enable startup for the current Windows user
RectangleRecorder.exe --disable-startup  Disable startup for the current Windows user
RectangleRecorder.exe --quit             Exit the recorder and shortcut listener
```

Startup uses the current user's `Software\Microsoft\Windows\CurrentVersion\Run` key, value `RectangleRecorder`, pointing to `RectangleRecorderLauncher.exe --background`. Administrator rights are not required.

## Performance and quality

The native launcher is about 49 KB. On this development machine it used **14.72 MiB working set / 2.46 MiB private memory**, with **0 CPU seconds over a 3-second idle sample**. The previous .NET tray process used 161.2 MiB working set / 74.1 MiB private memory on the same machine. Results vary by PC; see `verification/idle-resources.json`.

The interface and encoder run only when needed. Native idle mode has no polling timer, screen capture, or audio stream. Recording status updates once per second. Raw video buffering is bounded to 2–16 frames according to resolution. Encoder progress is consumed without accumulating a continuous log.

Encoding remains H.264/x264 ultrafast at your chosen CRF, frame rate, and original resolution. Desktop sound remains 48 kHz stereo AAC at 192 kb/s. The optimizations do not lower the quality setting or resolution. Odd widths/heights receive one padding pixel. Actual distinct captured frames depend on CPU, workload, and resolution; start with 30 fps and use 60 if your PC can keep up.

The portable folder remains about 150 MiB because it includes .NET and FFmpeg. No installer or runtime download is needed to use it. The build compiler is not shipped.

## Audio and limits

- Desktop audio captures sound through the default Windows multimedia output device at recording start. Apps routed to another output are excluded. Microphone recording is not included.
- Quiet gaps stay on the timeline. Audio streams directly into MP4 without temporary WAV files. If the output device changes/disconnects, stop and start a new recording. If no device is available, disable Desktop audio to record video alone.
- Capture includes other windows moved into the selected rectangle. Virtual coordinates support monitors left of/above the primary display. Mixed-DPI interaction still needs a hands-on check on your display setup.
- GDI capture is intended for ordinary desktop use; high-refresh gaming and HDR are outside this version's scope.
- Stop normally to finalize MP4. Killing the process or abrupt shutdown can leave an incomplete file. Encoder errors create a `.mp4.log` beside the attempted recording.

## Build and verify

Requires .NET 10 SDK, Zig 0.15.2 for the native launcher, and a Gyan FFmpeg essentials build (9.0.2 here). The portable compiler used here was downloaded from https://ziglang.org/download/ and verified against the SHA-256 in its official download index.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -Verify
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Verify-Launcher.ps1
```

Close the launcher from the tray before rebuilding. `Build.ps1` accepts `-FFmpegDirectory` and `-ZigExecutable` for alternative extracted tool locations; defaults are under `tools/`. First build downloads Microsoft runtime packs and NAudio.Wasapi / NAudio.Core 2.2.1 from NuGet. App usage is offline.

Verification checks selection geometry, manual start after shortcut selection, indicator and frame visibility/capture exclusion/removal, two-pixel hollow frame geometry and click-through styles, shortcut stop/save, and MP4 capture/decode at 15/30/60 fps. The audio check plays two brief quiet tones, verifies stereo AAC, detects recorded tones and the intervening gap, and compares stream durations. Native launcher checks cover shortcut routing, rejecting a deliberately registered conflict without losing the current shortcut, switching and persisting an alternative, unloading the interface, duplicate prevention, and idle resources.

Checks need an unlocked interactive desktop and working playback device. Run recorder verification with the normal launcher stopped so the test can register the shortcut. Captures/reports remain in `verification/` and are excluded from the ZIP. ffprobe is used only for verification.

## Third-party components

See `THIRD-PARTY.md` and included licenses. Application and native launcher source are supplied under MIT. FFmpeg runs as a separate, unmodified executable; the Windows build comes from [Gyan](https://www.gyan.dev/ffmpeg/builds/), a provider linked from [FFmpeg](https://ffmpeg.org/download.html).
