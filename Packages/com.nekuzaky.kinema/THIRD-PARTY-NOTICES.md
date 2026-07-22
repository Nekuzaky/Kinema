# Third-Party Notices

Kinema Motion Matching (the code in this package) is licensed under the MIT License — see
[LICENSE.md](LICENSE.md). This file records everything in or around the package that is **not** ours,
so there is no ambiguity about what a buyer receives and what they do not.

## What ships in this package

Only first-party code and documentation. No third-party runtime libraries, no mocap, no rigs, no
textures. The runtime depends only on Unity's own packages, declared in `package.json`:

- `com.unity.burst`
- `com.unity.mathematics`

Optional, and only if the consuming project already has them:

- `com.unity.timeline` — the Timeline integration compiles only when it is present.
- Unity's Input System — used by the **Locomotion Demo** sample, not by the runtime.

## What does NOT ship, and must be supplied by you

The package contains **no animation data and no character**. The demo needs a rig you provide.

### Adobe Mixamo (used during development, not redistributed)

The character used while developing the demo comes from [Adobe Mixamo](https://www.mixamo.com/) and
is subject to Adobe's Mixamo license. **It is not included in this package.** Mixamo assets may be
used in your own projects under Adobe's terms but may not be redistributed as a standalone asset, so
they are not part of what is published here. Drop your own Humanoid FBX into the sample's Character
folder to run the demo.

### Opsive OmniAnimation (optional integration, not redistributed)

An optional integration can bake a database from the
[Opsive OmniAnimation](https://opsive.com/) mocap pack. It is **compiled out by default** — the code
only exists when you define `KINEMA_OPSIVE`, and it never copies or redistributes the pack. It bakes
from a copy you already own and license from Opsive. Without the pack and the define, this integration
is not present in the build at all.

## Attribution for buyers

If you ship a game built with Kinema, no attribution is required by the MIT license (though it is
appreciated). The animation and character assets you supply carry their own licenses and their own
attribution requirements — those are between you and their authors, and nothing in Kinema changes
them.
