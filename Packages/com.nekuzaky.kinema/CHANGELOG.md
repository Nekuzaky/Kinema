# Changelog

All notable changes to this package are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.19.1] - 2026-07-16

### Fixed
- Batched search + teardown race: a controller disabled (or database-switched) between scheduling
  its batched search in Update and the batch's LateUpdate disposed the matcher's NativeArrays while
  the Burst job still read them - a safety-system error in the editor, a race in a build. Teardown
  now completes the pending handle first, and a stale handle arriving after re-initialization is
  completed but its outcome discarded (it says nothing about the new matcher's buffers). PlayMode
  regression test covers the exact window (coroutine resumes between Update and LateUpdate).
- `MotionMatcher.Dispose` now defensively completes the last `ScheduleSearch` handle, so direct API
  users can't dispose under a running job either.
- Timeline mixer restored the state captured before the FIRST control span forever: a script
  toggling matching between two clips was overwritten by the second clip's restore. State is now
  captured fresh at each span's start (EditMode regression test).

### Changed
- `MotionMatchingLOD.BaseInterval` is now public: runtime code changing a LOD-managed character's
  cadence must write it here - writing `SearchInterval` directly was silently stomped on the next
  recompute.
- `MotionMatchingSearchBatch.Register`/`Unregister`: route controllers spawned after the batch
  enabled (the OnEnable auto-collect only sees what already exists).
- Removed a duplicate undo step in the Tags tab's auto-tag button (`ApplyModifiedProperties`
  already registers one).

101/101 EditMode tests, 10/10 PlayMode tests.

## [1.19.0] - 2026-07-16

### Added
- Auto-tag apply: "Detect and apply gait tags" button in the Tags tab writes `GaitClassifier`
  suggestions into the config as real `ClipTagTrack` ranges via `AutoTagApplier` - same
  SerializedObject path as hand-authoring (undo works), tag names created in the vocabulary as
  needed, re-applying replaces the touched clips' ranges instead of stacking, hand-authored tracks
  on untouched clips are preserved. Rebake afterwards to bake the tags into the database. Unit
  tested (5 tests, read back through the config's public API and `MaskAt`).
- In-player search benchmark: the standalone smoke test now runs the editor benchmark's clustered
  synthetic measurement (5k frames x 44 dims) inside the built player and logs a
  `[KinemaSmoke] BENCH` line. Measured locally: mean 44.6 us / p99 86.1 us in the player vs
  87.4 / 138.3 us in-editor - build is ~2x faster, editor numbers are a conservative bound.

### Fixed
- Smoke-test build script left its temp scene in Assets: `EditorApplication.Exit` terminates the
  process before `finally` runs; the scene is now deleted before exiting.

100/100 EditMode tests (5 new), 9/9 PlayMode tests.

## [1.18.0] - 2026-07-15

### Added
- `Kinema.MotionMatching.Timeline` (new optional assembly, only compiles with `com.unity.timeline`
  installed): `MotionMatchingTrack` + `MotionMatchingClipAsset` extend `SetMatchingActive` to
  Timeline - a clip on the track fades matching in for its duration, restoring the prior state once
  no clip on the track is active; stays active across an overlapping crossfade between two clips.
  `MotionMatchingController.IsMatchingActive` added (previously no way to read the current
  active/inactive target). Mixer logic verified by building a real `PlayableGraph` and calling
  `Evaluate()` directly - no Timeline asset or window needed.
- `BlendSpaceMath` + `MotionMatchingBlendSpace`: 2D Gradient Band Interpolation weighting, grid
  generation and feature-row blending for MxM-style blend spaces - unit tested, **not yet wired
  into the baker** (playback replays the matched frame's original `AnimationClip`, so a synthetic
  grid point needs a real baked clip to be playable, not just a blended feature row to be matchable;
  see TODO.md).
- `MotionMatcher.ScheduleSearch`/`CompleteSearch`: non-blocking split of `Search`, so several
  matchers' Burst jobs can be scheduled together and completed together instead of each blocking
  the main thread in series. Addresses the "many characters searching concurrently" profiling gap -
  `Tools > Kinema > Benchmark Search` now measures it (8/32/128 synthetic characters; ~1.7-1.8x
  faster batched than sequential in local measurements). No controller uses this yet.

- First PlayMode tests (`Tests/Runtime`, 4 tests): a real controller on a synthetic database wrapped
  around a procedurally-authored `AnimationClip` - initialization via `SwitchDatabase`, scripted
  intent sweeps with frame/clip mapping asserted in range, the `SetMatchingActive` fade surviving
  ticking, disable/re-enable teardown. Headless: `-runTests -testPlatform PlayMode` (no
  `-nographics`).
- `GaitClassifier` + `Tools > Kinema > Log Auto-Tag Suggestions`: speed/turn/idle detection from
  the motion itself (denormalized root velocity: speed thresholds for idle/walk/run, direction
  change in deg/s for turning, flicker smoothing, consolidated per-clip ranges). Suggestions are
  logged, never written to a config. Verified against the baked Opsive set: detected turn spans
  line up with the turn-named clips without reading any names.
- Snapshot diffing (Debug tab > History): "Pin for diff" pins the scrubbed decision; scrubbing
  elsewhere then shows deltas - per-group costs with the biggest mover called out, frame/clip/jump
  changes, character distance, mean intent shift. Math in `SearchSnapshotDiff.Compute` (runtime,
  unit tested); panel is plain read-only IMGUI.
- First standalone-player verification (`Assets/StandaloneSmokeTest/` in the repo, not the package):
  headless Win64 build + a bootstrap that ticks a real controller 60 frames in the built player and
  prints a `[KinemaSmoke]` verdict. Verified locally: PASS (Burst AOT, live PlayableGraph, real
  searches, outside the Editor). See TODO.md for the URP-migration and antivirus hurdles hit on the
  way.

- `MotionMatchingSearchBatch` + `MotionMatchingController.SearchScheduler`: opt-in cross-character
  search batching. Controllers registered with the batch schedule their periodic search in Update
  and the batch completes them all in LateUpdate, so simultaneous searches overlap on Burst worker
  threads (the ~1.7x the benchmark measures) instead of each blocking the main thread in turn.
  Batched jumps land one graph evaluation later than synchronous ones - documented on the hook.
  Internally `RunSearch` split into `PrepareSearchQuery`/`ApplySearchOutcome`, shared by both paths.
- PlayMode tests for motion events: root-warping lands on its target by contact time (position +
  yaw, fixed timestep), events end on their own and matching resumes, root motion stays bounded.

95/95 EditMode tests (28 new: `MotionMatchingTimelineTests` builds a real `PlayableGraph` and
evaluates it; `BlendSpaceMathTests` covers the interpolation/grid/blend math;
`MotionMatcherScheduleSearchTests` checks the batched path against synchronous `Search`;
`SearchSnapshotDiffTests` covers the diff math; `GaitClassifierTests` covers gait classification on
hand-authored velocity tracks) plus 9/9 PlayMode tests (controller lifecycle, motion-event warping,
search-batch routing and fallback).

## [1.17.0] - 2026-07-15

### Added
- `MotionMatchingLOD`: degrades `MotionMatchingController.SearchInterval` with distance from a
  reference transform (`Camera.main` by default) via a piecewise-linear multiplier over configurable
  distance tiers, recomputed at a throttled rate rather than every frame. Addresses the "Animation
  LOD" gap in TODO.md. `MotionMatchingController.SearchInterval` is now also a public read/write
  property (previously only a serialized field clamped to the inspector's 0.02-0.5 slider range) so
  LOD and similar systems can drive it outside that range.
- Code coverage: `com.unity.testtools.codecoverage` added to `Packages/manifest.json`. Verified
  locally via `-enableCodeCoverage -coverageOptions "generateAdditionalMetrics;generateHtmlReport"`
  (OpenCover XML + HTML report); wired into the CI workflow below.
- `.github/workflows/tests.yml` re-added (EditMode tests + coverage report as build artifacts).
  Needs a `UNITY_LICENSE` repository secret set on the GitHub repo before it runs green - documented
  in the workflow file; not verifiable from a local checkout.
- `CONTRIBUTING.md`: setup, pre-PR checklist (run EditMode tests; say what you verified by eye vs.
  couldn't for the areas with no automated coverage), where things live in the package.

63/63 EditMode tests (10 new: `MotionMatchingLODTests` covers the distance-to-multiplier math
directly - the only part of the LOD system judgeable without a running scene;
`MotionMatchingControllerTests` covers the new `SearchInterval` clamp). Whether a given distance-tier
configuration still reads as acceptable on screen has not been judged visually - tune per project and
playtest; flagged in TODO.md.

## [1.16.0] - 2026-07-15

### Added
- GitHub Sponsors button on the repo (`.github/FUNDING.yml` -> github.com/sponsors/Nekuzaky).
- Welcome popup: opens once per installed/upgraded version (tracked in `EditorPrefs` by version, so
  it reappears on upgrade but stays quiet otherwise), brief quick-start, and three buttons -
  Documentation, Sponsor, Website (nekuzaky.com) - plus Close. Reopen anytime via
  `Tools > Kinema > Welcome`.

## [1.15.1] - 2026-07-15

### Fixed
- Crash right after any motion event ended (vault, and the new free jump - the latter is easy to
  spam, which is how this surfaced): `IndexOutOfRangeException` in `MapClipTimeToFrame`, thrown on
  the very next search. `PlayEvent` marks the slot with clip index -1 (external clip, not in the
  database). On end it resumes matching immediately, but `ContinuationFrame` was still reading that
  stale `-1` out of the slot instead of the frame actually being continued from, and indexed the
  clip array with it. Fixed to derive both the clip and the continuation time from the frame being
  continued, falling back to the slot's own precise clock only when the slot is confirmed to be
  playing that same clip (normal play keeps the sub-frame precision it had).
- `MapClipTimeToFrame` now rejects an invalid clip index with a clear `ArgumentOutOfRangeException`
  instead of raw `IndexOutOfRangeException` - defense in depth, so a similar mistake fails at the
  call site with a message instead of a few frames later with none.

52/52 EditMode tests (2 new, pinning the guard). The crash path itself needs a live PlayableGraph
(events, IK) to reproduce, which EditMode cannot drive - not covered by an automated regression
test; flagged in TODO.md.

## [1.15.0] - 2026-07-15

### Added
- Free jump: Space (gamepad South) with no obstacle ahead now plays a jump event - RunJumpRight
  while moving, IdleJump from standstill - unwarped, so the arc is the clip's own root motion.
  Previously the button only vaulted, and only inside the 0.35-1.15 m obstacle window; open-ground
  presses were eaten silently.
- Every jump press now logs what it decided (vault over X / free jump / nothing bound and why), so
  "it does not jump" becomes diagnosable from the Console.
- Traversal course in the demo scene: three vault walls across the height window, gapped platforms
  sized to the run-jump's travel, and rising steps - one lane that exercises the whole event system
  in a single run.

### Notes
- With the v1.14.2 pruner fix actually comparing the right frames, idle pruning now removes 2,080
  frames (4,705 -> 2,625, -44%); the buggy comparison had been under-pruning at -21%.

## [1.14.2] - 2026-07-15

Audit pass over the v1.13-v1.14 code, adversarial reading rather than new features.

### Fixed
- Idle pruning compared each candidate frame against the wrong frame: the "last kept" index belongs
  to the output list but was dereferenced into the input list. The two agree until the first removal,
  then drift further apart with every one, so keep/drop decisions were made against increasingly
  unrelated poses. Now reads the output list; three tests pin the semantics (identical idles
  collapse, comparison target is the last KEPT frame, moving frames are never pruned).

### Audited clean
- Live pose query: normalization and character-space handling match the bake exactly (both sides
  include root velocity in bone velocities).
- Foot phase: circular distance and absence gates correct in the Burst job; lead-in/tail extension
  handles negative offsets; -1 sentinel respected end to end.
- Critical-spring prediction: transient and its closed-form integral verified against the math.
- KD-tree invalidation on weight change; bake pipeline ordering (prune -> mirror -> phases) sound;
  time-to-frame mapping is a binary search on real frame times, correct under pruning gaps.

### Known bias (documented, unchanged)
- Deviation-triggered search measures deviation in the character's local frame, so self-rotation
  contributes to it; bounded by the v1.14.1 cooldown. Left alone until measured.

## [1.14.1] - 2026-07-15

### Fixed
- Pose flicker while moving (v1.13 regression, reported on video). Two causes, both mine:
  - The live pose query sampled the skeleton at the start of Update - AFTER the previous frame's
    foot lock and ground adaptation IK had moved the feet. IK-displaced feet resemble no baked
    frame, so every search found something "better", jumped, blended into an even stranger pose,
    and jumped again: a feedback loop. The pose is now sampled straight after the graph evaluates,
    before the LateUpdate IK passes touch it.
  - Deviation-triggered searches had no floor: a sustained turn deviates continuously, so the
    search fired every frame and each search was a chance to jump. A cooldown (default 60 ms) now
    bounds the rate; the timer-based interval is unchanged.
- Both features remain serialized toggles on the controller (Live Pose Query, Search On Deviation)
  and can be disabled outright if needed.

## [1.14.0] - 2026-07-15

### Added
- `Tools > Kinema > Benchmark Search`: measures the real Burst search and reports mean/median/p99
  per search, plus the number a game actually asks - how many characters can search at 10 Hz inside
  a 60 fps frame. Queries are real database rows with noise, not random vectors, because the
  branch-and-bound early-out makes a plausible query and a nonsense one take different paths.
  Synthetic clustered databases (5k to 400k frames) chart the growth curve past whatever is baked
  locally, and the baked database is measured twice - first and last - so an order-dependent result
  is visible rather than believed.

### Measured (Ryzen, 20 workers, Burst on, 44 dims)

| database | search | p99 | characters @10 Hz in a 60 fps frame |
|---|---|---|---|
| demo pack, 3,699 frames | 82 us | 128 us | ~780 |
| synthetic 25,000 | 337 us | 459 us | ~218 |
| synthetic 100,000 | 1,194 us | 1,422 us | ~70 |
| synthetic 100,000, KD-tree | 267 us | 450 us | ~222 |
| synthetic 400,000 | 4,694 us | 5,290 us | ~18 |
| synthetic 400,000, KD-tree | 942 us | 1,680 us | ~59 |

The linear scan grows linearly, as expected, and the KD-tree earns its keep past ~100k frames
(3x). Below ~25k frames the linear scan wins on both latency and simplicity - which is where the
demo, and most projects, live.

### Notes
- The first version of this benchmark was wrong and said so under testing: whichever database was
  measured first reported ~10x its true cost, because Burst compiles asynchronously in the editor
  and the early samples ran as managed IL. Reordering the runs moved the slow result with the
  order. Fixed by forcing synchronous Burst compilation; the first/last re-measurement of the same
  database now agrees within 8%, and that check ships with the benchmark.

## [1.13.0] - 2026-07-15

Core matching release: six improvements to the matcher itself, the first since v1.4.

### Added
- Live pose query. The query's pose half was copied from the database row being played, but the
  rendered pose is not that frame any more - inertialization, stride warping and IK all moved it.
  That is why the pose cost read exactly 0.000. The query now samples the schema's bones off the
  rig as rendered (positions and velocities in character space, the baker's own math), verified by
  a test feeding a skeleton standing exactly on a baked frame. Falls back to the frame copy when a
  schema bone is missing on the rig.
- Foot-phase cost term. Gait phase (0..1 through a step cycle) is baked from the contacts, stored
  as a parallel channel - the feature layout, KD-tree and old databases stay intact - and the
  matcher adds a circular phase distance so candidates that would cut a stride mid-step cost more.
  Inert at weight 0 (every existing config) and on frames with no cycle (idles carry -1).
- Critically damped spring prediction (default; the old exponential lag stays selectable). Velocity
  starts changing with zero acceleration and ramps in, the way a body with mass commits to a turn.
  Closed-form, verified against numerical integration.
- Deviation-triggered search: prediction runs every frame, and when the predicted future points
  drift beyond a threshold from what the last search answered, the search fires that frame instead
  of up to a full interval later. Character space keeps the criterion stable in straight lines.
- Idle pruning (opt-in on the config): near-identical consecutive idle frames are dropped at bake
  time. On the demo pack: 4,705 -> 3,699 frames (-21%), all of it interchangeable standing. Frame
  mapping switched to a binary search over actual frame times, correct on non-uniform grids.
- `WeightTuner`: coordinate descent over the matching weights, one deterministic replay of a
  recorded take per evaluation, scored by foot slide + jump rate. Record a lap, add the component
  next to a ReplayLocomotionProvider, call StartTuning, wait; best weights are applied and logged.

### Notes
- The demo config now bakes with phases, pruning and a 0.5 FootPhase weight. Existing configs are
  untouched: the phase term needs a rebake plus a nonzero weight to do anything.
- 47/47 EditMode tests (8 new across query, predictor and phase). Play-mode feel is not visually
  verified; the deterministic replay + Analysis tab is the intended way to measure it.

## [1.12.0] - 2026-07-15

### Added
- Ghosts on any body: hand a different Humanoid model to the spawner (Director > Ghosts > Ghost rig,
  or the sample director's Ghost Rig field) and the ghost is built on it - controller settings and
  database references copied over, Humanoid retargeting mapping the recorded performance onto the
  new proportions. A non-Humanoid rig is refused with a console warning, since the database cannot
  retarget onto it.
- Every ghost records its own pose from its first frame. Director > Ghosts > Bake Ghost Clip turns
  that performance into an AnimationClip - swap the rig, spawn, bake, and the take exists as a clip
  on the new character.
- `MotionMatchingController.SetAnimator`: re-point the controller before it initializes; the
  ghost-on-a-different-rig path needs it because the copied settings still name the source Animator.

## [1.11.0] - 2026-07-15

### Removed
- The in-game browser overlay (`AnimationBrowser`). Everything it did lives in the window's
  Director tab now, so the game view carries no UI at all. The in-game shortcuts stay: R/Select
  records, G/right-shoulder spawns a ghost, K/dpad-down clears, C/Ctrl/East crouches, Space/South
  vaults. Regenerate the demo scene to drop the now-missing component from existing scenes.

## [1.10.0] - 2026-07-15

Clean-game-view release: everything the in-game overlay did now lives in the window.

### Added
- Director > Tags: the require/exclude stance filter moved into the window.
- Director > Take timeline: CapCut-style navigation over a recorded take. The speed curve is the
  filmstrip, the playhead scrubs the newest ghost along the recorded trajectory (transport buttons
  and frame stepping included). Scrubbing snaps the ghost's root to the recorded transform; its
  matcher re-solves the pose from there, so the trajectory is exact and the animation approximate.
  Takes recorded with the in-game hotkeys appear too - the timeline reads them off the ghost.
- Director > Character: swap the character's rig in one click (edit mode). Components, settings and
  database references move to the new body via the component clipboard, the controller's Animator is
  re-pointed, and anything following the old transform (camera, AI targets) is retargeted. Humanoid
  retargeting maps the data onto the new proportions.
- `ReplayLocomotionProvider.Paused` / `ScrubTo(frame)`: the runtime side of the take timeline.

### Changed
- The in-game browser overlay is hidden by default: the game view starts clean; Tab or gamepad
  Start opens it. Regenerate the demo scene (or untick Visible On Start) for existing scenes.

## [1.9.0] - 2026-07-15

Director release: the record/ghost/playback controls move into the editor window, and the demo
gets full gamepad coverage.

### Added
- Director tab in the Motion Matching window. Plays any database clip on the live character like a
  custom Animator: transport controls, a scrubbable timeline with per-foot contact lanes drawn from
  the baked data, pause, and a filterable clip list. Playback is the clip-override path, so what is
  on screen is the baked clip through the real graph - IK and retargeting included.
- Recording from the window: capture intent and pose together, save the session as an asset, bake
  the pose take to an AnimationClip - same recorders the in-game overlay drives.
- Ghost direction from the window: spawn and clear ghosts without touching the game overlay.
- `GhostSpawner` (runtime): ghost creation shared by the window and the sample hotkeys. Strips the
  clone by whitelist (controller, IK) rather than blacklist, so project gameplay scripts are removed
  without the runtime knowing their types. Ghosts keep no collision motor - the traditional rule.
- `MotionMatchingController.OverridePaused` / `SetClipOverrideTime`: freeze and scrub the override
  clock, which is what makes the timeline a timeline.
- Full gamepad map: record on Select, ghost on right shoulder, clear on dpad-down, browser overlay
  on Start (all alongside the existing keyboard keys), and the follow camera now orbits with the
  right stick or middle-mouse drag - identical framing to before until the stick is touched.
- Example take shipped with the sample (`Takes/ExampleTake.asset`): a recorded session - intent
  only, velocities and trajectory - replayable by ghosts out of the box.

### Changed
- Window header shows the package version and a LIVE pill during play.

### Notes
- Recorded pose takes made from the Opsive pack are resampled licensed mocap and stay out of the
  repository; the example take carries intent only, which is why it can ship.

## [1.8.1] - 2026-07-15

### Changed
- Demo locomotion tuned to reduce foot sliding, which the recording surfaced (foot slide 0.17-0.27
  m/s, 3 clip changes per second). Two evidence-based causes:
  - `LocomotionInputProvider` top speed 4 -> 3 m/s. Measured against the Opsive set, ~6.5% of frames
    sit within stride-warp range of a 3 m/s request versus ~2.8% at 4 m/s; 66% of the set is below
    0.5 m/s, so run data is thin and asking for 4 m/s lands in its sparsest region. A starved search
    flickers between clips and pins the stride warp at its 1.3x ceiling, both of which slide the
    planted foot.
  - The demo character's clip-change cost is set to 0.25 (from the 0.1 default) so the search stops
    hopping between clips several times a second; each hop blends, and a blend drags the planted foot
    far enough to break the foot lock.
- Both are demo-scene values, not package defaults - the runtime defaults are unchanged.

### Notes
- Measured headlessly: the database speed histogram and per-speed stride-warp reachability. The foot
  slide itself could not be measured headless - it needs the PlayableGraph's animation jobs, which do
  not initialise under -batchmode -nographics - so the on-screen improvement is reasoned from those
  numbers, not yet confirmed on a running character.

## [1.8.0] - 2026-07-15

### Changed
- One demo generator: `Tools > Kinema > Demo Scene`. It picks its own source, best first - an
  installed mocap pack, otherwise an FBX in the sample's Character folder (using its clips, or
  generating a procedural set if it is only a skin) - bakes it, and builds the scene. There were four
  generators before (`Demo Scene`, `Setup > Demo From FBX`, `Setup > Demo From Opsive Pack`,
  `Setup > Placeholder Scene`), each with its own idea of what the demo scene was.
- Every source now funnels through one bake contract (`DemoBake`: rig, database and vault event
  paths) and one scene builder, so the demo means one thing rather than one thing per source.

### Removed
- The `Setup` submenu and the placeholder-scene generator. A capsule with no data has nothing left to
  demonstrate now that the generator resolves its own source.

## [1.7.1] - 2026-07-15

### Fixed
- Every small label in the window rendered as nothing: stat cards showed a number with no caption,
  clip bars had no names, subsystem rows were bare dots, and Summary collapsed to an empty box. The
  key/value styles were built from `EditorStyles.label` with `fontSize` forced down, which leaves the
  style without a font at that size - the text then measures and draws as nothing, which is why rows
  collapsed while the bars and numbers beside them kept rendering. They now derive from
  `EditorStyles.miniLabel`, which is already small, and are rebuilt when the editor skin changes.
- The window opened on whichever database the project returned first - typically the small starter
  set (187 frames) rather than the real one (4,705). It now opens on the richest baked database and
  pairs the matching config with it.

## [1.7.0] - 2026-07-15

Recording release, plus one tool instead of three.

### Added
- `PoseRecorder` + `PoseClipBaker`: record the skeleton as actually posed on screen - after matching,
  blending, stride warping and IK - and bake it into a real AnimationClip asset via
  `Tools > Kinema > Save Last Take As Animation Clip`. This is a different capture from
  `SessionRecorder`, which stores intent: replaying intent re-runs the matcher and can produce a
  different (equally valid) performance, while a pose take is the performance itself.
- `GhostReplayDirector`: record with R, and an NPC clone is sent out to redo your trajectory. The
  ghost is driven by your recorded *intent* and runs its own matching against it, so it reproduces
  where you went rather than replaying a video of you. G spawns another, K clears them.
- Vault event is now authored from the mocap pack's running jump, so Space works in the pack scene.
- Browser overlay gained a recording section (take length, ghost count, record/ghost/clear).

### Changed
- `Tools > Kinema > Demo Scene` now runs the entire setup itself: it bakes every clip in the
  installed mocap pack and then builds the scene. No separate setup trip first.
- One scene builder for every entry point, instead of each setup defining its own demo scene.

### Fixed
- Space did nothing in the pack scene: `VaultTrigger` was only ever added by the procedural FBX
  setup, so the pack scene never carried it and no vault event existed to trigger. Not an Input
  System problem, which is where it looked like it pointed.
- Scene building lost every asset reference wired before the scene was created. `NewScene` unloads
  unreferenced assets and destroys their instances, leaving Unity fake-nulls: the C# reference is
  alive, `== null` is true, and the wiring silently drops. The builder now takes paths and loads from
  disk after the scene exists.
- Ghost clones kept the controller's cached input provider after it was stripped, so they would have
  stood still; `SetLocomotionProvider` rebinds them to the replay.
- `ReplayLocomotionProvider.ForceRecordedTimestep` is now settable. It drives the engine clock, which
  is right for deterministic analysis and wrong for a ghost running next to a live player - it would
  dictate the player's frame rate too.

## [1.6.1] - 2026-07-15

### Fixed
- Opsive setup baked and played on the pack's own rig, which is a bare skeleton: 68 joints, a valid
  Humanoid avatar and no mesh at all. The character animated correctly and rendered nothing, which
  looks exactly like the scene failing to build. The setup now detects a skinless pack rig and falls
  back to the demo's skinned character, letting Humanoid retargeting map the mocap onto it. Baking
  happens on that same rig, so the features describe the body on screen rather than a proxy of it.
- The demo model is forced to Humanoid on import when used this way; retargeting only exists in
  muscle space, so a Generic import silently made the mocap unusable.
- `Tools > Kinema > Demo Scene` now warns when the resolved rig carries no skinned mesh, instead of
  handing over an empty-looking viewport.

### Changed
- All menus consolidated under `Tools > Kinema`. There were two entry points (a top-level `Kinema`
  menu and `Tools > Kinema`), which is one too many.

### Notes
- Retargeted bake vs the skeleton bake: peak root speed 5.92 m/s (was 6.47 - proportions differ),
  38% of frames travelling (was 40%), foot contact on 82% of frames (was 53%). The contact jump is
  the more plausible figure: outside a sprint flight phase a foot should almost always be down, so
  the skeleton bake was under-detecting contacts against the 0.15 m height threshold.

## [1.6.0] - 2026-07-15

Data inspection release. The demo answers "does locomotion feel right"; nothing answered "is my
data any good". The matcher only ever shows frames that fit what you happened to be doing, so a
broken, mis-tagged or badly baked clip can sit in a database indefinitely without ever appearing.

### Added
- `Tools > Kinema > Demo Scene`: builds a scene for exercising a whole database. Picks the richest
  baked config in the project automatically, so importing a bigger pack and rerunning picks it up
  with no arguments. Terrain is built to provoke the subsystems flat ground never touches - slopes
  and steps for ground adaptation, a low ledge for the vault event, a long lane for stride warping.
- `AnimationBrowser` sample: in-game overlay (Tab) listing every clip with a name filter, playing any
  of them on demand, toggling every tag as required or excluded, and showing the live frame, contact
  state, stride warp and foot-slide numbers alongside.
- `MotionMatchingController.PlayClipOverride` / `StopClipOverride` / `IsOverridingClip`: force-play a
  database clip with matching suspended. Stride warping is held at 1 for the duration, so what is on
  screen is the clip as baked rather than a scaled version of it.

## [1.5.0] - 2026-07-15

Mocap data release. The demo could previously only be driven by procedurally authored clips, which
capped how real the result could look no matter how good the runtime was. This release adds a setup
path for a real motion capture locomotion pack.

### Added
- `OpsivePackSetup`: one-click setup that builds a config, database and scene from an Opsive
  OmniAnimation locomotion pack. Bakes on the pack's own rig, so features, contact bone names and
  playback all stay on the skeleton the capture was authored for, with no retargeting guesswork.
- Clip auto-tagging from the pack's naming convention: 74 clips are sorted into 12 tags
  (Crouch, Strafe, Backward, Diagonal, Idle, Walk, Run, Sprint, Turn, Start, Stop, Jump) without
  painting a single range by hand.
- `StanceTagController` sample: drives `RequiredTags` / `ExcludedTags` from gameplay stance. A mixed
  pack needs this - nothing in the feature vector says "this pose is crouched", so an unfiltered
  search will answer a standing query with a crouched frame whose feet happen to line up.

### Notes
- The pack itself is per-seat licensed Asset Store content and is not redistributed here. Import it
  into your own project, then run Kinema > Motion Matching > Setup Demo From Opsive Pack. The
  generated config, database and scene are git-ignored for the same reason.
- Measured on the baked set: peak root speed 6.47 m/s, 40% of frames travelling above 0.3 m/s, a
  foot contact detected on 53% of frames.

## [1.4.0] - 2026-07-15

Movement realism release.

### Added
- Stride warping: clip playback is scaled by requested/baked root speed, so a database holding a
  1.4 m/s walk and a 4 m/s run can travel at any speed between them with the legs cycling in sync
  with the travel. Without it, root-motion locomotion can only move at the discrete speeds present
  in the data and the feet slide the difference. Range-clamped, smoothed, and skipped for idle and
  motion events. Exposed as `CurrentStrideWarp` and surfaced in the Analysis tab.
- `GroundAdaptationIK`: clips are authored on a flat floor, so on ramps and steps the feet float or
  sink. This probes under each foot, plants it on the real surface, tilts it to the slope, and drops
  the pelvis so the lower foot can still reach - height and orientation only, complementing
  `FootLockIK`'s horizontal pinning.
- The demo character now ships with ground adaptation, the quality probe and the session recorder
  wired in.

### Note on realism
System work has a ceiling: the procedural demo clips are sine-wave approximations. Stride warping
and IK remove the mechanical artefacts, but human-looking motion needs mocap data. Drop real
locomotion clips into the sample's `Character/Animations/` folder and re-run the setup.

## [1.3.0] - 2026-07-15

Instrumentation and authoring release: measure the system instead of eyeballing it.

### Added
- Session recording and deterministic replay: `SessionRecorder` captures a play session's
  locomotion intent frame by frame into a `SessionRecording` asset, and
  `ReplayLocomotionProvider` feeds that identical intent back through the controller as an ordinary
  `ILocomotionProvider`. With Force Recorded Timestep the recorded per-frame delta drives
  `Time.captureDeltaTime`, so matching decisions reproduce exactly - two tuning setups can finally
  be compared on the same performance rather than on two different human runs.
- Foot-slide measurement (`MotionQualityProbe`): while the baked contacts flag a foot grounded, any
  world-space travel is slide - the metric animation teams judge locomotion by. Kinema can measure
  it precisely because contacts are baked data rather than a guess. Also reports jump rate (flicker)
  and average/peak matching cost.
- Database coverage analysis (`CoverageReport`, controller frame-usage telemetry): which frames the
  matcher actually reaches for, how much of the database is dead weight, and which clips are never
  selected at all - wasted memory and mocap budget made visible.
- Analysis tab: quality stat cards with plain-English diagnosis, per-clip coverage bars with dead
  clips flagged, and record/save/replay controls in one place.
- Six coverage tests (39 total, all passing).

## [1.2.1] - 2026-07-14

### Fixed
- Settings tab logged "You can't nest Foldout Headers" once per List field: the runtime and config
  parameter groups used `BeginFoldoutHeaderGroup`, which cannot contain the reorderable-list
  drawers those objects expose. Replaced with plain framed sections.

## [1.2.0] - 2026-07-14

Quality-of-engineering release: real state rewind, automated tests, CI.

### Added
- Full-state snapshot rewind: `SearchSnapshot` now captures the character transform and both
  playback slots (clip, time, blend, mirror flag), and `MotionMatchingController.PreviewSnapshot`
  / `StopPreview` replay that exact state through the live graph - the Debug tab's history scrubber
  gained a Preview toggle that visually rewinds the character, not just the recorded numbers.
- EditMode test suite (`Tests/Editor`, 33 tests): feature schema layout and weight expansion,
  `CharacterSpace` round-trips, the trajectory history ring buffer, database normalization and
  accessors, and end-to-end matcher correctness (nearest-neighbour, tag filtering, ignore ranges)
  against synthetic databases built directly via `SetBakedData`.
- GitHub Actions CI (`.github/workflows/tests.yml`) running the suite on every push via
  game-ci/unity-test-runner; needs a `UNITY_LICENSE` repository secret to activate.
- `TODO.md`: the honest remaining-gaps list (PlayMode coverage, standalone build validation,
  large-scale performance, mirroring visual validation, and the not-yet-started feature scope).

## [1.1.0] - 2026-07-14

Tooling and demo-gameplay release.

### Added
- Overview dashboard: stat cards (frames / clips / dims / memory) and a subsystem status board
  (database, contacts, tags, mirroring, profiles, storage) with actionable detail per row.
- Frame Inspector in the Database tab: scrub any baked frame and read back its denormalized data —
  root speed, per-bone positions and weights, grounded feet, active tags.
- Cost sparkline in the Debug tab: total-cost history over the last 120 searches with jump markers.
- Bake pre-flight: rig/clips/schema checklist plus estimated frame count and asset size
  (mirroring and 16-bit storage factored in) before committing to a bake.
- Full parameter surface in Settings: every controller field (live in play mode, immediate weight
  resync) and every config field, plus one-click calibration profile buttons.
- Demo vault: procedural vault clip + motion event with warped contact, VaultTrigger (chest-ray
  probe, obstacle top 0.35-1.15 m, Space / gamepad South), optional Auto Vault for AI.
- AIFollowProvider sample: target-seeking intent through the same ILocomotionProvider contract as
  the player, with arrival slow-down.

### Fixed
- Backward teleport on clip loop: looping slot clocks are now monotonic (manual wrapping made the
  Animator emit a negative root-motion delta every loop).
- Procedural gait: human cadence (one leg cycle per clip), arms lowered from the T-pose with
  chain-composed elbows, level feet, pelvis sway, speed-scaled torso lean.
- FollowCamera auto-targets the matched character when its serialized reference is missing.
- Editor-time fake-null after AssetDatabase.Refresh when wiring freshly created event assets.

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
