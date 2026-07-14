# Locomotion Demo

A ready-to-run motion matching locomotion scene and the tooling to build it.

## What it contains

- `CharacterMotor` - turns animation root motion into collision-aware movement through a
  `CharacterController` (the matcher stays free of physics).
- `LocomotionInputProvider` - camera-relative WASD / left-stick intent via the Input System.
- `FollowCamera` - a minimal smoothed follow camera.
- Editor tooling to assemble and bake the demo in one click.

## Requirements

Input System package (`com.unity.inputsystem`). Import it before this sample if it is not already
in your project.

## One-click setup

1. Create a `Character` folder next to this README and drop a Humanoid or Generic rig FBX into it
   (for example a Mixamo character).
2. Run `Kinema > Motion Matching > Setup Full Demo From FBX`.

The setup:

- imports the rig, resolves its foot/hip bone names automatically;
- if you also placed locomotion FBXs under `Character/Animations/`, imports them (Humanoid) and bakes them;
- otherwise switches the rig to Generic and generates a demo locomotion set (idle, walk, run, turns)
  procedurally, so the scene works even with no animation assets;
- bakes the database and saves a fully wired `KinemaDemo.unity` (rig, controller, collision
  motor, input, follow camera).

Open the scene and press Play. Move with WASD or the left stick; open the Debug tab of the tool to
watch the live matching.

`Build Demo Scene (placeholder)` builds the same environment with a capsule instead of a rig, for a
quick look without any FBX.

## Notes

The generated clips are a functional stand-in with exact root motion and best-effort limb swing.
For production-quality motion, drop real locomotion clips into `Character/Animations/` and re-run
the setup; it will use those instead.
