# Changelog

All notable changes to this package are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
