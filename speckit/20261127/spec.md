# Auralita Product Specification

## 1. Problem Statement

Music listeners need a responsive, native-feeling player that combines reliable
Plex access with high-quality local and streamed playback. The product must
support a broad range of audio formats and audiophile playback features without
blocking the interface, while preserving user privacy and adapting its resource
use to the device.

Auralita will provide a shared, platform-independent music core and native
cross-platform application shells. The initial product target is macOS 27 on
Apple Silicon and Intel Macs. Windows and mobile platforms are planned targets;
their implementation must follow the platform boundaries and requirements
below.

## 2. Goals

- Deliver a native desktop player using Avalonia UI and modern .NET with
  NativeAOT-compatible architecture; do not use an Electron/web wrapper.
- Make local and Plex music browsing, searching, queuing, and playback feel
  immediate while keeping audio work off the UI thread.
- Reliably decode and play all required formats without user-installed codec
  packs.
- Provide audiophile playback controls and integrate with operating-system
  media controls and audio outputs.
- Keep credentials secure, data local-first, and network access limited to
  user-configured services.
- Adapt caching, indexing, and DSP resource use to the supported hardware tier.

## 3. Users and Use Cases

1. **Plex listener:** Signs in through Plex PIN OAuth, discovers servers, browses
   a library, plays music, and sees playback state reported to Plex.
2. **Local-library listener:** Indexes local watch folders, searches tracks,
   builds a queue, and plays supported files offline.
3. **Audiophile listener:** Enables gapless playback, ReplayGain/loudness
   leveling, crossfade, parametric EQ, and bit-perfect or exclusive output when
   the device supports it.
4. **On-the-go listener:** Caches or downloads music for offline playback and
   continues listening across network changes.
5. **macOS system-controls user:** Controls playback and views current-track
   metadata from hardware media keys, Control Center, Lock Screen, or AirPlay
   routing.
6. **Constrained-device user:** Uses the player within the memory, buffering,
   and background-indexing limits for their hardware tier.

## 4. Functional Requirements

### 4.1 Application and Architecture

- **FR-APP-1:** The application UI shall use Avalonia UI on modern .NET and
  shall be compatible with NativeAOT for supported targets.
- **FR-APP-2:** Shared business logic—including Plex access, local indexing,
  queue state, and DSP orchestration—shall reside in a platform-agnostic core.
- **FR-APP-3:** Platform capabilities shall be supplied through explicit
  interfaces, including `IMediaSessionService`, `IAudioOutputBackend`, and
  `ISecureCredentialStore`.
- **FR-APP-4:** The UI shall remain responsive during decoding, DSP, indexing,
  and network operations. UI updates shall be dispatched through the Avalonia
  Dispatcher.

### 4.2 Playback and Audio

- **FR-AUDIO-1:** Decoding and DSP shall run outside the UI thread using FFmpeg
  (`libavcodec`, `libavformat`, `libswresample`) and FluidSynth for MIDI and
  SoundFont synthesis.
- **FR-AUDIO-2:** The player shall decode and play MP3, WAV, FLAC, OGG, WMA,
  APE, M4A, AAC, MP2, AMR, MID, M4R, MMF, WV, AIF, AIFF, AU, RA, MKA, AC3, and
  CAF without external codec packs.
- **FR-AUDIO-3:** Playback shall support gapless transitions, sample-rate
  switching and bit-perfect/exclusive output where supported, ReplayGain or
  loudness leveling, crossfade, and parametric EQ.
- **FR-AUDIO-4:** The queue shall support responsive track manipulation,
  seeking, play, pause, stop, next, and previous actions.
- **FR-AUDIO-5:** macOS shall use CoreAudio or AudioQueue. Future Windows and
  Android implementations shall use WASAPI and AAudio, respectively.

### 4.3 Plex and Library

- **FR-LIB-1:** Users shall be able to authenticate to Plex using the Plex.tv
  PIN OAuth flow.
- **FR-LIB-2:** The player shall discover multiple Plex servers and attempt
  direct local connections with relay fallback when necessary.
- **FR-LIB-3:** Users shall be able to browse and search local and remote
  libraries and create or manage queues and playlists.
- **FR-LIB-4:** Plex features shall include Sonic Analysis integrations
  (including Sonic Sage, Mood/Style radio, and Track Radio), subject to
  availability from the configured Plex service.
- **FR-LIB-5:** Users shall be able to cache or download supported content for
  offline use. Playback timeline and scrobble state shall be reported to Plex.
- **FR-LIB-6:** Search shall cover local and remote music. The interface shall
  also provide synchronized lyrics from LRC files or embedded tags and a
  mini-player mode.

### 4.4 macOS Integration

- **FR-MAC-1:** macOS integration shall use MediaPlayer.framework, including
  `MPRemoteCommandCenter` and `MPNowPlayingInfoCenter`, for hardware media
  controls and Now Playing metadata.
- **FR-MAC-2:** The player shall publish track title, artist, album artwork,
  duration, and elapsed time to supported system surfaces.
- **FR-MAC-3:** The player shall support system output routing, including
  AirPlay 2, and handle output-device changes during playback.
- **FR-MAC-4:** Plex tokens and local server credentials shall be stored
  exclusively in macOS Keychain through Security.framework.

### 4.5 Persistence, Privacy, and Recovery

- **FR-DATA-1:** SQLite in WAL mode shall persist offline metadata, play queues,
  and download manifests. Schema migrations shall be deterministic and
  forward-only.
- **FR-PRIV-1:** The application shall not include third-party analytics SDKs
  or telemetry.
- **FR-PRIV-2:** Network calls shall be limited to user-configured Plex
  servers, Plex.tv authentication endpoints, and user-approved metadata or
  lyrics providers.
- **FR-REC-1:** Decoder errors shall be logged with the offending file URI,
  reported through a non-blocking notification, and shall not crash the UI.
  The player shall advance to the next valid track within 250 ms.
- **FR-REC-2:** On audio-device removal, playback shall pause cleanly, preserve
  the exact millisecond seek position, release the stale audio stream, and wait
  for user resume or default-output rebinding.
- **FR-REC-3:** If SQLite integrity validation fails at startup, the application
  shall back up the corrupt database with a timestamped `.corrupt` suffix,
  rebuild the database, and re-sync state from Plex and local watch folders.

## 5. Non-Functional Requirements

### 5.1 Performance and Resource Use

- **NFR-PERF-1:** Cold start shall complete in under one second.
- **NFR-PERF-2:** On Tier 1 hardware, idle/active memory shall remain below
  120 MB/350 MB. The player shall pre-buffer the next three tracks, use 32-bit
  float internal DSP, perform concurrent library scans, and offer bit-perfect
  exclusive DAC mode where supported.
- **NFR-PERF-3:** On Tier 2 hardware, idle/active memory shall remain below
  85 MB/220 MB. The player shall use a single-track ring buffer and throttle
  SQLite WAL indexing to at most two worker threads.
- **NFR-PERF-4:** On future Tier 3 mobile hardware, idle/active memory shall
  remain below 60 MB/150 MB, with disk-backed streaming cache and battery-aware
  network polling.
- **NFR-PERF-5:** Audio callbacks shall be lock-free and shall perform no heap
  allocations, mutex locking, or disk I/O.
- **NFR-PERF-6:** macOS hardware play/pause, next, previous, and scrubbing
  commands shall respond within 50 ms.

### 5.2 Security and Privacy

- **NFR-SEC-1:** C# projects shall enable nullable reference types and treat
  warnings as errors.
- **NFR-SEC-2:** Native bindings shall avoid reflection-based P/Invoke and use
  `[LibraryImport]` source generation where applicable to preserve
  NativeAOT compatibility.
- **NFR-SEC-3:** Secrets shall not be stored in application preferences or
  plaintext files. On macOS, they shall be stored in Keychain.
- **NFR-SEC-4:** Distribution shall use a hardened, signed and notarized
  macOS application package with only required entitlements.

### 5.3 Compatibility and Maintainability

- **NFR-COMP-1:** The primary release target shall be macOS 27 on Apple Silicon
  and Intel. Windows, iOS, iPadOS, and Android are future targets and shall
  reuse the shared core where practical.
- **NFR-COMP-2:** The macOS release shall be a universal `arm64` and `x86_64`
  application bundle.
- **NFR-MAINT-1:** Native interop and application builds shall be reproducible
  using documented/pinned dependencies; major dependency upgrades shall not be
  used as unverified workarounds.
- **NFR-MAINT-2:** No credentials, provisioning profiles, or compiled native
  library binaries shall be committed to source control.

## 6. Acceptance Criteria

A release candidate is acceptable when all applicable checks pass:

1. **Format coverage:** An automated golden-sample suite verifies decoding for
   each of the 21 required formats.
2. **Gapless playback:** Consecutive lossless tracks transition without an
   audible click or silence gap greater than 1 ms.
3. **Seeking stability:** One hundred rapid random seeks on VBR MP3, FLAC, APE,
   and MKA complete without decoder deadlock or memory leak.
4. **System controls:** macOS play/pause, next, previous, and scrubbing respond
   within 50 ms; Now Playing surfaces show correct artwork, title, artist,
   duration, and elapsed time.
5. **Output changes:** Switching among built-in speakers, AirPods, USB DAC, and
   AirPlay 2 mid-track does not crash the application and playback continues
   or recovers cleanly.
6. **Plex authentication:** PIN OAuth succeeds, the token is stored in
   Keychain, and the user remains authenticated after an application restart.
7. **Plex connectivity:** Switching networks can fall back from a direct local
   HTTPS connection to Plex Relay.
8. **Timeline reporting:** Plex receives accurate `playing`, `paused`, and
   `stopped` timeline state transitions.
9. **Failure recovery:** Malformed media advances to the next valid track
   within 250 ms without crashing the UI. Device removal preserves the seek
   position. A corrupt database is backed up and rebuilt at startup.
10. **Resource limits:** Measurements on representative devices confirm the
    idle and active memory ceilings and tier-specific behaviors in the
    non-functional requirements.
11. **Build quality:** CI builds supported targets with nullable analysis and
    warnings-as-errors enabled; automated tests pass.

## 7. Scope Boundaries

### In Scope

- Native Avalonia desktop application and shared .NET core.
- macOS 27 playback, system media controls, Keychain credential storage, and
  universal app packaging.
- Local music indexing and playback, required codec support, queues, search,
  lyrics, playlists, and offline caching/downloads.
- Plex authentication, server discovery, playback, radio/Sonic integrations,
  timeline reporting, and relay fallback.
- Tier-aware resource management, SQLite persistence, and the recovery
  behaviors specified above.
- Architecture that permits future Windows and mobile platform shells.

### Out of Scope for the Initial Release

- Shipping production Windows, iOS, iPadOS, or Android applications; these are
  future targets.
- Linux as a committed release target; it is mentioned as constrained hardware
  in the constitution but no Linux-specific audio backend or delivery target is
  defined.
- Building or operating Plex servers or third-party metadata/lyrics services.
- Bundling proprietary codec packs or requiring users to install codec packs.
- Third-party analytics, advertising, or undisclosed network services.
- Replacing Plex, Apple Music, or Spotify services or providing their
  subscription catalogs; feature-parity language describes user experience,
  not service ownership or catalog access.