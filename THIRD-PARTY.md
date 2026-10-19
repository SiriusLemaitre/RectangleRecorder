# Third-party components

## FFmpeg

- Version: 9.0.2-essentials_build-www.gyan.dev, Windows x64, unmodified.
- Provider: https://www.gyan.dev/ffmpeg/builds/
- Project: https://ffmpeg.org/
- Source revision supplied by the build provider: https://github.com/FFmpeg/FFmpeg/commit/946fcce07b
- License for this build: GNU GPL version 3. See `FFmpeg-LICENSE.txt`.
- Build configuration and dependency details: `FFmpeg-BUILD.txt`.
- Original downloaded archive: `ffmpeg-release-essentials.zip`.
- SHA-256 of that archive: `60F467265B1E312373DBCD92200C2618A74850F98D3D078E94296BB3FA2047BA`.

FFmpeg runs as a separate process. No FFmpeg library is linked into Rectangle Recorder. The binary is not modified. Source and build information are linked above; review the component license requirements before redistributing the binary to others.

## Microsoft .NET 10

The self-contained package includes the Microsoft .NET and Windows Desktop runtimes. Their license and third-party notices are included as `DOTNET-LICENSE.txt` and `DOTNET-THIRD-PARTY-NOTICES.txt`.

Project: https://github.com/dotnet

## NAudio

- Components: NAudio.Wasapi and NAudio.Core 2.2.1, unmodified NuGet assemblies.
- Author: Mark Heath and contributors.
- Source: https://github.com/naudio/NAudio/tree/v2.2.1
- License: MIT; see `NAudio-LICENSE.txt`.
- Used to capture Windows WASAPI playback-loopback audio and maintain the capture clock through silent intervals.

## Native shortcut launcher toolchain

- Built from `native/launcher.c` with Zig 0.15.2 targeting Windows x64 / MinGW.
- Compiler: https://ziglang.org/download/
- MinGW-w64 startup/runtime portions retain their upstream licenses; see `MINGW-LICENSE.txt`.
- No compiler or additional managed runtime is shipped for the launcher. It uses Windows system DLLs.
