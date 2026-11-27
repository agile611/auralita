# Auralita

Auralita is a macOS-first, Avalonia-based music player. The current local
playback slice plays files supported by macOS's AVFoundation audio player
alongside an in-memory queue. The player shows elapsed and remaining time,
song progress, and previous/play/stop/next controls; it advances to the next
queued track when playback finishes. Its original, XAML-authored retro player
skin is inspired by classic Winamp-era software rather than copying a specific
skin or its assets. The player controls and visual templates are authored in
app XAML without an external UI theme package. Plex integration, indexing,
persistence, the broad FFmpeg codec matrix, and system media controls remain
follow-on work.

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

Add local audio files to the queue and use **Previous**, **Play**, **Stop**, and
**Next** in the player deck. The progress bar and elapsed/remaining clocks
follow playback; tracks advance automatically at the end of a song. Audio is
decoded by macOS, so support depends on the system audio formats; unsupported
or unreadable files report an error in the app. Selected items are not copied,
persisted, or uploaded.

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
`Contents/MacOS`, including the AVFoundation playback bridge, and adds a
generated vector crystal icon to the app bundle for Finder and the Dock. This
local build is not notarized or signed with a Developer ID, and is not intended
for distribution. Golden codec assets and a universal
`arm64`/`x86_64` release bundle remain out of scope for this initial scaffold.
