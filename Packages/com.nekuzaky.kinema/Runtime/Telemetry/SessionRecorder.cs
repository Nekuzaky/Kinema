using System.Collections.Generic;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Records a play session's locomotion intent frame by frame. The buffer lives in memory; the
    /// editor window saves it as a <see cref="SessionRecording"/> asset. Replaying that asset feeds
    /// identical intent back through the controller, which is what makes tuning changes measurable
    /// instead of anecdotal.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Session Recorder")]
    [RequireComponent(typeof(MotionMatchingController))]
    public sealed class SessionRecorder : MonoBehaviour
    {
        #region Public

        [Tooltip("Begin recording as soon as the scene plays.")]
        [SerializeField] private bool _recordOnStart;

        [Tooltip("Safety cap on buffered frames (36000 = ~10 minutes at 60 fps).")]
        [SerializeField, Min(60)] private int _maxFrames = 36000;

        public bool IsRecording { get; private set; }
        public int RecordedFrameCount => _buffer.Count;
        public float RecordedDuration { get; private set; }
        public IReadOnlyList<SessionFrame> Buffer => _buffer;

        #endregion

        #region Private and Protected

        private readonly List<SessionFrame> _buffer = new List<SessionFrame>();
        private MotionMatchingController _controller;
        private ILocomotionProvider _provider;

        #endregion

        #region Unity API

        private void Awake()
        {
            _controller = GetComponent<MotionMatchingController>();
            _provider = GetComponent<ILocomotionProvider>();
        }

        private void Start()
        {
            if (_recordOnStart) StartRecording();
        }

        // LateUpdate: the controller has already ticked, so we capture intent alongside its outcome.
        private void LateUpdate()
        {
            if (!IsRecording || _buffer.Count >= _maxFrames) return;

            MotionMatchingDebugData debug = _controller.LastDebug;
            _buffer.Add(new SessionFrame
            {
                DeltaTime = Time.deltaTime,
                DesiredVelocity = _controller.DesiredVelocity,
                DesiredFacing = _provider != null ? _provider.DesiredFacing : Vector3.zero,
                Position = transform.position,
                Rotation = transform.rotation,
                SelectedFrame = _controller.CurrentFrame,
                Cost = debug != null && debug.HasData ? debug.TotalCost : 0f,
                Jumped = debug != null && debug.DidJump
            });
            RecordedDuration += Time.deltaTime;
        }

        #endregion

        #region Main API

        public void StartRecording()
        {
            _buffer.Clear();
            RecordedDuration = 0f;
            IsRecording = true;
        }

        public void StopRecording() => IsRecording = false;

        /// <summary>Copies the buffer into a recording asset (editor-side saving is the caller's job).</summary>
        public void WriteTo(SessionRecording recording, string recordedUtc)
        {
            if (recording == null) return;
            MotionMatchingDatabase db = _controller.Database;
            recording.SetRecording(
                _buffer.ToArray(),
                db != null ? db.name : "<none>",
                db != null ? db.FrameCount : 0,
                recordedUtc);
        }

        #endregion
    }
}
