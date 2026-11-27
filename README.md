# Auralita

Auralita is a macOS-first, Avalonia-based music player. It plays local files
through AVFoundation and can browse and stream music from Plex Media Server.
The player shows elapsed and remaining time, seekable song progress, cover
art, artist and album details, and previous/play/pause/stop/next controls. Its
original, XAML-authored retro player skin is inspired by classic Winamp-era
software rather than copying a specific skin or its assets. Plex connection
settings are stored locally and the authentication token is kept in macOS
Keychain. Broader local indexing, the full FFmpeg codec matrix, and system
media controls remain follow-on work.

## Requirements

- macOS on Apple Silicon for the local build described below
- .NET SDK `10.0.404` (pinned in `global.json`)
- Xcode Command Line Tools for NativeAOT publishing

The SDK can be installed locally without changing the system installation:

```sh
mkdir -p .tooling
curl -fsSL https://dot.net/v1/dotnet-install.sh -o .tooling/dotnet-install.sh
bash .tooling/dotnet-install.sh --channel 10.0 --install-dir "$PWD/.tooling/dotnet" --no-path
export DOTNET_ROOT="$PWD/.tooling/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="$PWD/.tooling/dotnet-home"
export NUGET_PACKAGES="$PWD/.tooling/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
```

## Build and run

```sh
dotnet restore Auralita.sln
dotnet build Auralita.sln -c Debug
dotnet test Auralita.sln -c Debug
dotnet run --project src/Auralita.App/Auralita.App.csproj
```

Add local audio files to the queue and use **Previous**, **Play**, **Pause**,
**Stop**, and **Next** in the player deck. The progress bar is clickable to
seek; elapsed/remaining clocks follow playback, and tracks advance
automatically at the end of a song. Audio is decoded by macOS, so support
depends on the system audio formats; unsupported or unreadable files report an
error in the app. Selected items are not copied, persisted, or uploaded.

To browse Plex, choose **PLEX LIBRARY**, enter a server URL (`http://host:32400`,
an HTTPS server URL, or `https://app.plex.tv`) and a Plex token, then select
**FIND MUSIC LIBRARIES** and load a music library. The token is stored in
Keychain; the server URL and selected library are saved in the per-user Auralita
application-support folder. Plex streams use AVPlayer and Plex cover art is
cached in memory while the app is running. Plex music sections reported as
either `artist` or `music` are recognized.

## NativeAOT publish

```sh
dotnet publish src/Auralita.App/Auralita.App.csproj \
  -c Release -r osx-arm64 -p:PublishAot=true
dotnet publish src/Auralita.App/Auralita.App.csproj \
  -c Release -r osx-x64 -p:PublishAot=true
```

## Build a macOS app bundle

The NativeAOT publish commands above create executables, not Finder-launchable
`.app` bundles. To build a locally ad-hoc-signed Apple Silicon app:

```sh
sh scripts/package-macos-app.sh
open artifacts/Auralita.app
```

The script packages the executable and Avalonia native libraries into
`Contents/MacOS`, including the AVFoundation, CoreMedia, and Keychain bridge,
and adds a generated vector crystal icon to the app bundle for Finder and the
Dock. This local build is not notarized or signed with a Developer ID, and is
not intended for distribution. Golden codec assets and a universal
`arm64`/`x86_64` release bundle remain out of scope for this initial scaffold.
