# Auralita Requirement Verification Checklist

This matrix maps each requirement in [spec.md](./spec.md) to a verification
method and a pass/fail rule. “Deferred” means it is not a gate for the initial
macOS release; retain it for the future platform milestone. Performance tests
whose protocol is awaiting stakeholder approval are blocked release gates, not
waived requirements. See [clarifications.md](./clarifications.md).

## Functional Requirements

| Requirement | Verification method | Pass criteria |
| --- | --- | --- |
| FR-APP-1 | Publish and launch the app for `osx-arm64` and `osx-x64` using the pinned NativeAOT toolchain. | Both architecture-specific builds succeed and launch; UI uses Avalonia and no Electron/web shell is present. |
| FR-APP-2 | Review project references and exercise core services with test adapters. | Core contains shared Plex, indexing, DSP orchestration, and queue logic; core builds/tests without referencing UI or platform implementations. |
| FR-APP-3 | Contract-test composition with fake media-session, audio-output, and credential-store adapters. | Each required capability is injected through an explicit interface; no platform-specific implementation is required by core tests. |
| FR-APP-4 | Run UI responsiveness tests while decoding, indexing, and network operations are stressed; inspect dispatcher usage. | UI updates are dispatched through Avalonia; no tested background operation blocks UI interaction or executes UI updates off-dispatcher. |
| FR-AUDIO-1 | Inspect thread ownership and run decoder/DSP tests, including MIDI through FluidSynth. | FFmpeg decoding and FluidSynth MIDI synthesis run outside the UI on the dedicated high-priority worker, fed by bounded source-I/O queues; failures are returned to the playback coordinator. Deadline behavior meets the approved C-16 protocol. |
| FR-AUDIO-2 | Automated golden-sample decode matrix for MP3, WAV, FLAC, OGG, WMA, APE, M4A, AAC, MP2, AMR, MID, M4R, MMF, WV, AIF, AIFF, AU, RA, MKA, AC3, and CAF. | Each approved fixture decodes to expected audio properties without an external codec pack. Fixture profiles and licenses are approved per C-13. |
| FR-AUDIO-3 | Feature tests for gapless transition, source-rate switching, supported exclusive/bit-perfect mode, ReplayGain/loudness, crossfade, and parametric EQ. | Each supported feature produces the requested behavior; unsupported hardware or processing conflicts are reported rather than falsely identified as bit-perfect. Gapless thresholds are tested per AC-2. |
| FR-AUDIO-4 | UI and service integration tests for queue editing, seeking, play, pause, stop, next, and previous. | Every action updates queue/playback state correctly and remains usable without waiting for remote work. |
| FR-AUDIO-5 | Verify macOS backend integration; future-platform contract suites for WASAPI and AAudio. | macOS uses CoreAudio or AudioQueue in initial release. Windows/WASAPI and Android/AAudio are explicitly deferred and pass their platform gates before those releases. |
| FR-LIB-1 | End-to-end Plex PIN OAuth test, restart app, and inspect credential-store integration. | Login succeeds, credential is stored in Keychain, and authentication survives restart without storing the token in SQLite or logs. |
| FR-LIB-2 | Integration test with multiple discovered servers and controlled direct-route failure. | Multiple servers are discovered; a reachable direct route is preferred and relay fallback works when direct access fails and relay is available. |
| FR-LIB-3 | Integration tests over local and Plex catalogs for browse, search, queue creation, and playlist create/manage. | Local and remote items are available through the supported catalog paths; users can create/manage queues and playlists supported by the source. |
| FR-LIB-4 | Capability tests against supported Plex configurations and unavailable-feature cases. | Sonic Sage, Mood/Style radio, and Track Radio work where available; unavailable capabilities are identified without breaking ordinary browse/playback. |
| FR-LIB-5 | Offline availability/download tests plus Plex timeline/scrobble integration tests. | Permitted content remains playable offline; timeline reports playing, paused, and stopped accurately. Unsupported download cases are surfaced, not silently promised. |
| FR-LIB-6 | Search tests across local/remote catalogs, LRC and embedded-lyrics fixtures, and mini-player UI tests. | Local search and queue interaction do not wait for network results; supported lyric sources synchronize correctly; mini-player controls the same playback state. Numeric search latency awaits C-03. |
| FR-MAC-1 | macOS integration test verifies registration and handling of remote commands and Now Playing publication. | `MPRemoteCommandCenter` and `MPNowPlayingInfoCenter` are used through the platform adapter; supported commands reach the playback service. |
| FR-MAC-2 | Inspect Control Center/Lock Screen Now Playing data while playing tracks with complete and partial metadata. | Title, artist, artwork, duration, and elapsed time are accurate where source metadata exists; absent values do not crash or publish stale data. |
| FR-MAC-3 | Hardware/device integration test switching among built-in output, AirPods, USB DAC, and AirPlay 2 during playback. | App does not crash; available routes continue playback or follow the documented pause/preserve-position recovery behavior. |
| FR-MAC-4 | Keychain integration test for Plex and local-server credentials, restart, sign-out, and storage inspection. | Secrets exist only in Keychain; sign-out removes credentials according to approved policy; neither database nor logs contain secret values. |
| FR-DATA-1 | Repository tests inspect WAL mode, migration fixtures, and persistence across restart. | Metadata, queues, and download manifests persist in WAL mode; migrations are deterministic and forward-only. |
| FR-PRIV-1 | Dependency/SBOM review and outbound traffic inspection in representative sessions. | No third-party analytics SDK or telemetry is present and no analytics/telemetry traffic is emitted. |
| FR-PRIV-2 | Instrumented network integration tests for login, Plex browsing/streaming, relay, and approved providers. | Requests go only to Plex.tv auth, configured Plex servers/relay, and explicitly approved providers; all other test destinations are blocked or absent. |
| FR-REC-1 | Feed malformed/corrupt fixtures; observe local logs, notification, next-track start, and process stability; inspect diagnostic exports. | Access-controlled local diagnostic log includes the offending URI with sensitive query credentials redacted; exports redact personal paths unless consented. Notification is non-blocking, UI remains alive, and next valid track advances within 250 ms. |
| FR-REC-2 | Remove active output during playback and inspect playback state, seek position, and native stream lifetime. | Playback pauses cleanly; millisecond position is preserved; stale stream is released; user resume or default-output rebinding works without a crash. |
| FR-REC-3 | Start with a deliberately corrupt test database and available/unavailable sync sources. | Timestamped `.corrupt` backup remains intact; a fresh valid database is created; reachable sources are re-synced and unavailable sources are reported/retried. |

## Non-Functional Requirements

| Requirement | Verification method | Pass criteria |
| --- | --- | --- |
| NFR-PERF-1 | Measure cold launch from process start to usable primary UI on agreed release hardware. | Cold start is under 1 second using the reset procedure and reporting statistic approved under C-18. |
| NFR-PERF-2 | Measure process resident memory at idle and under Tier 1 playback/prebuffer/index workload; inspect buffering/DSP/index concurrency. | Idle is below 120 MB and active below 350 MB; Tier 1 pre-buffers the next three tracks, uses 32-bit float DSP and concurrent scans, and offers exclusive mode for supported DACs. |
| NFR-PERF-3 | Measure process resident memory and worker counts on representative Tier 2 hardware/workload. | Idle is below 85 MB and active below 220 MB; one-track ring buffer and no more than two indexing workers are observed. |
| NFR-PERF-4 | Future mobile workload and platform lifecycle tests. | Deferred until Tier 3 release; idle below 60 MB and active below 150 MB, disk-backed streaming cache, battery-aware polling, and OS-managed background suspension are verified before release. |
| NFR-PERF-5 | Instrument callback allocations/locks and audit callback call graph; run under stress. | No heap allocation, mutex lock, or disk I/O occurs in audio callbacks; callbacks consume preallocated buffers only. |
| NFR-PERF-6 | Timestamp macOS remote command receipt and playback-engine application for play/pause, next, previous, and scrubbing. | Each command is applied within 50 ms using the measurement boundary approved under C-11. |
| NFR-SEC-1 | Inspect project settings and run the pinned build with warnings treated as errors. | Nullable reference types are enabled and build has zero warnings/errors for supported projects. |
| NFR-SEC-2 | Static source review/analyzer plus NativeAOT build of native bindings. | No reflection-based P/Invoke is used where `[LibraryImport]` applies; bindings compile in NativeAOT release builds. |
| NFR-SEC-3 | Search database, preferences, logs, crash artifacts, and app files after authentication. | No secrets are stored outside macOS Keychain; no secret values appear in logs or diagnostics. |
| NFR-SEC-4 | Inspect signed app entitlements, hardened-runtime signature, notarization result, and `.dmg`. | App is signed, hardened, notarized, and includes only required entitlements; no audio-input entitlement unless a validated feature requires it. |
| NFR-COMP-1 | Verify macOS release target and maintain future-platform conformance suites. | Initial release supports macOS 27 on Apple Silicon and Intel; Windows/iOS/iPadOS/Android are not represented as shipped until their own gates pass. |
| NFR-COMP-2 | Inspect bundle executable and each bundled native library with architecture tooling; launch both slices. | Universal app has runnable `arm64` and `x86_64` slices and all required native dependencies resolve from the bundle. |
| NFR-MAINT-1 | Rebuild from clean checkout using pinned manifests/scripts; compare dependency inputs and outputs. | Build inputs and native dependencies are documented/pinned and reproducible; no unverified major-version workaround is required. |
| NFR-MAINT-2 | Repository secret and binary scan plus review of staged release sources. | No credentials, provisioning profiles, or compiled `.dylib`/`.a` dependencies are committed. |

## End-to-End Acceptance Gates

- [ ] **AC-1 Codec coverage:** all 21 required formats pass the approved
  golden-sample suite.
- [ ] **AC-2 Gapless:** no audible click under the approved listening protocol
  and no measured silence interval greater than 1 ms between consecutive
  lossless tracks.
- [ ] **AC-3 Seeking:** 100 rapid random seeks each on VBR MP3, FLAC, APE, and
  MKA complete without decoder deadlock or memory leak.
- [ ] **AC-4 System controls:** hardware commands meet the 50 ms criterion;
  supported Now Playing surfaces show correct artwork, title, artist, duration,
  and elapsed time.
- [ ] **AC-5 Output changes:** output switching among built-in speakers,
  AirPods, USB DAC, and AirPlay 2 does not crash and continues or recovers
  according to the device-switch contract.
- [ ] **AC-6 Plex authentication:** PIN OAuth succeeds, Keychain persistence
  survives app restart, and no secret is written to app data/logs.
- [ ] **AC-7 Plex connectivity:** network change triggers direct-to-relay
  fallback when relay is available.
- [ ] **AC-8 Timeline:** Plex receives accurate playing, paused, and stopped
  transitions.
- [ ] **AC-9 Recovery:** malformed media advances within 250 ms; device removal
  preserves position; corrupt database is backed up and rebuilt.
- [ ] **AC-10 Resource limits:** approved measurements meet all release-tier
  memory ceilings and behavior requirements.
- [ ] **AC-11 Build quality:** supported targets build with nullable analysis
  and warnings-as-errors; automated tests pass.

## Release-Gate Dependencies and Sign-Off

- [ ] Stakeholders approve the runtime, search, memory, offline, DSP, latency,
  platform-capability, codec-profile, scheduling, diagnostics, and cold-start
  decisions identified in
  [clarifications.md](./clarifications.md).
- [ ] Hardware, licenses, and test fixtures required for each applicable gate
  are available.
- [ ] Future-platform requirements are labeled deferred rather than counted as
  macOS release failures or silently treated as satisfied.
