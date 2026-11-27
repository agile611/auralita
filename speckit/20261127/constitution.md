# Project Constitution: Native Cross-Platform Audiophile & Plex Music Player

**Version:** 1.0.0  
**Status:** Immutable Baseline  
**Primary Target:** macOS 27 (Apple Silicon & Intel)  
**Future Targets:** iOS, iPadOS, Android, Windows  

---

## 1. Architectural Pillars

### 1.1 Native UI & Cross-Platform Framework Strategy
* **Framework Mandate:** **Avalonia UI (.NET 9 / .NET 10 NativeAOT)** is designated as the core application framework. Electron is explicitly rejected to eliminate Chromium memory bloat, guarantee sub-second cold starts, and deliver compiled native binaries without web wrappers.
* **Single-Codebase Core, Native Shell:** Business logic, Plex API clients, local library indexing, DSP pipelines, and queue state reside in a shared, platform-agnostic `.NET` core library. Platform-specific OS integrations are injected via strict dependency interfaces (`IMediaSessionService`, `IAudioOutputBackend`, `ISecureCredentialStore`).

### 1.2 Audio Engine & Codec Architecture
* **Decoupled Native Audio Core:** Playback is decoupled from the UI thread. Audio decoding and DSP run on a dedicated real-time priority thread using **FFmpeg (`libavcodec`, `libavformat`, `libswresample`)** paired with **FluidSynth** (for MIDI/SoundFont synthesis) and **CoreAudio / AudioQueue** (macOS) / **WASAPI** (Windows) / **AAudio** (Android).
* **Universal Format Guarantee:** Every build must natively decode and play: `MP3`, `WAV`, `FLAC`, `OGG`, `WMA`, `APE`, `M4A`, `AAC`, `MP2`, `AMR`, `MID`, `M4R`, `MMF`, `WV`, `AIF`, `AIFF`, `AU`, `RA`, `MKA`, `AC3`, and `CAF` without external codec packs.
* **Audiophile Playback Pipeline:** Must support gapless playback, bit-perfect sample-rate switching (exclusive/hog mode where supported), ReplayGain / Loudness leveling, crossfading, and parametric EQ.

### 1.3 macOS 27 System Integration
* **Media & Hardware Controls:** First-class bindings to `MediaPlayer.framework` (`MPRemoteCommandCenter` and `MPNowPlayingInfoCenter`) to support hardware media keys, Control Center Now Playing widget, AirPlay 2 routing, Lock Screen controls, and Apple Silicon/Headphone spatial & handoff events.
* **Sandboxing & Security:** Tokens (Plex OAuth/PIN tokens, local server credentials) must be stored exclusively in the **macOS Keychain** (`Security.framework`).

### 1.4 Feature Parity & UX Pillars
* **Plex Integration (First-Class Citizen):** Full Plex.tv OAuth PIN authentication, multi-server discovery (local direct IP + relay fallback), Sonic Analysis integration (Sonic Sage, Mood/Style radio, Track Radio), offline caching/downloads, and scrobbling/timeline reporting.
* **Streaming-Class UX (Apple Music / Spotify Parity):** Instantaneous local + remote search, collaborative/smart playlists, synchronized lyrics (`LRC` / embedded tags), mini-player mode, and zero-latency queue manipulation.

---

## 2. Hardware Tier Classifications

The application must dynamically adapt its caching, DSP, and indexing footprint across three hardware profiles:

| Tier | Representative Hardware | Memory Budget (Idle / Active) | Audio & Indexing Behavior |
| :--- | :--- | :--- | :--- |
| **Tier 1: Flagship Desktop / Workstation** | Apple Silicon (M1–M4+), 16 GB+ RAM, macOS 27 | < 120 MB / < 350 MB | Full RAM pre-buffering (next 3 tracks), 32-bit float internal DSP, concurrent multi-threaded library scanning, bit-perfect exclusive DAC mode. |
| **Tier 2: Legacy / Constrained Desktop** | Intel Macs (2013–2020), low-power Windows/Linux devices, 8 GB RAM | < 85 MB / < 220 MB | Single-track ring buffer, throttled background SQLite WAL indexing (max 2 worker threads), standard sample rate mixing. |
| **Tier 3: Mobile & Tablet (Future)** | iPhone, iPad (M1+), Android devices | < 60 MB / < 150 MB | Aggressive disk-backed streaming cache, battery-aware network polling, OS-managed background audio suspension hooks. |

---

## 3. Engineering Standards

* **Language & Runtime:** C# 13+ with Nullable Reference Types (`<Nullable>enable</Nullable>`) and `TreatWarningsAsErrors` enforced across all projects.
* **Native Interop:** Zero reflection-based P/Invoke. All C/C++ bindings (`FFmpeg`, `CoreAudio`, `MediaPlayer`, `Security`) must use `[LibraryImport]` source generators to guarantee full **NativeAOT** compatibility.
* **Concurrency Model:** UI updates run strictly on the Avalonia Dispatcher. Audio buffer callbacks must be **lock-free** (ring buffers / atomic primitives)—no heap allocations, mutex locks, or disk I/O permitted on the real-time audio thread.
* **Local Persistence:** SQLite (via `Microsoft.Data.Sqlite` in WAL mode) for offline metadata cache, play queues, and download manifests. Schema migrations must be deterministic and forward-only.
* **Zero Telemetry / Local-First Privacy:** No third-party analytics SDKs. Network calls are restricted strictly to user-configured Plex servers, Plex.tv auth endpoints, and user-approved metadata/lyrics providers.

---

## 4. Testing & Validation Checklists

Every Pull Request and release candidate must pass the following gates:

### 4.1 Codec & Audio Matrix Gate
- [ ] Automated decode verification across a golden sample suite containing all **21 required formats** (`MP3`, `WAV`, `FLAC`, `OGG`, `WMA`, `APE`, `M4A`, `AAC`, `MP2`, `AMR`, `MID`, `M4R`, `MMF`, `WV`, `AIF`, `AIFF`, `AU`, `RA`, `MKA`, `AC3`, `CAF`).
- [ ] Gapless transition test: zero audible click or silence gap (> 1 ms) between consecutive lossless tracks.
- [ ] Seek stress test: 100 rapid random seeks on VBR MP3, FLAC, APE, and MKA without decoder deadlock or memory leak.

### 4.2 macOS 27 Native Integration Gate
- [ ] Hardware Play/Pause, Next, Previous, and Scrubbing respond within `< 50 ms` via `MPRemoteCommandCenter`.
- [ ] Album art, track title, artist, duration, and elapsed time render accurately in macOS Control Center and Lock Screen.
- [ ] Audio output hot-swap test: switching between built-in speakers, AirPods, USB DAC, and AirPlay 2 mid-track continues playback seamlessly without crashing.

### 4.3 Plex Protocol Gate
- [ ] Plex PIN OAuth login succeeds, persists token in macOS Keychain, and survives app restart without re-authentication.
- [ ] Graceful fallback from direct local HTTPS connection to Plex Relay when switching networks (Wi-Fi to cellular/VPN).
- [ ] Playback timeline (`:/timeline`) reports state transitions (`playing`, `paused`, `stopped`) accurately to Plex Media Server.

---

## 5. Git Discipline & Commit Protocol

* **Branching Strategy:** Trunk-based development on `main`. Short-lived feature branches (`feat/...`), bug fixes (`fix/...`), native bindings (`interop/...`), and architectural docs (`docs/...`).
* **Conventional Commits (Strict):** All commits must follow `<type>(<scope>): <imperative description>`:
  * `feat(audio): add bit-perfect CoreAudio device hog mode`
  * `fix(plex): handle TLS certificate pinning for local server IPs`
  * `interop(macos): bind MPNowPlayingInfoCenter artwork metadata`
  * `test(codecs): add golden samples for MMF, RA, and CAF formats`
* **Atomic Commits:** Each commit must compile cleanly and pass unit tests independently. Native C/Objective-C shim changes must be isolated from UI commits.
* **No Secrets or Binaries:** Never commit `.p12`, provisioning profiles, API keys, or compiled `.dylib`/`.a` binaries directly to Git; fetch native dependencies via reproducible build scripts or package manifests.

---

## 6. Environment & Tooling Standards

* **Build & SDK Baseline:**
  * `.NET SDK` (LTS / latest supporting NativeAOT for `osx-arm64` and `osx-x64`)
  * `Xcode Command Line Tools` & `macOS 27 SDK` (for Objective-C/Swift native bridge compilation and `codesign`)
  * `Homebrew` packages: `ffmpeg`, `fluid-synth`, `pkg-config` (pinned versions via `Brewfile`)
* **IDE & Automation:** Visual Studio Code (with C# Dev Kit / Ionide / `clangd` for native shims) and CLI-first build orchestration (`dotnet build`, `dotnet test`, `Makefile` / `Taskfile.yml`) requiring zero GUI interaction in CI/CD.
* **Packaging & Distribution:** Universal macOS `.app` bundle (`arm64` + `x86_64` via `lipo`) packaged into a signed, notarized `.dmg` with hardened runtime enabled (`com.apple.security.device.audio-input` excluded unless needed; network client + user-selected file read/write entitlements only).

---

## 7. Anti-Loop Guard & Recovery Procedures

### 7.1 Development & AI-Agent Anti-Loop Guard
* **3-Attempt Circuit Breaker:** When debugging native interop (`P/Invoke`, `CoreAudio`, `FFmpeg` memory marshaling) or Avalonia layout issues, if **3 consecutive iterations** fail to resolve the same compiler error, segmentation fault, or test failure:
  1. **HALT** immediate code edits.
  2. **ISOLATE** the failure into a minimal standalone reproduction (e.g., a 50-line console harness testing the raw C/FFmpeg/CoreAudio call outside Avalonia).
  3. **DIAGNOSE** root cause via `lldb`, `leaks`, or `Instruments` (Time Profiler / Allocations) before touching application code again.
* **No Blind Dependency Upgrades:** Do not bump Avalonia, .NET, or FFmpeg major versions mid-feature to work around a bug without documenting a reproduction and rollback commit.

### 7.2 Runtime Failure & Recovery Procedures
* **Corrupt Media / Decoder Panic:** If `FFmpeg` encounters a malformed frame or unsupported sub-codec inside any of the 21 containers, the audio engine must catch the error code, log the offending file URI, emit a non-blocking toast notification, and automatically advance to the next valid track within `250 ms`—never crashing the UI process.
* **Audio Device Disconnect / CoreAudio Stale Stream:** Upon receiving a `kAudioHardwarePropertyDevices` removal interrupt, the engine immediately pauses playback cleanly, preserves exact millisecond seek position, releases the stale `AudioUnit`, and awaits user resume or default output re-binding.
* **SQLite / Cache Corruption:** If local SQLite integrity check (`PRAGMA integrity_check`) fails on startup, automatically back up the corrupt file (`library.db.corrupt.<timestamp>`), rebuild a fresh database, and re-sync state from Plex and local watch folders transparently.