# TODO / Known Limitations

Honest gap list. The architecture and tooling are AAA-informed and cover a wide feature surface
(see the [wiki](https://github.com/Nekuzaky/Kinema/wiki)), but the project is young: few real
play hours, no mocap data, no automated coverage of runtime behaviour. This file tracks what's
left, roughly in priority order within each section.

## Data

- [ ] **Replace procedural demo animations with real mocap.** The bundled locomotion and vault
      clips are code-generated placeholders (correct cadence and root motion, approximate limb
      detail). Drop 5+ real clips into `Character/Animations/` and re-run the demo setup to
      upgrade instantly - the pipeline doesn't change, only the input quality does.
- [ ] **Validate mirroring visually.** Baked and wired (trajectory/root X-flip, Left/Right bone
      swap, runtime mirror pose job) but never checked on screen. Keep `Generate Mirrored
      Variants` off until it has been playtested on a real rig.

## Performance

- [ ] **Stress-test at scale.** No database with tens of thousands of frames has been run; the
      Burst job and the optional KD-tree are dimensioned in theory (chunked parallel scan,
      weight-scaled tree), not measured. Profile a large multi-character-worth of clips before
      relying on either for a shipping budget.
- [ ] **KD-tree is weak above ~15-20 effective dimensions** (a typical feature vector is 40+).
      Useful only for very large databases with no tag filtering; confirm it actually beats
      BurstLinear on your data before enabling it.
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
- [ ] Retargeting through Humanoid so one database serves multiple skeletons.
- [ ] Auto-tagging utilities (speed/turn/idle detection painting tag ranges automatically).
- [ ] Learned Motion Matching (Ubisoft La Forge): decompressor / stepper / projector networks
      replacing the database at runtime for large memory savings. The normalized, well-typed data
      model should make training-data export straightforward when this is picked up.
- [ ] Interop with Timeline / cutscene tooling beyond the basic Mecanim-fade (`SetMatchingActive`).

## Process

- [x] EditMode automated tests (33 tests, `Tests/Editor`) - **done**.
- [x] CI workflow (`.github/workflows/tests.yml`, GitHub Actions via game-ci) - **configured**,
      needs a `UNITY_LICENSE` repository secret added by the repo owner to actually run green.
- [ ] Code coverage measurement/reporting.
- [ ] Contribution guidelines beyond the README's one-paragraph note.
