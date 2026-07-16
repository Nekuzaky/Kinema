# Kinema against the primary sources

What the published work says, what Kinema does, and where they disagree. Written so a disagreement is
a decision someone made rather than something nobody noticed.

Sources:

- **Clavet 2016** — Simon Clavet, *Motion Matching and The Road to Next-Gen Animation*, GDC 2016
  ([slides](https://archive.org/details/GDC2016Clavet)). The original talk; For Honor.
- **Mach & Zhuravlov 2021** — *Motion Matching in 'The Last of Us Part II'*, GDC 2021
  ([GDC Vault](https://www.gdcvault.com/play/1027118/Motion-Matching-in-The-Last), paywalled).
- **Holden 2020** — Holden et al., *Learned Motion Matching*, SIGGRAPH 2020.
- **Holden 2022** — *Inertialization Transition Cost*
  ([theorangeduck](https://theorangeduck.com/page/inertialization-transition-cost)).
- **Bollo 2018** — David Bollo, *Inertialization: High-Performance Animation Transitions in Gears of
  War*, GDC 2018.

## The foot-slide diagnosis is theirs, not ours

Clavet 2016 lists the causes of foot sliding directly: **"Blending very often"** and **"Keeping up
with gameplay"**. Holden puts the same thing the other way round: when the search constantly returns
motions that deviate from the target trajectory, the result is considerable foot sliding.

Kinema measured, on the demo at v1.29: **93% of searches jumped**, foot slide **0.68-1.47 m/s**
against an Analysis tab that calls anything over 0.15 heavy. That is the failure both sources
describe, and it is why v1.30.0 went after the clip-change brake rather than the IK.

Both sources reach for foot locking *as well*, not instead: Clavet locks the toe "when it doesn't
move too much in the main animation"; Holden blends the locked foot in with inertialization and an
analytical two-joint IK. Kinema has `FootLockIK`. Locking hides residual slide; it does not fix a
character that is changing its mind nine times a second.

## Where Kinema disagrees with Clavet

| | Clavet 2016 | Kinema | Why |
|---|---|---|---|
| Search rate | every frame | every 0.1 s (`_searchInterval`) | 10 Hz is what makes crowds affordable. Clavet's budget was one player character. |
| Same-clip hysteresis | `same anim && abs(dt) < 0.2` | same, `_continuityWindow = 0.12` | Same mechanism, shorter window. Untested against 0.2. |
| Clip-change penalty | **none** | `_clipChangeCost`, 25% of the continuation cost | See below. |
| Trajectory weighting | `cost += responsivity * futureCost` | per-group weights (`FeatureWeights`) | Same knob, more of them. |
| Transition | 0.25 s crossfade | inertialization (Bollo 2018) or 2-slot crossfade | Inertialization is strictly newer than the talk. |

**The clip-change penalty is ours, and the sources do not have it.** Clavet applies no bias toward
the current clip: stability is supposed to *emerge* from the cost function plus the hysteresis
window, and he is explicit that the character "switches multiple times per second" by design. So
`_clipChangeCost` is a brake the original does not need.

That is worth being uncomfortable about. Two readings, and they are not exclusive:

1. The penalty is a legitimate engineering knob the talk omits, and 25% is a reasonable default.
2. The penalty is **compensating for a cost function that is not discriminative enough** - if the
   costs were right, the current clip would win on its own merits and no brake would be needed.

Reading 2 is more likely, and the next section is why.

## The change the sources actually point at

Kinema's pose features are the naive form: bone positions and bone velocities as separate blocks,
summed with hand-tuned weights.

```
[ TrajPos 2T | TrajDir 2T | BonePos 3B | BoneVel 3B | RootVel 2 ]
```

Holden 2022 argues this is wrong on two counts, and both apply here verbatim:

1. **Mixed units.** Positions and velocities are added together, so the weights that balance them are
   arbitrary - which is exactly why Kinema ships a weight tuner. The demo config is the illustration:
   `BonePosition: 1`, `BoneVelocity: 0.6`. Nothing derives that 0.6; it was tuned until it looked
   right.
2. **The relationship is thrown away.** Under an inertialized transition, a positive position offset
   is *fine* if it comes with a negative velocity offset of the right size, because the spring
   absorbs it. Summing the two magnitudes separately cannot see that, so it rejects transitions that
   would have been smooth and accepts ones that will not be.

His fix is to define transition cost as the displacement an inertialized transition actually causes.
For a critically damped spring at position `x` with velocity `v` and half-damping `y`:

```
integral of the decay curve = (2*x*y + v) / y^2
```

which rearranges into **one composite feature per bone**, replacing both blocks:

```
(2 * pos / y) + (vel / y^2)
```

He reports it working with "no tweaking of velocity and positional weights required".

For Kinema that would mean:

- Pose features go from `3B + 3B` to `3B`. The demo bake is `T = 6` trajectory samples and `B = 3`
  bones (Hips, LeftFoot, RightFoot), so `4T + 6B + 2 = 44` today and `4T + 3B + 2 = **35**` after -
  a fifth off every distance evaluation, and a smaller training set for
  [Learned Motion Matching](Learned-Motion-Matching).
- `BoneWeights` stops needing a velocity/position balance at all.
- The cost starts measuring *what the transition will look like* instead of a proxy for it - which is
  the thing `_clipChangeCost` is currently papering over.

**Shipped in v1.32.0** as `PoseCostMode.InertializationCost`, opt-in on the schema. `Naive` stays the
default, so no existing bake changes meaning; a config with no mode field deserializes to `Naive`.
Switching modes requires a rebake, and `IsLayoutCompatibleWith` refuses to mix them - two schemas can
agree on every count and still write different numbers into the same slots, which is the one
incompatibility that would otherwise pass silently.

Not yet measured on real data: whether it lowers the jump rate, and what it does to foot slide.

## Open, honestly

- v1.30.0's relative clip-change brake **has not been observed running**. The number that settles it
  is the `jump rate` on the stats label: 93% before; near 0-20% would mean it took.
- `_jumpImprovementThreshold` is 2%, which is very permissive, and was left alone deliberately while
  the brake was the variable under test.
- `_continuityWindow` is 0.12 s where Clavet used 0.2 s. Untested either way.
- The demo caps at 3 m/s because that is the top the bake covers - not a number from any source.
