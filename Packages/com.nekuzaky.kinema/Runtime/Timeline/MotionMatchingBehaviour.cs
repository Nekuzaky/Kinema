#if KINEMA_TIMELINE
using UnityEngine.Playables;

namespace Kinema.MotionMatching.Timeline
{
    /// <summary>
    /// Per-clip data only - carries the fade time a <see cref="MotionMatchingClipAsset"/> was
    /// authored with into the mixer. All activation logic lives in
    /// <see cref="MotionMatchingMixerBehaviour"/>, which is the one actually bound to the track's
    /// <see cref="MotionMatching.MotionMatchingController"/>.
    /// </summary>
    public sealed class MotionMatchingBehaviour : PlayableBehaviour
    {
        public float FadeTime = 0.2f;
    }
}
#endif
