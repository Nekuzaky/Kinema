# TODO / Known Limitations

Honest gap list. The architecture and tooling are AAA-informed and cover a wide feature surface
(see the [wiki](https://github.com/Nekuzaky/Kinema/wiki)), but the project is young: few real
play hours, no mocap data, no automated coverage of runtime behaviour. This file tracks what's
left, roughly in priority order within each section.

## Data

- [x] **Real mocap demo path** - `Tools > Kinema > Demo Scene` bakes an installed Opsive
      OmniAnimation pack (74 clips) when present, auto-tagged and retargeted onto the demo's
      Humanoid rig; falls back to the procedural clips only when no pack and no dropped-in FBX
      clips are available.
- [ ] **Validate mirroring visually.** Baked and wired (trajectory/root X-flip, Left/Right bone
      swap, runtime mirror pose job) but never checked on screen. Keep `Generate Mirrored
      Variants` off until it has been playtested on a real rig.
- [ ] **Validate the v1.13-1.15 matching changes visually.** Live pose query, foot-phase cost,
      spring prediction, deviation search and idle pruning are compile/headless-verified and unit
      tested, but runtime feel (does it read as more or less real) has not been judged on screen.

## Performance

- [x] **Stress-tested at scale** - `Tools > Kinema > Benchmark Search` measures the real Burst
      search against the baked database and synthetic clustered sets up to 400k frames; see the
      README/changelog for numbers. Confirmed: BurstLinear wins below ~25k frames (where the demo
      and most projects sit), the KD-tree only earns its keep past ~100k.
- [ ] Not yet profiled: many characters searching concurrently in one frame (the benchmark times
      one matcher in isolation), and standalone-build numbers (everything measured so far is
      in-editor with Burst forced synchronous).
- [ ] Animation LOD: degrade search cadence with camera distance for crowds of matched
      characters. Not implemented.

## Runtime behaviour

- [ ] **No PlayMode test coverage.** EditMode tests (see [README](README.md#testing)) cover
      schema/database/matcher logic against synthetic data, but the PlayableGraph, inertializer,
      foot lock IK, motion events and warping have only been judged visually against recorded
      playtest sessions - no automated regression coverage. Candidates: PlayMode tests driving a
      controller through scripted intent and asserting root motion / frame selection stays within
      expected bounds.
- [ ] **Never built as a standalone player.** Everything so far ran in the Editor. Burst
      compilation, Input System bindings and the Playables graph should all work in a build, but
      this hasn't been confirmed on Windows/Mac/Linux/mobile/console.
- [ ] Events and overlay layers have exactly one demonstrated use case each (vault; none for
      overlays). Untested against overlapping events, event-during-event, or multiple concurrent
      overlay layers.

## Tooling

- [ ] **Snapshot debugger has no state diffing.** The rewind (`PreviewSnapshot`/`StopPreview`)
      replays one recorded moment exactly, but there's no side-by-side comparison between two
      snapshots or a way to step frame-by-frame within a single decision.
- [ ] Frame Inspector and Tag timeline have no keyboard navigation or search/filter for large
      clip counts.

## Feature scope not started

- [ ] Blend space integration (MxM-style: blend spaces as matchable data).
- [x] Retargeting through Humanoid so one database serves multiple skeletons - the ghost-on-a-
      different-rig path and the Director's one-click rig swap both do this (copy settings, keep
      the database, retarget onto the new body).
- [x] Auto-tagging from clip naming conventions (Opsive pack setup: 74 clips sorted into 12 tags
      from their file names, no ranges painted by hand). Speed/turn/idle detection from motion
      itself, rather than naming convention, is still not implemented.
- [ ] Learned Motion Matching (Ubisoft La Forge): decompressor / stepper / projector networks
      replacing the database at runtime for large memory savings. The normalized, well-typed data
      model should make training-data export straightforward when this is picked up.
- [ ] Interop with Timeline / cutscene tooling beyond the basic Mecanim-fade (`SetMatchingActive`).

## Process

- [x] EditMode automated tests (50 tests, `Tests/Editor`) - **done**.
- [x] CI workflow (`.github/workflows/tests.yml`, GitHub Actions via game-ci) - **configured**,
      needs a `UNITY_LICENSE` repository secret added by the repo owner to actually run green.
- [ ] Code coverage measurement/reporting.
- [ ] Contribution guidelines beyond the README's one-paragraph note.
