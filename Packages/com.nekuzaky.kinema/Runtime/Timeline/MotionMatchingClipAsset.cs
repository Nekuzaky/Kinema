#if KINEMA_TIMELINE
using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Kinema.MotionMatching.Timeline
{
    /// <summary>
    /// A clip on a <see cref="MotionMatchingTrack"/>. While it (or an overlapping/crossfaded
    /// neighbour) has nonzero blend weight, the bound <see cref="Kinema.MotionMatching.MotionMatchingController"/>
    /// is faded to motion-matching-driven; outside any clip it is restored to whatever it was before
    /// the track took over. No end-of-clip fade time is exposed separately - <see cref="FadeTime"/>
    /// covers both directions, since Timeline's own clip ease-in/ease-out already controls how the
    /// blend weight itself ramps.
    /// </summary>
    [Serializable]
    public class MotionMatchingClipAsset : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("Fade time (seconds) passed to SetMatchingActive when this clip gains or loses control.")]
        [SerializeField] private float _fadeTime = 0.2f;

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<MotionMatchingBehaviour>.Create(graph);
            playable.GetBehaviour().FadeTime = Mathf.Max(0.01f, _fadeTime);
            return playable;
        }
    }
}
#endif
