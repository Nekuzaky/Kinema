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

## Integrating it into a game that is not the demo

Everything here comes from someone integrating the package into an FPS and paying for it in debugging
cycles. The demo scene is not a neutral example: it is a scene where the player is *itself* a matched
character, and several defaults quietly assume that.

**The controller poses a skeleton. It does not move anything.** There is no motor in the runtime, on
purpose - a game already owns how its characters move. The controller picks a clip; the clip's root
motion is delivered to `OnAnimatorMove`, and *something you write* has to consume it and call
`Move()`. The demo's `CharacterMotor` (in the sample) is one such thing, and is worth reading before
writing your own. Without one, the character animates in place and nothing about it looks like an
error.

**`AICommandProvider.Player Target` is a field. Assign it.** If it is empty, the provider falls back
to searching for a `MotionMatchingController` that has no `AICommandProvider` - which finds the player
in this package's demo and nothing at all in a game where the player is a capsule and a camera. An
agent that cannot find its target does not misbehave visibly: it stands still holding a Follow goal
it can never act on. That now logs an error rather than being inferred from frozen NPCs.

**The Database and the Config are different assets, and the bake names them alike.**
`MyConfig` is the recipe; `MyConfigDatabase` is what the bake produced. The controller wants the
Database.

**The rig has to carry the bones the database was baked against.** They are matched by transform
name. Bake against one rig and run against another - even a near-identical one - and the names miss;
the pose query then silently demotes itself to copying the current database frame, which still
animates and still looks alive while matching against a pose the character is not in. That now
reports every missing bone at once, as an error.

**Awake does not run while a GameObject is inactive.** Anything spawned disabled and enabled later -
a pooled enemy, most obviously - would miss a resolve that only happened in `Awake`. The components
here re-resolve on their first tick for that reason. If you write your own, do the same.

### What is an error, and what is not

A hard error means *nothing can happen*: no database, no Animator, an AI with nothing to target, a rig
missing the schema's bones. These disable the component or say plainly that it will do nothing.

A warning means *something will be worse than you expect*: a database with no tags, a tag name that
matched nothing. These still run.

Neither is verbose logging, which is off by default (**Settings > Verbose logging**) and prints
transitions - a vault firing, an AI changing goal. For the state of everything at once, use **Copy
Diagnostics** in the window header: it puts every number about every character in the scene on the
clipboard as plain text.

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
