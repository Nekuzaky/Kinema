# Changelog

All notable changes to this package are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.5.0] - 2026-07-15

Mocap data release. The demo could previously only be driven by procedurally authored clips, which
capped how real the result could look no matter how good the runtime was. This release adds a setup
path for a real motion capture locomotion pack.

### Added
- `OpsivePackSetup`: one-click setup that builds a config, database and scene from an Opsive
  OmniAnimation locomotion pack. Bakes on the pack's own rig, so features, contact bone names and
  playback all stay on the skeleton the capture was authored for, with no retargeting guesswork.
- Clip auto-tagging from the pack's naming convention: 74 clips are sorted into 12 tags
  (Crouch, Strafe, Backward, Diagonal, Idle, Walk, Run, Sprint, Turn, Start, Stop, Jump) without
  painting a single range by hand.
- `StanceTagController` sample: drives `RequiredTags` / `ExcludedTags` from gameplay stance. A mixed
  pack needs this - nothing in the feature vector says "this pose is crouched", so an unfiltered
  search will answer a standing query with a crouched frame whose feet happen to line up.

### Notes
- The pack itself is per-seat licensed Asset Store content and is not redistributed here. Import it
  into your own project, then run Kinema > Motion Matching > Setup Demo From Opsive Pack. The
  generated config, database and scene are git-ignored for the same reason.
- Measured on the baked set: peak root speed 6.47 m/s, 40% of frames travelling above 0.3 m/s, a
  foot contact detected on 53% of frames.

## [1.4.0] - 2026-07-15

Movement realism release.

### Added
- Stride warping: clip playback is scaled by requested/baked root speed, so a database holding a
  1.4 m/s walk and a 4 m/s run can travel at any speed between them with the legs cycling in sync
  with the travel. Without it, root-motion locomotion can only move at the discrete speeds present
  in the data and the feet slide the difference. Range-clamped, smoothed, and skipped for idle and
  motion events. Exposed as `CurrentStrideWarp` and surfaced in the Analysis tab.
- `GroundAdaptationIK`: clips are authored on a flat floor, so on ramps and steps the feet float or
  sink. This probes under each foot, plants it on the real surface, tilts it to the slope, and drops
  the pelvis so the lower foot can still reach - height and orientation only, complementing
  `FootLockIK`'s horizontal pinning.
- The demo character now ships with ground adaptation, the quality probe and the session recorder
  wired in.

### Note on realism
System work has a ceiling: the procedural demo clips are sine-wave approximations. Stride warping
and IK remove the mechanical artefacts, but human-looking motion needs mocap data. Drop real
locomotion clips into the sample's `Character/Animations/` folder and re-run the setup.

## [1.3.0] - 2026-07-15

Instrumentation and authoring release: measure the system instead of eyeballing it.

### Added
- Session recording and deterministic replay: `SessionRecorder` captures a play session's
  locomotion intent frame by frame into a `SessionRecording` asset, and
  `ReplayLocomotionProvider` feeds that identical intent back through the controller as an ordinary
  `ILocomotionProvider`. With Force Recorded Timestep the recorded per-frame delta drives
  `Time.captureDeltaTime`, so matching decisions reproduce exactly - two tuning setups can finally
  be compared on the same performance rather than on two different human runs.
- Foot-slide measurement (`MotionQualityProbe`): while the baked contacts flag a foot grounded, any
  world-space travel is slide - the metric animation teams judge locomotion by. Kinema can measure
  it precisely because contacts are baked data rather than a guess. Also reports jump rate (flicker)
  and average/peak matching cost.
- Database coverage analysis (`CoverageReport`, controller frame-usage telemetry): which frames the
  matcher actually reaches for, how much of the database is dead weight, and which clips are never
  selected at all - wasted memory and mocap budget made visible.
- Analysis tab: quality stat cards with plain-English diagnosis, per-clip coverage bars with dead
  clips flagged, and record/save/replay controls in one place.
- Six coverage tests (39 total, all passing).

## [1.2.1] - 2026-07-14

### Fixed
- Settings tab logged "You can't nest Foldout Headers" once per List field: the runtime and config
  parameter groups used `BeginFoldoutHeaderGroup`, which cannot contain the reorderable-list
  drawers those objects expose. Replaced with plain framed sections.

## [1.2.0] - 2026-07-14

Quality-of-engineering release: real state rewind, automated tests, CI.

### Added
- Full-state snapshot rewind: `SearchSnapshot` now captures the character transform and both
  playback slots (clip, time, blend, mirror flag), and `MotionMatchingController.PreviewSnapshot`
  / `StopPreview` replay that exact state through the live graph - the Debug tab's history scrubber
  gained a Preview toggle that visually rewinds the character, not just the recorded numbers.
- EditMode test suite (`Tests/Editor`, 33 tests): feature schema layout and weight expansion,
  `CharacterSpace` round-trips, the trajectory history ring buffer, database normalization and
  accessors, and end-to-end matcher correctness (nearest-neighbour, tag filtering, ignore ranges)
  against synthetic databases built directly via `SetBakedData`.
- GitHub Actions CI (`.github/workflows/tests.yml`) running the suite on every push via
  game-ci/unity-test-runner; needs a `UNITY_LICENSE` repository secret to activate.
- `TODO.md`: the honest remaining-gaps list (PlayMode coverage, standalone build validation,
  large-scale performance, mirroring visual validation, and the not-yet-started feature scope).

## [1.1.0] - 2026-07-14

Tooling and demo-gameplay release.

### Added
- Overview dashboard: stat cards (frames / clips / dims / memory) and a subsystem status board
  (database, contacts, tags, mirroring, profiles, storage) with actionable detail per row.
- Frame Inspector in the Database tab: scrub any baked frame and read back its denormalized data —
  root speed, per-bone positions and weights, grounded feet, active tags.
- Cost sparkline in the Debug tab: total-cost history over the last 120 searches with jump markers.
- Bake pre-flight: rig/clips/schema checklist plus estimated frame count and asset size
  (mirroring and 16-bit storage factored in) before committing to a bake.
- Full parameter surface in Settings: every controller field (live in play mode, immediate weight
  resync) and every config field, plus one-click calibration profile buttons.
- Demo vault: procedural vault clip + motion event with warped contact, VaultTrigger (chest-ray
  probe, obstacle top 0.35-1.15 m, Space / gamepad South), optional Auto Vault for AI.
- AIFollowProvider sample: target-seeking intent through the same ILocomotionProvider contract as
  the player, with arrival slow-down.

### Fixed
- Backward teleport on clip loop: looping slot clocks are now monotonic (manual wrapping made the
  Animator emit a negative root-motion delta every loop).
- Procedural gait: human cadence (one leg cycle per clip), arms lowered from the T-pose with
  chain-composed elbows, level feet, pelvis sway, speed-scaled torso lean.
- FollowCamera auto-targets the matched character when its serialized reference is missing.
- Editor-time fake-null after AssetDatabase.Refresh when wiring freshly created event assets.

## [1.0.0] - 2026-07-14

Scale release.

### Added
- Calibration profiles: named weight presets on the config, baked into the database, applied at
  runtime with `controller.SetCalibrationProfile(name)` (no rebake).
- 16-bit feature storage (opt-in): features serialized as halves, halving the asset size; decoded
  once at load. Negligible quality impact on normalized features.
- Multi-database: an additional-databases list on the controller plus
  `SwitchDatabase(database | index)`; the next search re-anchors in the new set and the
  inertializer absorbs the transition.
- Optional KD-tree search (`SearchAcceleration.KdTree`): a tree over weight-scaled features for
  very large databases. Falls back to the Burst linear job whenever tag masks or ignore ranges are
  active. BurstLinear remains the default and the right choice below ~50k frames.
- Mecanim interop: `SetMatchingActive(bool, fade)` fades the matching output against the
  Animator's own controller, so scripted states and cinematics can take over and hand back.

## [0.5.0] - 2026-07-14

Actions release.

### Added
- Motion events (`MotionEventDefinition` asset + `controller.PlayEvent`): triggered action clips
  (vault, interaction) played outside the matching loop, with root warping so the clip's contact
  moment lands exactly on the requested position/rotation. Matching resumes with an immediate
  search when the clip ends; the inertializer absorbs both seams.
- Overlay layers: an `AnimationLayerMixerPlayable` above the matching chain.
  `PlayOverlay(clip, avatarMask, weight, fade)` / `StopOverlay()` for upper-body actions while
  locomotion keeps matching; one-shot overlays fade out on their own.
- Mirroring (experimental, opt-in on the config): every frame baked as a mirrored variant
  (trajectory/root X-flip, Left/Right bone slots swapped, contacts bit-swapped) and played through a
  runtime mirror pose job (name-paired Left/Right transforms, X-plane reflection). Doubles coverage
  without new mocap. Requires a symmetric rig; off by default.
- Snapshot debugger: the last 240 matching decisions recorded in a preallocated ring buffer
  (costs, breakdown, trajectories) with a scrubber in the Debug tab.

### Honest status
- Mirroring is baked-and-wired but unvalidated visually; keep it off until playtested.

## [0.4.0] - 2026-07-14

Control release.

### Added
- Semantic tags: a 64-tag vocabulary on the config, per-clip tag ranges, baked into the database as
  per-frame bitmasks. The search filters candidates with `RequiredTags` / `ExcludedTags` masks
  (controller properties, plus `RequireTag(name)` helper); filtering happens inside the Burst job.
- Tags tab in the editor window: visual per-clip timeline (one lane per tag, colored range blocks)
  with numeric editing, stored on the config.
- Strafe mode in the sample input provider (hold right mouse / gamepad left trigger): the character
  faces the camera while moving in any direction. Pair with a "Strafe" tag on strafe clips.
- AnimationEvent relay: events of the active clip fire via SendMessage with Mecanim semantics
  (manual-clock playback bypasses Unity's own dispatch), loop-wrap safe. Toggleable.

## [0.3.0] - 2026-07-14

Motion quality release.

### Added
- Inertialization transitions (`PoseInertializer`, Burst-compiled animation job): the graph
  hard-switches clips and a per-bone pose/velocity offset decays over the blend time with a cubic
  Hermite curve (Gears of War 4 style). New `TransitionMode` on the controller; inertialization is
  the default, crossfade remains available.
- Foot contacts baked into the database (per-frame bitmask; feet auto-detected from schema bone
  names) and `FootLockIK`: analytic two-bone IK in LateUpdate that pins grounded feet, with break
  distance and smooth release.
- Burst/Jobs search: features live in a persistent `NativeArray` and the weighted nearest-neighbour
  scan runs as a Burst-compiled parallel job (chunked, branch-and-bound early-out per chunk).
  `MotionMatcher` is now `IDisposable`.

### Changed
- New package dependencies: `com.unity.burst`, `com.unity.mathematics`.
- Databases must be rebaked to include contact data (older databases still load; foot lock disables itself).

### Honest status
- Compiles and bakes headless; inertialization and foot lock have not yet been visually playtested.
  Both can be disabled per component if something looks off (`TransitionMode.Crossfade`, FootLockIK weight 0).

## [0.2.0] - 2026-07-14

Matching quality and tooling pass, informed by JLPM22/MxM-style systems.

### Added
- Past + future trajectory: `TrajectoryTimes` now accepts negative offsets, sampled from a runtime
  `TrajectoryHistory` ring buffer, so matching considers where the character came from, not only where it is going.
- Per-bone feature weights (`FeatureSchema.BoneWeights`), parallel to `BoneNames`, to weight feet/hands
  above the hips (MxM-style joint weighting).
- Transition (clip-change) cost penalty on the controller to reduce clip flicker while preserving responsiveness.
- Scene-view debug gizmos for the matched frame's sampled bone positions, plus the bone-weights field in the Settings tab.

### Changed
- Default schema now samples two past and four future trajectory points. Databases baked with 0.1.0 must be rebaked.

### Roadmap
- Inertialization-based pose transitions and Burst/Jobs-accelerated search are the next steps; they change
  runtime behaviour and are best landed with in-editor playtesting.

## [0.1.0] - 2026-07-14

Initial release.

### Added
- Runtime motion matching for locomotion built on a manually clocked `PlayableGraph`
  (two-slot crossfade), input-agnostic via `ILocomotionProvider`.
- Data model: `FeatureSchema`, `TrajectorySample`, `MotionFrameInfo`, `MotionClipEntry`,
  per-group `FeatureWeights`, and a `CharacterSpace` reference frame shared by bake, runtime and debug.
- Offline bake pipeline (`MotionMatchingBaker`, `PoseExtractor`) sampling `AnimationClip`s into a
  normalized feature database (`MotionMatchingDatabase`) with per-dimension mean/std statistics.
- Configurable weighted nearest-neighbour scorer (`MotionMatcher`) with a per-group cost breakdown.
- Editor window with Overview, Database, Bake, Debug and Settings tabs, plus custom inspectors
  and scene-view gizmos/handles for the desired and candidate trajectories.
- Sample "Locomotion Demo": collision-aware `CharacterMotor`, `LocomotionInputProvider`,
  `FollowCamera`, a one-click scene setup, and a procedural locomotion generator used when no
  clips are supplied.

### Known limitations
- Search is a linear scan (fine for a few thousand frames; a spatial index is a future step).
- In-place clips produce flat trajectories; clips with root motion are recommended.
