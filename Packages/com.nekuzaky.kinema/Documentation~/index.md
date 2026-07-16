# Kinema Motion Matching - Documentation

## Overview

Motion matching selects, every few frames, the animation pose that best fits the character's current
motion and desired trajectory, then blends to it. This package provides the full loop: an offline
bake that turns `AnimationClip`s into a searchable feature database, a runtime that queries it and
plays the result through a `PlayableGraph`, and an editor tool to drive and debug the whole thing.

## Architecture

```
Editor (bake, window, inspectors)         Runtime (Kinema.MotionMatching)
  MotionMatchingBaker  ---------------->    MotionMatchingDatabase (ScriptableObject)
  PoseExtractor                             MotionMatchingConfig   (ScriptableObject)
  MotionMatchingWindow                      MotionMatcher / MotionMatchingQuery
  Inspectors + gizmos                       TrajectoryPredictor / CharacterSpace
                                            MotionMatchingController (PlayableGraph)
```

- `Runtime` depends on Burst and Mathematics (the search is a Burst job), and on Timeline only for
  the optional `Kinema.MotionMatching.Timeline` assembly, which compiles only when Timeline is present.
- `Editor` references `Runtime`.
- `Samples~` reference `Runtime` and `Editor`, and the Input System.

## Feature vector

Each baked frame is one flat, normalized vector laid out as contiguous groups:

```
[ TrajectoryPosition 2*T | TrajectoryDirection 2*T | BonePosition 3*B | BoneVelocity 3*B | RootVelocity 2 ]
```

`T` is the number of trajectory sample times (negative offsets sample the character's recorded past
via a `TrajectoryHistory` buffer, positive offsets the predicted future), `B` the number of sampled
bones. Everything is expressed in `CharacterSpace` (a horizontal frame at the root, +Z forward),
which makes matching invariant to world position and heading. Values are stored normalized (per-dimension mean and
standard deviation), and the statistics are kept so a runtime query can be normalized identically.

## Scoring

Matching is a weighted squared distance between the query and every candidate frame. There is one
weight per `FeatureGroup` (`FeatureWeights`); the schema expands these into a per-dimension weight
table. `MotionMatcher.Search` runs a linear scan with a branch-and-bound early-out and returns the
winning frame together with a per-group cost breakdown for the debug layer.

The query is built from two halves:
- the trajectory half, from the desired locomotion (`TrajectoryPredictor`; a critically damped spring
  by default, with a first-order lag selectable);
- the pose half, sampled off the rendered skeleton, so the cost function judges the pose actually on
  screen rather than the frame the clock happens to sit on. It falls back to copying the current
  frame when the schema's bones cannot be resolved on the rig.

## Bake pipeline

`MotionMatchingBaker` instantiates the config's rig, and for each clip `PoseExtractor` samples it at
the bake frame rate, computing bone positions/velocities and root trajectory in character space.
All frames are concatenated, per-dimension mean/std are computed, the data is normalized, and a
`MotionMatchingDatabase` asset is written next to the config.

## Runtime

`MotionMatchingController` owns a manually clocked `PlayableGraph` with a two-slot
`AnimationMixerPlayable` for crossfades. Every search interval it builds a query, searches the
database, and - only if a candidate clearly beats continuing the current clip - crossfades to it.
Locomotion intent is read from an `ILocomotionProvider`, so the same controller serves players and AI.

## Editor tool

`Tools > Kinema > Motion Matching Window` (Ctrl+Shift+M):

- Overview - assets and readiness at a glance.
- Database - baked frames, clips, feature layout, memory footprint.
- Bake - assign rig and clips, bake or rebake; bake a blend space into playable grid clips.
- Tags - author tag ranges per clip, or detect idle/walk/run/turn from the motion itself.
- Director - play a baked clip on the live character, record, spawn ghosts, swap the rig.
- AI - every agent's brain, goal and reason, with manual commands.
- Debug - live clip/frame, cost breakdown, snapshot rewind and diff (play mode).
- Analysis - benchmarks and motion-quality measures.
- Settings - weights and schema.

Custom inspectors add one-click bake and a live readout; scene-view handles draw the desired and
candidate trajectories with a cost label. `Tools > Kinema > Copy Diagnostics` puts the whole scene's
matching state on the clipboard as text.

## Extension points

Shipped since this page first listed them as roadmap: inertialization transitions, the Burst/Jobs
search over `NativeArray` features, mirroring, tags and sections, contacts and motion events, KD-tree
acceleration, and multiple databases. See the [changelog](../CHANGELOG.md) for what landed when.

Still open:

- Learned Motion Matching (Ubisoft La Forge). The dataset export and training pipeline exist
  (`Tools > Kinema > Learned MM > Export Training Dataset`, `Documentation~/Training/`) behind the
  backend-agnostic `ILearnedMotionModel` seam; the Sentis backend that would run the exported ONNX
  is not built, so the package takes no ML dependency yet.
- Blend spaces as *runtime* data: today they bake to grid clips ahead of time (`BlendSpaceBaker`)
  rather than being blended on demand.
- Search acceleration (replace the linear scan with a KD-tree or PCA projection behind `MotionMatcher`).
- Multiple databases (a selector layer above the controller).

## Code style

Runtime and editor code follow a consistent shape: regions ordered Public / Private and Protected /
Unity API / Main API / Tools and Utilities; private fields `_camelCase`; types, methods and
properties `PascalCase`.
