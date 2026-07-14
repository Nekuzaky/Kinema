# Kinema Motion Matching

[![Unity 6000.3+](https://img.shields.io/badge/Unity-6000.3%2B-black)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](Packages/com.nekuzaky.kinema/LICENSE.md)
[![UPM](https://img.shields.io/badge/UPM-com.nekuzaky.kinema-informational)](Packages/com.nekuzaky.kinema)
[![Tests](https://github.com/Nekuzaky/Kinema/actions/workflows/tests.yml/badge.svg)](https://github.com/Nekuzaky/Kinema/actions/workflows/tests.yml)

Data-driven motion matching locomotion for Unity. An offline bake pipeline turns `AnimationClip`s
into a normalized feature database; a runtime queries it every few frames and blends to the best
pose through a `PlayableGraph`; an integrated editor tool drives and debugs the whole loop.

The toolkit is packaged as an installable UPM package
(`com.nekuzaky.kinema`) and this repository is also a Unity project you can open to try the
demo directly.

## Features

- Runtime locomotion motion matching on a manually clocked `PlayableGraph`, with inertialization
  transitions (Burst animation job) or two-slot crossfades.
- Past + future trajectory matching, per-group and per-bone feature weights, calibration profiles.
- Baked foot contacts with a foot-lock IK component; semantic tags (64-bit masks) with filtered
  search and a visual timeline editor; AnimationEvent relay.
- Motion events with root warping, upper-body overlay layers, experimental mirrored variants.
- Burst-compiled parallel search (optional KD-tree for very large sets), 16-bit feature storage,
  multi-database switching, Mecanim interop.
- Snapshot debugger: scrub the last 240 matching decisions with full cost breakdowns.
- Offline bake from `AnimationClip`s into a normalized feature database (per-dimension mean/std).
- Configurable weighted scorer with a per-group cost breakdown.
- Integrated editor window (Overview / Database / Bake / Debug / Settings), custom inspectors, and
  scene-view trajectory gizmos.
- Input-agnostic runtime via `ILocomotionProvider`; a collision-aware `CharacterMotor` keeps physics
  out of the matcher.
- Sample locomotion scene with a one-click setup that generates demo animations when none are supplied.

## Installation

Unity Package Manager, "Add package from git URL":

```
https://github.com/Nekuzaky/kinema.git?path=/Packages/com.nekuzaky.kinema
```

Requires Unity 6000.3+. The sample uses the Input System package; the runtime has no package
dependencies.

## Quick start

1. Open the tool: `Kinema > Motion Matching > Window` (Ctrl+Shift+M).
2. Create a config and assign a rig plus locomotion clips.
3. Bake the database.
4. Add a `MotionMatchingController` to your character and assign the database.

Or import the "Locomotion Demo" sample and run `Kinema > Motion Matching > Setup Full Demo
From FBX` for a fully wired scene.

## How it works

Each frame is baked into one normalized feature vector:

```
[ TrajectoryPosition 2*T | TrajectoryDirection 2*T | BonePosition 3*B | BoneVelocity 3*B | RootVelocity 2 ]
```

Matching is a weighted squared distance over these vectors. The query combines a predicted desired
trajectory with the pose of the frame currently playing; the controller only cuts to a new frame when
it clearly beats continuing the current clip, then crossfades. See
[the documentation](Packages/com.nekuzaky.kinema/Documentation~/index.md) for detail.

## Repository layout

```
Packages/com.nekuzaky.kinema/   The package (Runtime, Editor, Samples~, docs, license)
Assets/                                   Unity project shell and the imported demo
```

## Testing

`Packages/com.nekuzaky.kinema/Tests/Editor` holds EditMode tests covering the feature schema
layout, `CharacterSpace` round-trips, the trajectory history ring buffer, the database's
normalization/accessors, and end-to-end matcher correctness (nearest-neighbour, tag filtering,
ignore ranges) against synthetic databases built directly through `SetBakedData` - no rig or bake
pipeline required. Run them from Unity's Test Runner window (`Window > General > Test Runner`,
EditMode tab) or headless:

```
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
```

CI runs the same suite on every push via [GitHub Actions](.github/workflows/tests.yml)
([game-ci/unity-test-runner](https://github.com/game-ci/unity-test-runner)); it needs a
`UNITY_LICENSE` (or `UNITY_EMAIL`/`UNITY_PASSWORD`) repository secret to activate Unity in the
runner - see the action's docs for obtaining one from a Unity Personal license.

See [TODO.md](TODO.md) for the known gaps this doesn't yet cover (runtime/PlayMode behaviour,
large-scale performance, standalone builds).

## Roadmap

The architecture is deliberately open to grow toward: pose cost breakdown (in place), mirroring,
contextual tags and animation sections, foot/hand contacts, events, bone profiles, search
acceleration (KD-tree / PCA), and multi-database selection.

## Demo assets

The demo character is provided by [Adobe Mixamo](https://www.mixamo.com/) and remains subject to
Adobe's Mixamo license. It is included only to make the demo runnable and is not covered by this
project's license.

## License

MIT. See [LICENSE](Packages/com.nekuzaky.kinema/LICENSE.md).

Author: [Nekuzaky](https://github.com/Nekuzaky).
