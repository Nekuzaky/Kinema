# Kinema Motion Matching

[![Unity 6000.3+](https://img.shields.io/badge/Unity-6000.3%2B-black)](https://unity.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](Packages/com.nekuzaky.kinema/LICENSE.md)
[![UPM](https://img.shields.io/badge/UPM-com.nekuzaky.kinema-informational)](Packages/com.nekuzaky.kinema)

Data-driven motion matching locomotion for Unity. An offline bake pipeline turns `AnimationClip`s
into a normalized feature database; a Burst-compiled runtime queries it every few frames and blends
to the best pose through a `PlayableGraph`; an integrated editor window drives, records and debugs
the whole loop.

The toolkit is packaged as an installable UPM package (`com.nekuzaky.kinema`) and this repository is
also a Unity project you can open to try the demo directly.

## Features

**Matching core**
- Weighted nearest-neighbour search over baked feature vectors: past+future trajectory, per-bone
  pose and velocity, root velocity, gait phase - Burst-compiled parallel job with a KD-tree option
  for very large databases.
- Live pose query: the query's pose half is sampled off the rendered skeleton (after IK), not copied
  from a database row, so the cost function judges what is actually on screen.
- Foot-phase cost term keeps candidates on-cycle, so a jump cannot cut a stride mid-step.
- Critically damped spring trajectory prediction (selectable), deviation-triggered search on top of
  the timed interval, idle-duplicate pruning at bake time.
- Inertialization transitions (Burst animation job) or two-slot crossfades, foot-lock + ground
  adaptation IK, stride warping, semantic tags (64-bit masks, filtered in the search job), motion
  events with root warping, mirrored variants, calibration profiles, multi-database switching,
  Mecanim interop.

**Editor window** (`Tools > Kinema > Motion Matching Window`, Ctrl+Shift+M)
- Overview / Database / Bake / Tags / Director / Debug / Analysis / Settings.
- Director tab: play any baked clip on the live character like a custom Animator (scrubbable
  timeline with per-foot contact lanes), record intent + pose, spawn ghost NPCs that replay a
  recording through their own matching, bake a performance to a real `AnimationClip`, swap the
  character's rig in one click.
- `Tools > Kinema > Benchmark Search`: measures the real search cost (mean/median/p99, order-checked
  against Burst's async compile) and converts it into characters-per-frame at a given search rate.

**Demo** (`Tools > Kinema > Demo Scene`)
- One menu item resolves its own source - an installed mocap pack, otherwise a dropped-in FBX (using
  its clips or generating a procedural set) - bakes it, and builds a scene with a traversal course
  (vault walls, gapped platforms, rising steps), full keyboard + gamepad input, and camera orbit.

## Installation

Unity Package Manager, "Add package from git URL":

```
https://github.com/Nekuzaky/kinema.git?path=/Packages/com.nekuzaky.kinema
```

Requires Unity 6000.3+. The sample uses the Input System package; the runtime has no package
dependencies.

## Quick start

1. Open the tool: `Tools > Kinema > Motion Matching Window` (Ctrl+Shift+M).
2. Create a config and assign a rig plus locomotion clips.
3. Bake the database (Bake tab).
4. Add a `MotionMatchingController` to your character and assign the database.

Or import the "Locomotion Demo" sample and run `Tools > Kinema > Demo Scene` for a fully wired scene
- drop a Humanoid FBX in the sample's Character folder first if you have no mocap pack installed.

## How it works

Each frame is baked into one normalized feature vector:

```
[ TrajectoryPosition 2*T | TrajectoryDirection 2*T | BonePosition 3*B | BoneVelocity 3*B | RootVelocity 2 ]
```

plus a parallel gait-phase channel used as an extra cost term. Matching is a weighted squared
distance over these vectors, evaluated by the query's predicted future trajectory and its live pose
sampled off the rendered skeleton. The controller only cuts to a new frame when it clearly beats
continuing the current clip, then crossfades or inertializes. See
[the documentation](Packages/com.nekuzaky.kinema/Documentation~/index.md) for detail.

## Repository layout

```
Packages/com.nekuzaky.kinema/   The package (Runtime, Editor, Samples~, docs, license)
Assets/                         Unity project shell and the imported demo
```

## Testing

`Packages/com.nekuzaky.kinema/Tests/Editor` holds EditMode tests covering the feature schema layout,
`CharacterSpace` round-trips, the trajectory history ring buffer, the database's
normalization/accessors, idle-duplicate pruning, and end-to-end matcher correctness (nearest-
neighbour, tag filtering, ignore ranges, phase cost) against synthetic databases built directly
through `SetBakedData` - no rig or bake pipeline required. Run them from Unity's Test Runner window
(`Window > General > Test Runner`, EditMode tab) or headless:

```
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
```

`Tools > Kinema > Benchmark Search` measures search performance directly (headless-runnable); see
[TODO.md](TODO.md) for the gaps that remain (PlayMode/runtime feel automation, standalone builds).

## Demo assets

The demo character is provided by [Adobe Mixamo](https://www.mixamo.com/) and remains subject to
Adobe's Mixamo license. It is included only to make the demo runnable and is not covered by this
project's license. A richer demo path exists for the (separately licensed, not redistributed) Opsive
OmniAnimation mocap pack - see [the documentation](Packages/com.nekuzaky.kinema/Documentation~/index.md).

## License

MIT. See [LICENSE](Packages/com.nekuzaky.kinema/LICENSE.md).

Author: [Nekuzaky](https://github.com/Nekuzaky).
