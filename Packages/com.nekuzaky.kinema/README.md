# Kinema Motion Matching

`com.nekuzaky.kinema`

Data-driven motion matching locomotion for Unity: an offline bake pipeline, a normalized feature
database, a configurable weighted scorer, a `PlayableGraph` runtime, and an integrated editor tool
with live debug. Small and readable, structured to grow toward mirroring, tags, contacts and cost
breakdowns.

## Requirements

- Unity 6000.3 or newer.
- Input System package (`com.unity.inputsystem`) for the sample only. The runtime has no package dependencies.

## Installation

Package Manager, "Add package from git URL":

```
https://github.com/Nekuzaky/kinema.git?path=/Packages/com.nekuzaky.kinema
```

Or add to `Packages/manifest.json`:

```json
"com.nekuzaky.kinema": "https://github.com/Nekuzaky/kinema.git?path=/Packages/com.nekuzaky.kinema"
```

## Quick start

1. Open the tool: `Kinema > Motion Matching > Window` (Ctrl+Shift+M).
2. Create a config (Overview tab) and assign a Humanoid or Generic rig plus locomotion clips.
3. Bake the database (Bake tab).
4. Add a `MotionMatchingController` to your character, assign the database, and drive it with an
   `ILocomotionProvider` (the sample ships one).

See the "Locomotion Demo" sample for a fully wired scene and a one-click setup.

## Layout

- `Runtime/` - data, database, matcher, controller. No package dependencies.
- `Editor/` - bake pipeline, editor window, inspectors.
- `Samples~/Locomotion Demo/` - collision motor, input provider, follow camera, demo tooling.

## Documentation

Full documentation lives in `Documentation~/index.md`. License: MIT (`LICENSE.md`).
