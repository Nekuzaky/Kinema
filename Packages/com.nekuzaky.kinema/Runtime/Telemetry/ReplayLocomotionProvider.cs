using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Replays a <see cref="SessionRecording"/> as locomotion intent: the controller cannot tell it
    /// apart from a player. Because the input stream is identical every run, two parameter sets can
    /// be compared on the same performance - the only honest way to tell whether a tuning change
    /// actually improved anything.
    ///
    /// With <c>Force Recorded Timestep</c> the recorded per-frame delta is pushed back through
    /// <see cref="Time.captureDeltaTime"/>, so the matching decisions reproduce exactly rather than
    /// drifting with the current machine's frame rate. That drives the whole player's clock, so keep
    /// it on only while analysing.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Replay Locomotion Provider")]
    [RequireComponent(typeof(MotionMatchingController))]
    public sealed class ReplayLocomotionProvider : MonoBehaviour, ILocomotionProvider
    {
        #region Public

        [SerializeField] private SessionRecording _recording;
        [SerializeField] private bool _playOnStart = true;
        [SerializeField] private bool _loop;

        [Tooltip("Drive the engine clock with the recorded deltas so decisions reproduce exactly. Affects the whole player - analysis only.")]
        [SerializeField] private bool _forceRecordedTimestep = true;

        [Tooltip("Snap the character back to the recorded start pose when the replay begins.")]
        [SerializeField] private bool _restoreStartPose = true;

        public Vector3 DesiredVelocity => _playing ? Current.DesiredVelocity : Vector3.zero;
        public Vector3 DesiredFacing => _playing ? Current.DesiredFacing : Vector3.zero;

        public bool IsReplaying => _playing;

        public bool Loop { get => _loop; set => _loop = value; }

        /// <summary>
        /// Drives the engine clock from the recording. Exact reproduction, but it is a global effect:
        /// leave it off for a ghost running alongside a live player, or the player's own frame rate
        /// gets dictated by the tape. Set before <see cref="Play"/>.
        /// </summary>
        public bool ForceRecordedTimestep { get => _forceRecordedTimestep; set => _forceRecordedTimestep = value; }

        /// <summary>Snap to the recorded start pose when the replay begins. Set before <see cref="Play"/>.</summary>
        public bool RestoreStartPose { get => _restoreStartPose; set => _restoreStartPose = value; }

        public bool PlayOnStart { get => _playOnStart; set => _playOnStart = value; }
        public int FrameIndex => _index;
        public float Progress01 => _recording != null && _recording.FrameCount > 0
            ? Mathf.Clamp01((float)_index / _recording.FrameCount)
            : 0f;

        public SessionRecording Recording
        {
            get => _recording;
            set { _recording = value; _index = 0; }
        }

        #endregion

        #region Private and Protected

        private bool _playing;
        private int _index;

        private SessionFrame Current =>
            _recording != null && _index >= 0 && _index < _recording.FrameCount
                ? _recording.Frames[_index]
                : default;

        #endregion

        #region Unity API

        private void Start()
        {
            if (_playOnStart) Play();
        }

        private void OnDisable() => ReleaseTimestep();

        private void Update()
        {
            if (!_playing || _recording == null) return;

            _index++;
            if (_index >= _recording.FrameCount)
            {
                if (_loop) { _index = 0; ApplyStartPose(); }
                else { Stop(); return; }
            }

            if (_forceRecordedTimestep)
                Time.captureDeltaTime = Mathf.Max(1e-4f, Current.DeltaTime);
        }

        #endregion

        #region Main API

        public void Play()
        {
            if (_recording == null || !_recording.IsValid) return;
            _index = 0;
            _playing = true;
            ApplyStartPose();
            if (_forceRecordedTimestep)
                Time.captureDeltaTime = Mathf.Max(1e-4f, Current.DeltaTime);
        }

        public void Stop()
        {
            _playing = false;
            ReleaseTimestep();
        }

        #endregion

        #region Tools and Utilities

        private void ApplyStartPose()
        {
            if (!_restoreStartPose || _recording == null || _recording.FrameCount == 0) return;
            SessionFrame first = _recording.Frames[0];

            // Move through the CharacterController if present: writing transform.position directly
            // while it is enabled is silently overridden.
            var cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
                transform.SetPositionAndRotation(first.Position, first.Rotation);
                cc.enabled = true;
            }
            else
            {
                transform.SetPositionAndRotation(first.Position, first.Rotation);
            }
        }

        private void ReleaseTimestep()
        {
            if (_forceRecordedTimestep) Time.captureDeltaTime = 0f; // back to real time
        }

        #endregion
    }
}
