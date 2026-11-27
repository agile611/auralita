# Auralita Clarifications and Decisions

This document resolves ambiguities in [spec.md](./spec.md) against the
project baseline in [constitution.md](./constitution.md). “Decision” means the
recommended baseline for implementation. Items marked **Stakeholder
confirmation required** should be approved before they become release
commitments.

## 1. Open Questions and Resolutions

| ID | Open question | Resolution |
| --- | --- | --- |
| C-01 | Which platforms are part of the first release? The documents mention macOS, Windows, Linux, and mobile, but do not consistently distinguish supported hardware from shipping targets. | **Decision:** The initial releasable product is macOS 27 on Apple Silicon and Intel, as a universal app. Windows and mobile are future targets; Linux is not a committed target. Tier 2 resource constraints still inform design, but do not imply a Linux deliverable. |
| C-02 | Which .NET and C# versions are mandatory? The constitution names .NET 9 / .NET 10 and C# 13+, while the specification says modern .NET. | **Decision:** The initial project pins .NET SDK 10.0.404 and C# 14, satisfying the .NET 10 / C# 13+ baseline. Both `osx-arm64` and `osx-x64` NativeAOT publishes have been verified. Reassess the pinned release SDK only through an explicit dependency decision. |
| C-03 | What qualifies as “instantaneous” search and “zero-latency” queue manipulation? | **Decision:** Local results and queue edits must be applied without waiting for network work; remote results may arrive asynchronously and must not block local results or UI interaction. The specification does not currently promise a numeric search-latency threshold. **Stakeholder confirmation required:** set a measurable p95 target after a representative library size and network profile are agreed. |
| C-04 | What exactly does the memory budget measure, and which actions constitute idle and active use? | **Decision:** Treat the budgets as a per-process resident-memory ceiling, measured after startup has settled (idle) and during representative playback plus the tier's specified buffering/indexing workload (active). Exclude OS-wide cache and separately running Plex services. **Stakeholder confirmation required:** agree on test hardware, measurement tooling, workload duration, and whether native libraries are included in process memory before using this as a release gate. |
| C-05 | Does the gapless criterion mean no audible click, no silence longer than 1 ms, or both? | **Decision:** Both are required for consecutive lossless tracks: no audible transition click and no measured silence interval greater than 1 ms. Use a defined digital-output capture method for the silence measurement and a documented listening protocol for click assessment. |
| C-06 | How should audio-device changes behave when the requested output disappears? | **Decision:** If the active device is removed, pause, preserve the seek position to millisecond precision, release the stale stream, and wait for user resume or default-output rebinding. For a user-requested switch to an available device, continue playback where the backend permits; otherwise recover using the same pause-and-preserve behavior. This reconciles seamless output switching with the explicit device-removal recovery rule. |
| C-07 | Is Plex offline support a general download feature, or only caching for playback? Which sources may be retained? | **Decision:** Support offline playback only for content the user can access and that Plex or the local source permits the application to retain. Keep downloaded media distinct from transient streaming cache, expose its availability to the user, and remove it when the user requests deletion or signs out where applicable. Do not bypass Plex access controls or DRM. **Stakeholder confirmation required:** confirm exact Plex download/API support and sign-out retention policy before promising offline availability for every remote item. |
| C-08 | Do “collaborative/smart playlists” imply shared editing and playlist synchronization? | **Decision:** The initial scope guarantees playlist creation and management only where supported by the local library or configured Plex service. It does not promise collaborative editing, smart-playlist rule semantics, or conflict resolution until those behaviors and service support are specified. |
| C-09 | Are all referenced metadata, lyrics, radio, and Sonic features guaranteed regardless of Plex account, server version, or provider? | **Decision:** Features that depend on Plex server capabilities, account entitlements, or user-approved providers are capability-gated. Show unavailable states without breaking ordinary browsing or playback. Do not add a provider or network destination without user approval and privacy review. |
| C-10 | Which component owns sample-rate conversion, DSP order, and “bit-perfect” behavior? | **Decision:** The audio backend reports supported formats/rates and exclusive-mode availability. The playback pipeline preserves the source rate and avoids DSP/resampling in bit-perfect mode; if an enabled effect or device constraint requires processing, disclose that bit-perfect output is unavailable and use the configured standard-mix path. **Stakeholder confirmation required:** define effect ordering and user-facing behavior when EQ, ReplayGain, or crossfade conflicts with bit-perfect mode. |
| C-11 | Does “within 50 ms” measure command dispatch, visible UI response, or audible output change? | **Decision:** Measure time from receipt of the macOS remote command to the playback engine accepting and applying the state/seek change. Track audible-output latency separately; the 50 ms requirement is not an end-to-end guarantee of hardware acoustics. **Stakeholder confirmation required:** approve this interpretation and the test harness before treating it as a release gate. |
| C-12 | Which macOS APIs are responsible for AirPlay routing, spatial/audio handoff events, and the advertised system surfaces? | **Decision:** Use supported macOS APIs and system-managed routing; MediaPlayer.framework is the stated media-controls/Now Playing integration, not an assumed implementation of every routing capability. Validate each feature against the target macOS SDK and device capabilities, and expose unsupported features gracefully. **Stakeholder confirmation required:** confirm whether spatial-audio and handoff events are launch requirements or aspirational platform capabilities. |
| C-13 | What is the scope of “all 21 formats” for MIDI and format variants, and what does a successful decode prove? | **Decision:** The guarantee applies to the listed file/container formats using the bundled/reproducibly supplied decoder stack, with MIDI rendered through FluidSynth and an available SoundFont. Golden samples must identify codec/profile, sample rate, channel layout, and expected output; format names alone do not imply every possible codec variant. **Stakeholder confirmation required:** approve representative sample profiles and the SoundFont licensing/distribution approach. |
| C-14 | What happens when Plex or a local source is unavailable during database recovery? | **Decision:** Preserve the timestamped corrupt-database backup and create a valid fresh database. Re-sync reachable sources; report unavailable sources non-blockingly and retry on the next appropriate connection rather than claiming a complete restoration. Do not delete the backup automatically. |
| C-15 | What does “offline metadata cache” mean for deletion, sign-out, and local-file changes? | **Decision:** Treat metadata and download manifests as rebuildable cache/state, not the authoritative copy of user media. Keep local source files untouched. Remove cached credentials on sign-out; retain or remove downloaded media according to the policy resolved in C-07. **Stakeholder confirmation required:** approve a user-visible cache/download cleanup policy and any retention period. |
| C-16 | How can the constitution's dedicated real-time-priority decode/DSP thread coexist with the rule that audio callbacks do no I/O, allocation, or locking? | **Decision:** Keep source reads and demux/network I/O on bounded I/O workers. Feed compressed packets through bounded queues to a dedicated high-priority decode/DSP worker; the OS audio callback only copies preallocated PCM from the ring buffer. Validate decoder deadline behavior on target hardware. **Stakeholder confirmation required:** confirm that “real-time priority” means best-effort OS scheduling for this processing worker, not hard real-time guarantees. |
| C-17 | How can decoder diagnostics include the offending file URI without exposing private paths? | **Decision:** The local, access-controlled diagnostic log shall include the URI required by FR-REC-1. Redact credentials and sensitive query parameters, and redact or omit the path from any user-initiated diagnostic export unless the user explicitly consents. Logs are never sent automatically. |
| C-18 | How is the sub-second cold-start criterion measured? | **Decision:** Measure from process launch to the primary UI becoming usable, on agreed release hardware, with the application in a defined cold-start state. Record repeated samples and report the per-run results; **Stakeholder confirmation required:** approve the cold-start reset procedure, sample count, and summary statistic before release sign-off. |

## 2. Assumptions

- macOS 27 is the minimum and primary release target stated by the project,
  even though SDK and hardware availability must be verified when development
  and release work starts.
- “Universal macOS app” means the distributable application contains native
  `arm64` and `x86_64` slices; external native dependencies must also be
  available for each supported slice.
- The listed memory ceilings apply to the player process, including its loaded
  native libraries and buffers, rather than to the operating system or Plex
  server.
- Local playback remains available without Plex authentication or network
  access. Plex-dependent features degrade independently when offline.
- The player may use system-provided media routing and playback facilities,
  but must not claim device/API support that has not been validated.
- All third-party libraries, codec components, fonts/SoundFonts, and sample
  assets must have licenses compatible with development and distribution.

## 3. Dependencies Identified

- .NET SDK/runtime and NativeAOT support for macOS `arm64` and `x86_64`.
- Avalonia UI compatibility with the chosen .NET baseline and macOS release.
- FFmpeg libraries and their licensing, codec configuration, and reproducible
  availability for both macOS architectures.
- FluidSynth and a redistributable SoundFont for MIDI playback.
- Xcode command-line tools, the target macOS SDK, signing, notarization, and
  universal-binary packaging tools.
- macOS Keychain, MediaPlayer, CoreAudio/AudioQueue, and supported system
  routing APIs.
- Plex.tv authentication, Plex server discovery/relay, server API behavior,
  account capabilities, and Sonic feature availability.
- SQLite provider and deterministic migration strategy.
- Representative test devices and licensed golden samples for each required
  format, plus audio-output test hardware.
- User approval and availability of any external metadata or lyrics providers.

## 4. Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Target SDK/API availability or macOS version behavior differs from assumptions. | Verify against the actual target SDK early; isolate OS integrations behind interfaces and gate unsupported capabilities. |
| FFmpeg/FluidSynth builds, licensing, or NativeAOT interop prevent universal packaging. | Establish a per-architecture dependency and license audit; run decode and packaging smoke tests on both slices before feature completion. |
| Plex API, entitlement, or relay behavior varies by server/account/network. | Test supported server and account configurations; capability-detect and report unavailable features; keep local playback independent. |
| Performance and latency criteria cannot be reproduced without workload definitions. | Approve the test protocol and representative hardware before making these release gates; record measurements in CI or release evidence. |
| Bit-perfect output conflicts with DSP features or output-device limits. | Make the active output mode explicit, detect incompatibilities, and require a clear user choice rather than silently claiming bit-perfect playback. |
| Offline downloads create retention, privacy, or source-permission concerns. | Limit downloads to permitted content, provide clear storage and deletion controls, and resolve sign-out retention before release. |
| A corrupt database cannot be fully re-synced while its sources are offline. | Preserve the backup, rebuild a valid database, identify incomplete sources, and retry synchronization without blocking startup. |
| Broad codec requirements include uncommon profiles or samples with unclear redistribution rights. | Define profile coverage and licensing before assembling the golden suite; test the actual redistributable decoder build. |

## 5. Stakeholder Decisions

The following decisions should be confirmed before implementation planning or
before the affected release gate is committed:

1. **Runtime baseline:** Select the pinned .NET SDK/C# version at project
   kickoff (C-02).
2. **Search performance:** Choose the p95 local/remote search targets and
   representative library/network profile (C-03).
3. **Performance test protocol:** Approve devices, workload, measurement method,
   and process-memory accounting (C-04).
4. **Offline policy:** Confirm supported Plex download cases, sign-out behavior,
   and retention/deletion controls (C-07, C-15).
5. **Playlist scope:** Decide whether collaborative and smart playlists are
   launch features, and define synchronization/conflict semantics if included
   (C-08).
6. **Bit-perfect UX:** Decide how DSP conflicts are presented and whether
   processing is disabled automatically or only after user confirmation
   (C-10).
7. **Remote-command latency:** Approve the 50 ms measurement boundary and test
   method (C-11).
8. **macOS feature commitments:** Confirm whether spatial-audio and handoff
   support are required for the initial release (C-12).
9. **Codec acceptance matrix:** Approve representative codec profiles and
   SoundFont licensing/distribution (C-13).
10. **Audio scheduling:** Confirm the best-effort real-time scheduling
    interpretation and approve decoder deadline tests (C-16).
11. **Diagnostics privacy:** Approve local log retention/access and explicit
    consent behavior for path-bearing diagnostic exports (C-17).
12. **Cold-start protocol:** Approve cold-state setup, sample count, and
    reporting statistic for the one-second target (C-18).

