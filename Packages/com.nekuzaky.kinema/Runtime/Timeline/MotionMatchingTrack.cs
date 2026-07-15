#if KINEMA_TIMELINE
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Kinema.MotionMatching.Timeline
{
    /// <summary>
    /// Drives a <see cref="Kinema.MotionMatching.MotionMatchingController"/> from Timeline: drop
    /// <see cref="MotionMatchingClipAsset"/> clips on this track to fade matching in for their
    /// duration (e.g. a cutscene AnimatorController state hands off to live gameplay locomotion at
    /// the clip's start), then back to whatever the controller was doing before once no clip on the
    /// track is active. Bind the track to the character's <see cref="Kinema.MotionMatching.MotionMatchingController"/>
    /// in the Timeline window like any other track binding.
    /// </summary>
    [TrackClipType(typeof(MotionMatchingClipAsset))]
    [TrackBindingType(typeof(Kinema.MotionMatching.MotionMatchingController))]
    public class MotionMatchingTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<MotionMatchingMixerBehaviour>.Create(graph, inputCount);
        }
    }
}
#endif
