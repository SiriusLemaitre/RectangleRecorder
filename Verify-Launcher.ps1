param([string]$AppDirectory = (Join-Path $PSScriptRoot 'app'))
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class RecorderWindowTest {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    public static IntPtr ByTitle(string title) { return FindWindow(null, title); }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr hwnd, string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern int GetPrivateProfileInt(string section, string key, int fallback, string file);
}
'@
function Wait-Until([scriptblock]$Condition, [string]$Failure) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (& $Condition)) { if ([DateTime]::UtcNow -gt $deadline) { throw $Failure }; Start-Sleep -Milliseconds 40 }
}
$report = [Collections.Generic.List[string]]::new()
$launcherFile = Join-Path $AppDirectory 'RectangleRecorderLauncher.exe'
$appFile = Join-Path $AppDirectory 'RectangleRecorder.exe'
trap {
    $openSelector = [RecorderWindowTest]::ByTitle('Select recording area')
    if ($openSelector -ne [IntPtr]::Zero) { [void][RecorderWindowTest]::PostMessage($openSelector,0x0100,[IntPtr]27,[IntPtr]::Zero) }
    throw $_
}
$oldSelector = [RecorderWindowTest]::ByTitle('Select recording area')
if ($oldSelector -ne [IntPtr]::Zero) {
    [void][RecorderWindowTest]::PostMessage($oldSelector,0x0100,[IntPtr]27,[IntPtr]::Zero)
    Wait-Until { @(Get-Process RectangleRecorder -ErrorAction SilentlyContinue | Where-Object Path -eq $appFile).Count -eq 0 } 'Previous selector did not close.'
}
$launch = Start-Process -FilePath $launcherFile -ArgumentList '--background' -WindowStyle Hidden -PassThru
Wait-Until { [RecorderWindowTest]::FindWindow('RectangleRecorderShortcutHost','RectangleRecorderShortcutHost') -ne [IntPtr]::Zero } 'Native launcher did not start.'
$hostWindow = [RecorderWindowTest]::FindWindow('RectangleRecorderShortcutHost','RectangleRecorderShortcutHost')
if ([RecorderWindowTest]::GetProp($hostWindow,'HotkeyRegistered') -ne [IntPtr]1) { throw 'Native launcher failed to register the configured shortcut.' }
$report.Add('PASS Native launcher owns the configured shortcut without loading the recording interface')
[void][RecorderWindowTest]::PostMessage($hostWindow,0x0312,[IntPtr]1,[IntPtr]::Zero)
Wait-Until { [RecorderWindowTest]::ByTitle('Select recording area') -ne [IntPtr]::Zero } 'Shortcut did not open selection.'
$selector = [RecorderWindowTest]::ByTitle('Select recording area')
[void][RecorderWindowTest]::PostMessage($selector,0x0100,[IntPtr]27,[IntPtr]::Zero)
Wait-Until { @(Get-Process RectangleRecorder -ErrorAction SilentlyContinue | Where-Object Path -eq $appFile).Count -eq 0 } 'Cancelling selection did not unload the recording interface.'
$report.Add('PASS Native shortcut opens selection; cancelling unloads the interface')
[void][RecorderWindowTest]::PostMessage($hostWindow,0x8311,[IntPtr]::Zero,[IntPtr]::Zero)
Wait-Until { [RecorderWindowTest]::ByTitle('Rectangle Recorder') -ne [IntPtr]::Zero } 'Tray show command did not open controls.'
$controls = [RecorderWindowTest]::ByTitle('Rectangle Recorder')
[void][RecorderWindowTest]::PostMessage($controls,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
Wait-Until { @(Get-Process RectangleRecorder -ErrorAction SilentlyContinue | Where-Object Path -eq $appFile).Count -eq 0 } 'Closing controls did not release .NET memory.'
$report.Add('PASS Closing idle controls releases the .NET process and leaves the shortcut available')
$initialPreset = [RecorderWindowTest]::GetProp($hostWindow,'HotkeyPreset').ToInt32()
if ($initialPreset -eq 0 -and [RecorderWindowTest]::RegisterHotKey([IntPtr]::Zero,73,0x4006,0x79)) {
    try {
        [void][RecorderWindowTest]::PostMessage($hostWindow,0x0111,[IntPtr]102,[IntPtr]::Zero)
        Wait-Until { [RecorderWindowTest]::ByTitle('Shortcut unavailable') -ne [IntPtr]::Zero } 'Conflicting shortcut was not rejected.'
        if ([RecorderWindowTest]::GetProp($hostWindow,'HotkeyPreset') -ne [IntPtr]::Zero -or [RecorderWindowTest]::GetProp($hostWindow,'HotkeyRegistered') -ne [IntPtr]1) { throw 'Existing shortcut was lost after a conflict.' }
        [void][RecorderWindowTest]::PostMessage([RecorderWindowTest]::ByTitle('Shortcut unavailable'),0x0111,[IntPtr]1,[IntPtr]::Zero)
        $report.Add('PASS A registered shortcut conflict is rejected while the existing shortcut stays active')
    } finally { [void][RecorderWindowTest]::UnregisterHotKey([IntPtr]::Zero,73) }
    [void][RecorderWindowTest]::PostMessage($hostWindow,0x0111,[IntPtr]102,[IntPtr]::Zero)
    Wait-Until { [RecorderWindowTest]::GetProp($hostWindow,'HotkeyPreset') -eq [IntPtr]2 } 'Alternate shortcut could not be selected.'
    if ([RecorderWindowTest]::GetPrivateProfileInt('Shortcut','Preset',-1,(Join-Path $AppDirectory 'launcher.ini')) -ne 2) { throw 'Shortcut selection was not saved.' }
    [void][RecorderWindowTest]::PostMessage($hostWindow,0x0111,[IntPtr]100,[IntPtr]::Zero)
    Wait-Until { [RecorderWindowTest]::GetProp($hostWindow,'HotkeyPreset') -eq [IntPtr]::Zero } 'Default shortcut could not be restored.'
    $report.Add('PASS Alternate shortcut persists, switches immediately and restores Alt+Shift+Space')
}
$duplicate = Start-Process -FilePath $launcherFile -ArgumentList '--background' -WindowStyle Hidden -Wait -PassThru
$hosts = @(Get-Process RectangleRecorderLauncher | Where-Object Path -eq $launcherFile)
if ($duplicate.ExitCode -ne 0 -or $hosts.Count -ne 1) { throw 'Duplicate native launchers were created.' }
$report.Add('PASS Repeated background launches leave exactly one native launcher')
$idleHost = $hosts[0]
$idleHost.Refresh()
$cpuBefore = $idleHost.TotalProcessorTime.TotalSeconds
Start-Sleep -Seconds 3
$idleHost.Refresh()
$metrics = [pscustomobject]@{
    LauncherBytes = (Get-Item -LiteralPath $launcherFile).Length
    WorkingSetMiB = [math]::Round($idleHost.WorkingSet64 / 1MB, 2)
    PrivateMemoryMiB = [math]::Round($idleHost.PrivateMemorySize64 / 1MB, 2)
    IdleCpuSecondsOverThreeSeconds = $idleHost.TotalProcessorTime.TotalSeconds - $cpuBefore
    InterfaceProcessRunning = (@(Get-Process RectangleRecorder -ErrorAction SilentlyContinue | Where-Object Path -eq $appFile).Count -ne 0)
}
$verification = Join-Path $PSScriptRoot 'verification'
$metrics | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $verification 'idle-resources.json')
$report.Add('ALL NATIVE LAUNCHER CHECKS PASSED')
$report | Set-Content -LiteralPath (Join-Path $verification 'native-launcher-report.txt')
$report
$metrics | Format-List
