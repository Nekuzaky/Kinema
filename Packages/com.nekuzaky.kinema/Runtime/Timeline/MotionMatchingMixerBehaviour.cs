#if KINEMA_TIMELINE
using UnityEngine.Playables;

namespace Kinema.MotionMatching.Timeline
{
    /// <summary>
    /// Bound (via <see cref="MotionMatchingTrack"/>'s <c>TrackBindingType</c>) to a
    /// <see cref="Kinema.MotionMatching.MotionMatchingController"/>. Extends the existing
    /// <see cref="Kinema.MotionMatching.MotionMatchingController.SetMatchingActive"/> Mecanim-interop fade to
    /// Timeline: while any clip on the track has nonzero blend weight, matching is faded in (a
    /// cinematic-authored AnimatorController state can drive the character outside the clip); when no
    /// clip is active, the pre-clip state is restored. Clip boundaries mid-blend both report nonzero
    /// weight, so overlapping/crossfaded clips on the track keep matching continuously active across
    /// the transition rather than flickering it off and on.
    /// </summary>
    public sealed class MotionMatchingMixerBehaviour : PlayableBehaviour
    {
        private bool _stateCaptured;
        private bool _stateBeforeControl;
        private bool _controllingLastFrame;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var controller = playerData as Kinema.MotionMatching.MotionMatchingController;
            if (controller == null) return;

            int inputCount = playable.GetInputCount();
            bool controlling = false;
            float fadeTime = 0.2f;

            for (int i = 0; i < inputCount; i++)
            {
                if (playable.GetInputWeight(i) <= 0f) continue;
                controlling = true;
                var clip = (ScriptPlayable<MotionMatchingBehaviour>)playable.GetInput(i);
                fadeTime = clip.GetBehaviour().FadeTime;
                break;
            }

            if (controlling && !_controllingLastFrame)
            {
                if (!_stateCaptured)
                {
                    _stateBeforeControl = controller.IsMatchingActive;
                    _stateCaptured = true;
                }
                controller.SetMatchingActive(true, fadeTime);
            }
            else if (!controlling && _controllingLastFrame)
            {
                controller.SetMatchingActive(_stateBeforeControl, fadeTime);
            }

            _controllingLastFrame = controlling;
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            _stateCaptured = false;
            _controllingLastFrame = false;
        }
    }
}
#endif
