# Changelog

All notable changes to this package are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
