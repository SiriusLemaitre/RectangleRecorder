#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <strsafe.h>
#include <wchar.h>

#define HOST_CLASS L"RectangleRecorderShortcutHost"
#define TOGGLE (WM_APP + 0x310)
#define SHOW_RECORDER (WM_APP + 0x311)
#define STOP_RECORDER (WM_APP + 0x312)
#define EXIT_RECORDER (WM_APP + 0x313)
#define TRAY_CALLBACK (WM_APP + 1)
#define RUN_KEY L"Software\\Microsoft\\Windows\\CurrentVersion\\Run"
static const WCHAR *shortcutLabels[] = { L"Alt+Shift+Space", L"Ctrl+Alt+Shift+Space", L"Ctrl+Shift+F10" };
static const UINT shortcutModifiers[] = { MOD_ALT | MOD_SHIFT, MOD_CONTROL | MOD_ALT | MOD_SHIFT, MOD_CONTROL | MOD_SHIFT };
static const UINT shortcutKeys[] = { VK_SPACE, VK_SPACE, VK_F10 };
static WCHAR settingsPath[32768];
static int shortcutPreset, hotkeyId = 1;
static BOOL hotkeyRegistered;
static WCHAR appPath[32768], hostPath[32768], directory[32768];
static NOTIFYICONDATAW tray;
static HANDLE launching;
static HICON appIcon;
static UINT taskbarCreated;

static BOOL CALLBACK FindRecorder(HWND window, LPARAM result) {
    WCHAR title[64], path[32768];
    if (!GetWindowTextW(window, title, 64) || wcscmp(title, L"Rectangle Recorder")) return TRUE;
    DWORD pid = 0, length = 32768;
    GetWindowThreadProcessId(window, &pid);
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!process) return TRUE;
    BOOL same = QueryFullProcessImageNameW(process, 0, path, &length) && !_wcsicmp(path, appPath);
    CloseHandle(process);
    if (!same) return TRUE;
    *(HWND*)result = window;
    return FALSE;
}

static HWND RecorderWindow(void) { HWND result = NULL; EnumWindows(FindRecorder, (LPARAM)&result); return result; }

static void UpdateShortcutTip(HWND window) {
    StringCchPrintfW(tray.szTip, 128, L"Rectangle Recorder — select / stop: %s", shortcutLabels[shortcutPreset]);
    tray.uFlags = NIF_TIP;
    Shell_NotifyIconW(NIM_MODIFY, &tray);
    SetPropW(window, L"HotkeyRegistered", (HANDLE)(INT_PTR)hotkeyRegistered);
    SetPropW(window, L"HotkeyPreset", (HANDLE)(INT_PTR)shortcutPreset);
}

static void ChangeShortcut(HWND window, int preset) {
    if (preset == shortcutPreset && hotkeyRegistered) return;
    int candidateId = hotkeyId == 1 ? 2 : 1;
    if (!RegisterHotKey(window, candidateId, shortcutModifiers[preset] | MOD_NOREPEAT, shortcutKeys[preset])) {
        MessageBoxW(window, L"This shortcut is already registered by Windows or another app. Your existing shortcut has been kept. Choose another in the Shortcut menu.", L"Shortcut unavailable", MB_OK | MB_ICONINFORMATION);
        return;
    }
    WCHAR value[4]; StringCchPrintfW(value, 4, L"%d", preset);
    if (!WritePrivateProfileStringW(L"Shortcut", L"Preset", value, settingsPath)) {
        UnregisterHotKey(window, candidateId);
        MessageBoxW(window, L"Could not save the shortcut. Keep the app in a writable folder. Your existing shortcut has been kept.", L"Rectangle Recorder", MB_OK | MB_ICONERROR);
        return;
    }
    if (hotkeyRegistered) UnregisterHotKey(window, hotkeyId);
    hotkeyId = candidateId; shortcutPreset = preset; hotkeyRegistered = TRUE;
    UpdateShortcutTip(window);
    HWND recorder = RecorderWindow();
    if (recorder) PostMessageW(recorder, WM_APP + 0x314, 0, 0);
}

static void InvokeRecorder(UINT command) {
    HWND recorder = RecorderWindow();
    if (recorder) {
        DWORD pid;
        GetWindowThreadProcessId(recorder, &pid);
        AllowSetForegroundWindow(pid);
        PostMessageW(recorder, command, 0, 0);
        return;
    }
    if (command == STOP_RECORDER || command == EXIT_RECORDER) return;
    if (launching) {
        if (WaitForSingleObject(launching, 0) == WAIT_TIMEOUT) return;
        CloseHandle(launching); launching = NULL;
    }
    WCHAR *line = HeapAlloc(GetProcessHeap(), 0, 66000 * sizeof(WCHAR));
    if (!line) return;
    StringCchPrintfW(line, 66000, L"\"%s\" --hosted %s", appPath, command == TOGGLE ? L"--select" : L"");
    STARTUPINFOW startup = { .cb = sizeof(startup) };
    PROCESS_INFORMATION process;
    if (CreateProcessW(appPath, line, NULL, NULL, FALSE, 0, NULL, directory, &startup, &process)) {
        launching = process.hProcess;
        CloseHandle(process.hThread);
    } else {
        StringCchCopyW(tray.szInfoTitle, 64, L"Could not open recorder");
        StringCchCopyW(tray.szInfo, 256, L"Keep RectangleRecorder.exe and ffmpeg.exe beside the launcher.");
        tray.uFlags = NIF_INFO; tray.dwInfoFlags = NIIF_ERROR;
        Shell_NotifyIconW(NIM_MODIFY, &tray);
    }
    HeapFree(GetProcessHeap(), 0, line);
}

static void StartupCommand(WCHAR *command, size_t length) { StringCchPrintfW(command, length, L"\"%s\" --background", hostPath); }

static BOOL StartupEnabled(void) {
    WCHAR actual[32768], expected[32768]; DWORD bytes = sizeof(actual);
    StartupCommand(expected, 32768);
    return RegGetValueW(HKEY_CURRENT_USER, RUN_KEY, L"RectangleRecorder", RRF_RT_REG_SZ, NULL, actual, &bytes) == ERROR_SUCCESS && !_wcsicmp(actual, expected);
}

static void ToggleStartup(HWND owner) {
    HKEY key; LONG error = RegCreateKeyExW(HKEY_CURRENT_USER, RUN_KEY, 0, NULL, 0, KEY_SET_VALUE, NULL, &key, NULL);
    if (error == ERROR_SUCCESS) {
        if (StartupEnabled()) error = RegDeleteValueW(key, L"RectangleRecorder");
        else { WCHAR command[32768]; StartupCommand(command, 32768); error = RegSetValueExW(key, L"RectangleRecorder", 0, REG_SZ, (BYTE*)command, (DWORD)((wcslen(command) + 1) * sizeof(WCHAR))); }
        RegCloseKey(key);
    }
    if (error != ERROR_SUCCESS) MessageBoxW(owner, L"Windows startup could not be updated.", L"Rectangle Recorder", MB_OK | MB_ICONERROR);
}

static HICON MakeIcon(void) {
    BITMAPINFO info = { .bmiHeader = { .biSize = sizeof(BITMAPINFOHEADER), .biWidth = 32, .biHeight = -32, .biPlanes = 1, .biBitCount = 32, .biCompression = BI_RGB } };
    DWORD *pixels;
    HBITMAP color = CreateDIBSection(NULL, &info, DIB_RGB_COLORS, (void**)&pixels, NULL, 0);
    if (!color) return LoadIconW(NULL, IDI_APPLICATION);
    for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) {
        DWORD p = 0;
        if (x >= 3 && x <= 28 && y >= 5 && y <= 26 && (x <= 5 || x >= 26 || y <= 7 || y >= 24)) p = 0xff5ca9ff;
        int dx = x - 16, dy = y - 16;
        if (dx*dx + dy*dy <= 30) p = 0xffff6174;
        pixels[y*32 + x] = p;
    }
    HBITMAP mask = CreateBitmap(32, 32, 1, 1, NULL);
    ICONINFO iconInfo = { .fIcon = TRUE, .hbmMask = mask, .hbmColor = color };
    HICON icon = CreateIconIndirect(&iconInfo);
    DeleteObject(mask); DeleteObject(color);
    return icon;
}

static void TrayMenu(HWND window) {
    HMENU menu = CreatePopupMenu();
    AppendMenuW(menu, MF_STRING, 1, L"Open recorder");
    WCHAR selectLabel[96]; StringCchPrintfW(selectLabel, 96, L"Select area\t%s", shortcutLabels[shortcutPreset]);
    AppendMenuW(menu, MF_STRING, 2, selectLabel);
    AppendMenuW(menu, MF_STRING, 3, L"Stop recording");
    AppendMenuW(menu, MF_SEPARATOR, 0, NULL);
    HMENU shortcuts = CreatePopupMenu();
    for (int i = 0; i < 3; i++) AppendMenuW(shortcuts, MF_STRING | ((i == shortcutPreset && hotkeyRegistered) ? MF_CHECKED : 0), 100 + i, shortcutLabels[i]);
    AppendMenuW(menu, MF_POPUP, (UINT_PTR)shortcuts, L"Shortcut");
    AppendMenuW(menu, MF_STRING | (StartupEnabled() ? MF_CHECKED : 0), 4, L"Start with Windows");
    AppendMenuW(menu, MF_STRING, 5, L"Exit");
    POINT point; GetCursorPos(&point); SetForegroundWindow(window);
    UINT selected = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.x, point.y, 0, window, NULL);
    DestroyMenu(menu); PostMessageW(window, WM_NULL, 0, 0);
    if (selected == 1) InvokeRecorder(SHOW_RECORDER);
    if (selected == 2) InvokeRecorder(TOGGLE);
    if (selected == 3) InvokeRecorder(STOP_RECORDER);
    if (selected == 4) ToggleStartup(window);
    if (selected == 5) { InvokeRecorder(EXIT_RECORDER); DestroyWindow(window); }
    if (selected >= 100 && selected <= 102) ChangeShortcut(window, selected - 100);
}

static LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == taskbarCreated) { tray.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP; Shell_NotifyIconW(NIM_ADD, &tray); return 0; }
    if (message == WM_HOTKEY && wParam == (WPARAM)hotkeyId) { InvokeRecorder(TOGGLE); return 0; }
    if (message == WM_COMMAND && LOWORD(wParam) >= 100 && LOWORD(wParam) <= 102) { ChangeShortcut(window, LOWORD(wParam) - 100); return 0; }
    if (message == SHOW_RECORDER || message == TOGGLE || message == STOP_RECORDER) { InvokeRecorder(message); return 0; }
    if (message == TRAY_CALLBACK) {
        if ((UINT)lParam == WM_LBUTTONDBLCLK) InvokeRecorder(SHOW_RECORDER);
        if ((UINT)lParam == WM_RBUTTONUP || (UINT)lParam == WM_CONTEXTMENU) TrayMenu(window);
        return 0;
    }
    if (message == WM_CLOSE) { InvokeRecorder(EXIT_RECORDER); DestroyWindow(window); return 0; }
    if (message == WM_DESTROY) { if (hotkeyRegistered) UnregisterHotKey(window, hotkeyId); Shell_NotifyIconW(NIM_DELETE, &tray); PostQuitMessage(0); return 0; }
    return DefWindowProcW(window, message, wParam, lParam);
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR arguments, int show) {
    (void)previous; (void)show;
    GetModuleFileNameW(NULL, hostPath, 32768);
    StringCchCopyW(directory, 32768, hostPath);
    WCHAR *slash = wcsrchr(directory, L'\\'); if (!slash) return 1; *slash = 0;
    StringCchPrintfW(appPath, 32768, L"%s\\RectangleRecorder.exe", directory);
    StringCchPrintfW(settingsPath, 32768, L"%s\\launcher.ini", directory);
    shortcutPreset = GetPrivateProfileIntW(L"Shortcut", L"Preset", 0, settingsPath);
    if (shortcutPreset < 0 || shortcutPreset > 2) shortcutPreset = 0;
    BOOL background = wcsstr(arguments, L"--background") != NULL;
    BOOL quit = wcsstr(arguments, L"--quit") != NULL;
    HANDLE mutex = CreateMutexW(NULL, TRUE, L"Local\\RectangleRecorderShortcutHost");
    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        HWND existing = FindWindowW(HOST_CLASS, HOST_CLASS);
        if (existing && (quit || !background)) PostMessageW(existing, quit ? WM_CLOSE : SHOW_RECORDER, 0, 0);
        CloseHandle(mutex); return 0;
    }
    if (quit) { CloseHandle(mutex); return 0; }
    appIcon = MakeIcon();
    WNDCLASSW type = { .lpfnWndProc = WindowProcedure, .hInstance = instance, .lpszClassName = HOST_CLASS, .hIcon = appIcon };
    RegisterClassW(&type);
    HWND window = CreateWindowExW(WS_EX_TOOLWINDOW, HOST_CLASS, HOST_CLASS, 0, 0, 0, 0, 0, NULL, NULL, instance, NULL);
    if (!window) { CloseHandle(mutex); return 1; }
    taskbarCreated = RegisterWindowMessageW(L"TaskbarCreated");
    tray.cbSize = sizeof(tray); tray.hWnd = window; tray.uID = 1; tray.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    tray.uCallbackMessage = TRAY_CALLBACK; tray.hIcon = appIcon;
    StringCchPrintfW(tray.szTip, 128, L"Rectangle Recorder — select / stop: %s", shortcutLabels[shortcutPreset]);
    Shell_NotifyIconW(NIM_ADD, &tray);
    hotkeyRegistered = RegisterHotKey(window, hotkeyId, shortcutModifiers[shortcutPreset] | MOD_NOREPEAT, shortcutKeys[shortcutPreset]);
    UpdateShortcutTip(window);
    if (!hotkeyRegistered) {
        tray.uFlags = NIF_INFO; tray.dwInfoFlags = NIIF_WARNING;
        StringCchCopyW(tray.szInfoTitle, 64, L"Recording shortcut unavailable");
        StringCchCopyW(tray.szInfo, 256, L"Another app may be using this shortcut. Right-click this icon and choose another under Shortcut. The controls remain available here.");
        Shell_NotifyIconW(NIM_MODIFY, &tray);
    }
    if (!background) InvokeRecorder(SHOW_RECORDER);
    MSG message;
    while (GetMessageW(&message, NULL, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    if (launching) CloseHandle(launching);
    DestroyIcon(appIcon); ReleaseMutex(mutex); CloseHandle(mutex);
    return 0;
}
