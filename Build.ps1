param(
    [string]$FFmpegDirectory = (Join-Path $PSScriptRoot 'tools\ffmpeg\ffmpeg-9.0.2-essentials_build'),
    [string]$ZigExecutable = (Join-Path $PSScriptRoot 'tools\zig\zig-x86_64-windows-0.15.2\zig.exe'),
    [switch]$Verify
)
$ErrorActionPreference = 'Stop'
$appDirectory = Join-Path $PSScriptRoot 'app'
$encoderFile = Join-Path $FFmpegDirectory 'bin\ffmpeg.exe'
if (-not (Test-Path -LiteralPath $encoderFile)) {
    throw 'Extract a Gyan FFmpeg essentials build and pass its directory with -FFmpegDirectory. See README.md.'
}
& dotnet publish (Join-Path $PSScriptRoot 'src\RectangleRecorder.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o $appDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
if (-not (Test-Path -LiteralPath $ZigExecutable)) { throw 'Zig 0.15.2 is required to build the native launcher. Pass its path with -ZigExecutable.' }
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $PSScriptRoot 'tools\zig-cache'
& $ZigExecutable cc (Join-Path $PSScriptRoot 'native\launcher.c') -target x86_64-windows-gnu -O2 -s -municode '-Wl,--subsystem,windows' -luser32 -lshell32 -lgdi32 -ladvapi32 -o (Join-Path $appDirectory 'RectangleRecorderLauncher.exe')
if ($LASTEXITCODE -ne 0) { throw 'Native launcher build failed.' }
Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $ZigExecutable) 'lib\libc\mingw\COPYING') -Destination (Join-Path $appDirectory 'MINGW-LICENSE.txt')
Copy-Item -LiteralPath $encoderFile -Destination $appDirectory
Copy-Item -LiteralPath (Join-Path $FFmpegDirectory 'LICENSE') -Destination (Join-Path $appDirectory 'FFmpeg-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $FFmpegDirectory 'README.txt') -Destination (Join-Path $appDirectory 'FFmpeg-BUILD.txt')
foreach ($name in @('README.md', 'THIRD-PARTY.md', 'LICENSE', 'NAudio-LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $appDirectory
}
$dotnetDirectory = Split-Path -Parent (Get-Command dotnet.exe).Source
Copy-Item -LiteralPath (Join-Path $dotnetDirectory 'LICENSE.txt') -Destination (Join-Path $appDirectory 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $dotnetDirectory 'ThirdPartyNotices.txt') -Destination (Join-Path $appDirectory 'DOTNET-THIRD-PARTY-NOTICES.txt')
if ($Verify) {
    Copy-Item -LiteralPath (Join-Path $FFmpegDirectory 'bin\ffprobe.exe') -Destination $appDirectory
    $verificationDirectory = Join-Path $PSScriptRoot 'verification'
    $testProcess = Start-Process -FilePath (Join-Path $appDirectory 'RectangleRecorder.exe') -ArgumentList @('--self-test', ('"' + $verificationDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $verificationDirectory 'report.txt')
    if ($testProcess.ExitCode -ne 0) { throw 'Verification failed.' }
}
Write-Host "Ready: $(Join-Path $appDirectory 'RectangleRecorder.exe')"
