# Auralita Implementation Tasks

Tasks are ordered by dependency and grouped into milestones. Estimates are
indicative person-days for implementation and focused validation; they exclude
external approval delays, hardware procurement, and release-notarization wait
time. Owners are role assignments because individual contributors have not
been named. Acceptance evidence is recorded against
[checklist.md](./checklist.md).

## Execution Status

| Task | Status | Current blocker |
| --- | --- | --- |
| TSK-01 | **Done** | Foundation baseline selected; remaining feature/release decisions are tracked as explicit gates in clarifications. |
| TSK-02 | **In Progress** | Solution restores/builds; both macOS architecture publishes pass. CI workflow and clean-checkout gate remain. |
| TSK-03 | **In Progress** | Initial track/queue model is implemented; remaining application ports and contracts are outstanding. |
| TSK-04 | Pending | Not started; depends on TSK-03. |
| TSK-05 | Pending | Not started; depends on TSK-01, TSK-02, and TSK-03. |
| TSK-06 | Pending | Not started; depends on TSK-01, TSK-02, and TSK-03. |
| TSK-07 | Pending | Not started; depends on TSK-03 and TSK-05. |
| TSK-08 | Pending | Not started; depends on TSK-03, TSK-06, and TSK-07. |
| TSK-09 | Pending | Not started; depends on TSK-03, TSK-04, and TSK-06. |
| TSK-10 | **In Progress** | Queue UI and local AVFoundation play/stop are implemented; FFmpeg coverage, pause/seek, decoder pipeline, and catalog integrations remain. |
| TSK-11 | Pending | Not started; depends on TSK-03 and TSK-06. |
| TSK-12 | Pending | Not started; depends on TSK-07, TSK-09, and TSK-11. |
| TSK-13 | Pending | Not started; depends on TSK-04 and TSK-10 through TSK-12. |
| TSK-14 | Pending | Not started; depends on TSK-06, TSK-08, and TSK-10. |
| TSK-15 | Pending | Not started; depends on TSK-09 through TSK-11. |
| TSK-16 | Pending | Not started; depends on TSK-01 and TSK-07 through TSK-10. |
| TSK-17 | **In Progress** | Build and seven core tests pass; the full requirement matrix and hardware/integration gates remain. |
| TSK-18 | Pending | Not started; depends on TSK-02, TSK-05, TSK-06, TSK-08, TSK-14, and TSK-17. |
| TSK-19 | Pending | Not started; depends on TSK-17 and TSK-18. |

The initial app/core/test projects are present. Decoder, DSP, Plex, persistence,
indexing, system media controls, and the full release acceptance suite are not
implemented; do not mark those requirements as passed.

## Milestone 0 — Resolve Release Decisions

### TSK-01: Confirm release baselines and acceptance protocols

- **Description:** Select the project bootstrap baseline and record
  feature-specific decisions as release gates before their implementation.
  The initial runtime is pinned; unresolved search, memory, Plex offline,
  playlist, DSP, codec, scheduling, diagnostic, and release decisions remain
  explicitly gated rather than blocking the project scaffold.
- **Dependencies:** None.
- **Acceptance criteria:**
  - macOS-first scope and the pinned .NET 10.0.404 / C# 14 baseline are
    recorded and reproducible.
  - Unresolved feature and release criteria remain linked to named
    clarifications and cannot be reported as passed.
  - Plex retention, playlist behavior, and codec/SoundFont distribution are
    resolved before those features or release claims are completed.
- **Estimated effort:** 1–3 person-days, excluding stakeholder response time.
- **Owner:** Product owner with Architecture, Audio, and Release leads.

## Milestone 1 — Buildable Foundation

### TSK-02: Establish solution structure and reproducible builds

- **Description:** Create the Avalonia app, platform-neutral core, adapter/test
  project boundaries, pinned SDK/dependency manifests, CI build/test commands,
  nullable and warnings-as-errors settings, and architecture-specific
  NativeAOT smoke builds.
- **Dependencies:** TSK-01.
- **Acceptance criteria:**
  - Clean checkout restores and builds the initial solution for `osx-arm64`
    and `osx-x64`.
  - CI runs build and unit-test commands without GUI interaction.
  - Project references enforce the core/platform dependency direction.
- **Estimated effort:** 2–4 person-days.
- **Owner:** .NET Platform / Build Engineer.

### TSK-03: Define core domain and service contracts

- **Description:** Implement domain types and the core-facing contracts for
  playback, catalog, Plex gateway, persistence, offline content, media session,
  audio output, credentials, lyrics, and clocks.
- **Dependencies:** TSK-02.
- **Acceptance criteria:**
  - Core contracts compile independently of Avalonia, SQLite, Plex HTTP code,
    and macOS frameworks.
  - Async operations support cancellation and expose operational failures
    distinctly from success.
  - Audio buffer ownership, callback constraints, source identity, and playback
    state contracts are covered by contract tests.
- **Estimated effort:** 3–5 person-days.
- **Owner:** .NET Core / Domain Engineer.

### TSK-04: Implement SQLite persistence and recovery

- **Description:** Add the WAL database, initial schema/repositories,
  forward-only migration runner, integrity check, corrupt-database backup, and
  fresh-database recovery/re-sync orchestration.
- **Dependencies:** TSK-03.
- **Acceptance criteria:**
  - Metadata, queues, playlists, scan state, lyrics, and download manifests
    persist across restart.
  - Migration tests prove deterministic upgrade from supported prior schemas.
  - Corruption test preserves a timestamped backup, creates a valid database,
    and reports unavailable sync sources without claiming complete recovery.
  - No credential fields exist in the schema.
- **Estimated effort:** 3–6 person-days.
- **Owner:** Data / Persistence Engineer.

### TSK-05: Package native audio dependencies and bindings

- **Description:** Establish reproducible FFmpeg and FluidSynth builds or
  package inputs for both macOS architectures; implement NativeAOT-compatible
  bindings/loading and validate codec availability and SoundFont licensing.
- **Dependencies:** TSK-01, TSK-02, TSK-03.
- **Acceptance criteria:**
  - Both architecture-specific builds resolve bundled native dependencies
    without machine-specific absolute paths.
  - `[LibraryImport]` is used where applicable and NativeAOT smoke builds pass.
  - Licensed test assets/SoundFont are selected or the MIDI release gate is
    explicitly blocked pending selection.
  - Native library provenance, versions, and licenses are documented.
- **Estimated effort:** 5–10 person-days.
- **Owner:** Native Audio / Interop Engineer.

### TSK-06: Implement macOS security and file-access adapters

- **Description:** Implement Keychain credential storage, lifecycle/sign-out
  handling, user-selected library access, app-managed data locations, and
  minimal entitlement configuration.
- **Dependencies:** TSK-01, TSK-02, TSK-03.
- **Acceptance criteria:**
  - Credential read/write/delete integration tests pass across app restart.
  - Secrets never enter the database, preferences, logs, or diagnostics.
  - Local library access is limited to user-selected roots; source files are
    never modified by indexing.
  - Entitlements match the approved macOS feature set.
- **Estimated effort:** 3–6 person-days.
- **Owner:** macOS Platform / Security Engineer.

## Milestone 2 — Playback and Local Library

### TSK-07: Build the worker-based decode and DSP pipeline

- **Description:** Implement source readers, FFmpeg decoder lifecycle,
  FluidSynth MIDI rendering, bounded compressed-packet queues, dedicated
  high-priority decode/DSP worker, seek coordination, DSP chain, format
  negotiation, preallocated SPSC PCM ring buffer, and typed decoder errors.
- **Dependencies:** TSK-03, TSK-05.
- **Acceptance criteria:**
  - Decode and DSP run outside the UI and real-time output callback.
  - Source I/O/demux stays off the decode/DSP worker; deadline behavior passes
    the C-16 test protocol.
  - Callback path has no heap allocations, mutexes, disk/network I/O, or logging.
  - Seek, decoder failure, and track transition behavior pass focused tests.
  - All approved format fixtures can be decoded by the pipeline.
- **Estimated effort:** 8–14 person-days.
- **Owner:** Audio Engine Engineer.

### TSK-08: Implement macOS audio output and device recovery

- **Description:** Implement `IAudioOutputBackend` using CoreAudio or
  AudioQueue, including format negotiation, device enumeration, output
  selection, hot-swap detection, stream lifecycle, and position-preserving
  device-loss recovery.
- **Dependencies:** TSK-03, TSK-06, TSK-07.
- **Acceptance criteria:**
  - Playback works through built-in, AirPods, USB DAC, and available AirPlay
    outputs on the supported test matrix.
  - Device removal pauses, preserves millisecond position, releases stale
    resources, and supports the approved resume/rebind behavior.
  - Callback stress tests satisfy NFR-PERF-5.
- **Estimated effort:** 5–9 person-days.
- **Owner:** macOS Audio / CoreAudio Engineer.

### TSK-09: Implement local indexing and search

- **Description:** Add user-selected watch folders, bounded background
  scanning, metadata extraction, idempotent catalog updates, local queries,
  and scan progress/error reporting.
- **Dependencies:** TSK-03, TSK-04, TSK-06.
- **Acceptance criteria:**
  - User-selected folders can be scanned and rescanned without modifying
    source files or creating duplicate catalog entries.
  - Local search returns independently of remote requests.
  - Worker concurrency and memory behavior follow the selected hardware tier.
  - File access and malformed metadata failures are surfaced safely.
- **Estimated effort:** 4–8 person-days.
- **Owner:** Library / Data Engineer.

### TSK-10: Implement core playback and queue UI

- **Description:** Build Avalonia playback screens and view models for local
  browse, queue edits, play/pause/stop/next/previous, seeking, output selection,
  and playback error notifications.
- **Dependencies:** TSK-03, TSK-07, TSK-08, TSK-09.
- **Acceptance criteria:**
  - UI controls invoke the same playback services as system media commands.
  - Queue edits do not wait on remote/network operations.
  - UI state updates are dispatched through the Avalonia Dispatcher.
  - Decoder notifications are non-blocking and the interface remains usable
    during indexing and playback.
- **Estimated effort:** 6–10 person-days.
- **Owner:** Avalonia UI Engineer.

## Milestone 3 — Plex and macOS Integration

### TSK-11: Implement Plex authentication and server discovery

- **Description:** Implement PIN OAuth, credential-store integration,
  multi-server discovery, capability detection, direct-route selection, and
  relay fallback.
- **Dependencies:** TSK-03, TSK-06.
- **Acceptance criteria:**
  - PIN OAuth succeeds and persists only through Keychain.
  - Multiple servers and capabilities are represented without leaking tokens
    to logs or database.
  - Direct connection is preferred when reachable; relay fallback succeeds
    when direct access fails and relay is available.
  - Network loss does not block local playback or UI interaction.
- **Estimated effort:** 5–9 person-days.
- **Owner:** Plex / Network Services Engineer.

### TSK-12: Add Plex catalog, remote playback, and timeline reporting

- **Description:** Implement Plex library browsing/search integration,
  remote stream-source adaptation, timeline/scrobble reporting, Sonic/radio
  capability-gated features, and network-change recovery.
- **Dependencies:** TSK-07, TSK-09, TSK-11.
- **Acceptance criteria:**
  - Supported remote catalog items can be browsed and played through the shared
    playback pipeline.
  - Playing, paused, and stopped timeline transitions are accurate.
  - Sonic and radio features are gated by server/account capabilities and fail
    without breaking ordinary playback.
  - Remote results arrive asynchronously and do not block local results.
- **Estimated effort:** 6–10 person-days.
- **Owner:** Plex / Playback Integration Engineer.

### TSK-13: Implement offline cache, downloads, and user controls

- **Description:** Separate transient streaming cache from retained offline
  downloads; implement manifests, availability, user deletion, sign-out
  behavior, recovery from incomplete downloads, and user-facing download,
  offline-availability, and delete controls.
- **Dependencies:** TSK-04, TSK-10, TSK-11, TSK-12.
- **Acceptance criteria:**
  - Permitted downloaded content is playable offline and has accurate
    availability status.
  - Transient cache and retained downloads have distinct lifecycle behavior.
  - User deletion works; retention follows the policy approved in TSK-01.
  - Users can see offline availability and initiate/remove permitted downloads.
  - No access-control/DRM bypass is implemented.
- **Estimated effort:** 4–8 person-days.
- **Owner:** Offline Storage / Plex Engineer.

### TSK-14: Add macOS remote controls and Now Playing

- **Description:** Implement MediaPlayer.framework adapter for remote
  commands and Now Playing metadata, mapping system commands to core playback.
- **Dependencies:** TSK-06, TSK-08, TSK-10.
- **Acceptance criteria:**
  - Play/pause, next, previous, and scrubbing commands update engine state
    within the approved 50 ms measurement boundary.
  - Correct artwork, title, artist, duration, and elapsed time appear in
    supported system surfaces; stale metadata is cleared on track changes.
  - Adapter lifecycle and unsupported system capability behavior are tested.
- **Estimated effort:** 3–6 person-days.
- **Owner:** macOS Platform Engineer.

### TSK-15: Complete search, playlists, lyrics, and mini-player flows

- **Description:** Integrate local and remote search results, supported
  playlist operations, LRC/embedded lyrics, approved provider configuration,
  and mini-player mode.
- **Dependencies:** TSK-09, TSK-10, TSK-11.
- **Acceptance criteria:**
  - Local results and queue interactions remain available while remote search
    is pending or unavailable.
  - Lyrics synchronize against playback position for approved sources.
  - Playlist behavior matches the approved initial scope; unsupported
    collaborative/smart semantics are not presented as available.
  - Mini-player controls remain consistent with the main player and system
    controls.
- **Estimated effort:** 5–9 person-days.
- **Owner:** Avalonia UI / Library Engineer.

## Milestone 4 — Performance, Verification, and Release

### TSK-16: Implement hardware-tier resource profiles

- **Description:** Add tier-aware buffering, DSP/indexing concurrency, cache
  strategy, and measurement instrumentation for applicable desktop hardware.
- **Dependencies:** TSK-01, TSK-07, TSK-08, TSK-09, TSK-10.
- **Acceptance criteria:**
  - Tier 1 behavior supports next-three-track prebuffer, 32-bit float DSP,
    concurrent indexing, and exclusive output when available.
  - Tier 2 uses a single-track ring buffer and at most two index workers.
  - Approved resident-memory measurements meet Tier 1 and Tier 2 ceilings.
  - Tier 3 behavior remains deferred until a mobile platform milestone.
- **Estimated effort:** 4–7 person-days.
- **Owner:** Performance / Audio Engineer.

### TSK-17: Complete requirement verification and resilience matrix

- **Description:** Build and execute the golden codec suite, gapless and seek
  stress tests, Plex protocol tests, Keychain/privacy checks, UI responsiveness
  tests, corruption/recovery tests, and requirement evidence reporting.
- **Dependencies:** TSK-04, TSK-05, TSK-06, TSK-07, TSK-08, TSK-09, TSK-10,
  TSK-11, TSK-12, TSK-13, TSK-14, TSK-15, TSK-16.
- **Acceptance criteria:**
  - Every applicable row in [checklist.md](./checklist.md) has recorded
    pass/fail evidence.
  - All 21 approved formats pass; seeking stress test completes 100 random
    seeks for each required stress format.
  - Gapless, 250 ms decoder recovery, 50 ms command response, memory, and
    corruption recovery gates pass their approved measurement protocols;
    cold-start, decoder-deadline, and diagnostic-export checks also pass.
  - Any failed or blocked gate is visible and release-blocking; no unsupported
    future-platform gate is marked passed.
- **Estimated effort:** 6–12 person-days, excluding hardware availability.
- **Owner:** QA / Audio Test Engineer with feature owners.

### TSK-18: Assemble, sign, and notarize universal macOS distribution

- **Description:** Produce architecture-specific release builds, assemble the
  universal app, verify native library resolution and entitlements, sign,
  notarize, staple, and package the `.dmg`.
- **Dependencies:** TSK-02, TSK-05, TSK-06, TSK-08, TSK-14, TSK-17.
- **Acceptance criteria:**
  - App and all required bundled native libraries contain `arm64` and
    `x86_64` slices with working in-bundle runtime paths.
  - Hardened signature, notarization, and approved minimal entitlements pass
    verification.
  - Clean-environment launch and playback smoke tests pass on both supported
    architectures.
  - No credentials, provisioning profiles, or source-controlled native
    binaries are included.
- **Estimated effort:** 3–6 person-days, excluding Apple processing time.
- **Owner:** Release / macOS Build Engineer.

### TSK-19: Review release evidence and sign off

- **Description:** Review stakeholder decisions, requirement verification
  evidence, known limitations, dependency licenses, and release artifact
  integrity; approve or reject the release candidate.
- **Dependencies:** TSK-17, TSK-18.
- **Acceptance criteria:**
  - Every applicable release gate is passed or explicitly blocks release.
  - Stakeholder decisions and deferred future-platform items are reflected in
    release notes and user-facing claims.
  - Product, QA, and release owners approve the evidence and artifact.
- **Estimated effort:** 1–2 person-days.
- **Owner:** Product Owner, QA Lead, and Release Manager.

## Dependency Order

```text
TSK-01
  └─ TSK-02
      └─ TSK-03
          ├─ TSK-04 ─────────┐
          ├─ TSK-05 ─ TSK-07 ├─ TSK-08 ─ TSK-10 ─ TSK-14
          └─ TSK-06 ─────────┘       │        └─ TSK-15
                 └─ TSK-11 ─ TSK-12 ─ TSK-13
                         └─────────────────────┘
TSK-07, TSK-08, TSK-09, TSK-10 ─ TSK-16
TSK-04..TSK-16 ─ TSK-17 ─ TSK-18 ─ TSK-19
```

The dependency list on each task is authoritative; the diagram is a compact
overview. TSK-09 is also required by TSK-10, TSK-12, and TSK-15 as listed
above.

## Checklist Traceability

Each requirement row in [checklist.md](./checklist.md) is verified by at least
one task below. Acceptance evidence is collected in TSK-17; platform-deferred
requirements remain deferred until their platform milestone.

| Checklist requirements | Implementing / verification tasks |
| --- | --- |
| FR-APP-1 | TSK-02, TSK-18 |
| FR-APP-2 | TSK-03 |
| FR-APP-3 | TSK-03, TSK-06, TSK-08, TSK-14 |
| FR-APP-4 | TSK-10, TSK-17 |
| FR-AUDIO-1, FR-AUDIO-2 | TSK-05, TSK-07, TSK-17 |
| FR-AUDIO-3 | TSK-07, TSK-08, TSK-16, TSK-17 |
| FR-AUDIO-4 | TSK-07, TSK-10 |
| FR-AUDIO-5 | TSK-08, TSK-18; future platform work is deferred |
| FR-LIB-1, FR-LIB-2 | TSK-06, TSK-11 |
| FR-LIB-3 | TSK-09, TSK-10, TSK-12 |
| FR-LIB-4 | TSK-12 |
| FR-LIB-5 | TSK-12, TSK-13 |
| FR-LIB-6 | TSK-15 |
| FR-MAC-1, FR-MAC-2 | TSK-14 |
| FR-MAC-3 | TSK-08, TSK-14 |
| FR-MAC-4 | TSK-06 |
| FR-DATA-1, FR-REC-3 | TSK-04, TSK-17 |
| FR-PRIV-1 | TSK-17, TSK-19 |
| FR-PRIV-2 | TSK-11, TSK-15, TSK-17 |
| FR-REC-1 | TSK-07, TSK-10, TSK-17 |
| FR-REC-2 | TSK-08, TSK-17 |
| NFR-PERF-1 | TSK-02, TSK-17, TSK-18 |
| NFR-PERF-2, NFR-PERF-3 | TSK-16, TSK-17 |
| NFR-PERF-4 | Future mobile milestone; deferred |
| NFR-PERF-5 | TSK-07, TSK-08, TSK-17 |
| NFR-PERF-6 | TSK-14, TSK-17 |
| NFR-SEC-1 | TSK-02, TSK-17 |
| NFR-SEC-2 | TSK-02, TSK-05 |
| NFR-SEC-3 | TSK-06, TSK-17 |
| NFR-SEC-4 | TSK-18 |
| NFR-COMP-1 | TSK-02, TSK-18; future platform milestones are deferred |
| NFR-COMP-2 | TSK-18 |
| NFR-MAINT-1 | TSK-02, TSK-05, TSK-18 |
| NFR-MAINT-2 | TSK-05, TSK-18, TSK-19 |

## Planning Notes

- Task estimates are ranges because native integration, Plex behavior, and
  hardware-dependent audio validation carry material uncertainty.
- Assign named individuals during sprint planning; role owners above are
  accountability placeholders, not named commitments.
- Stakeholder decision tasks and unavailable hardware/licensing inputs can
  block dependent work even when their engineering estimate is complete.
- Keep each task deliverable independently testable and record checklist
  evidence as it is produced rather than waiting until TSK-17.
