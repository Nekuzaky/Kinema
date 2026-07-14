using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Drives a character with motion matching. Every <see cref="_searchInterval"/> seconds it
    /// builds a query from the desired trajectory (locomotion intent) and the current pose, searches
    /// the database for the best matching frame, and crossfades to it through a two-slot
    /// <see cref="PlayableGraph"/> whose clock this component owns.
    ///
    /// The component is deliberately input-agnostic: it reads intent from an
    /// <see cref="ILocomotionProvider"/> if one is present, otherwise from <see cref="DesiredVelocity"/>.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Motion Matching Controller")]
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class MotionMatchingController : MonoBehaviour
    {
        #region Public

        [Header("Data")]
        [SerializeField] private MotionMatchingDatabase _database;
        [SerializeField] private Animator _animator;

        [Header("Matching")]
        [Tooltip("Per-group weights. Initialized from the database default; tweak live to feel the effect.")]
        [SerializeField] private FeatureWeights _weights = FeatureWeights.Default;

        [Tooltip("Seconds between database searches. 0.1 (10 Hz) is a good default.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _searchInterval = 0.1f;

        [Tooltip("A candidate must beat continuing the current clip by this fraction to trigger a jump.")]
        [SerializeField, Range(0f, 0.5f)] private float _jumpImprovementThreshold = 0.02f;

        [Tooltip("Candidates within this many seconds of the current frame (same clip) count as 'keep playing'.")]
        [SerializeField, Range(0f, 0.3f)] private float _continuityWindow = 0.12f;

        [Header("Playback")]
        [Tooltip("Crossfade duration when jumping to a new frame.")]
        [SerializeField, Range(0f, 0.4f)] private float _blendTime = 0.15f;

        [SerializeField, Range(0.1f, 2f)] private float _playbackSpeed = 1f;

        [Header("Trajectory Prediction")]
        [SerializeField] private TrajectoryPredictionSettings _prediction = TrajectoryPredictionSettings.Default;

        [Header("Manual Intent (used when no ILocomotionProvider is present)")]
        [SerializeField] private Vector3 _desiredVelocity;

        [Header("Debug")]
        [SerializeField] private bool _drawGizmos = true;
        [SerializeField] private Color _desiredTrajectoryColor = new Color(0.2f, 0.8f, 1f);
        [SerializeField] private Color _candidateTrajectoryColor = new Color(1f, 0.7f, 0.1f);

        public MotionMatchingDatabase Database => _database;
        public bool IsInitialized => _initialized;
        public MotionMatchingDebugData LastDebug => _debug;

        public int CurrentFrame => _initialized ? MapCurrentFrame() : -1;
        public int CurrentClipIndex => _initialized ? _slotClipIndex[_activeSlot] : -1;
        public float CurrentClipTime => _initialized ? (float)_slotTime[_activeSlot] : 0f;

        public FeatureWeights Weights
        {
            get => _weights;
            set
            {
                _weights = value;
                _matcher?.UpdateWeights(_weights);
            }
        }

        /// <summary>World-space desired horizontal velocity, used when no provider is attached.</summary>
        public Vector3 DesiredVelocity
        {
            get => _locomotion != null ? _locomotion.DesiredVelocity : _desiredVelocity;
            set => _desiredVelocity = value;
        }

        #endregion

        #region Private and Protected

        private const int SlotCount = 2;

        private ILocomotionProvider _locomotion;
        private MotionMatcher _matcher;
        private MotionMatchingQuery _query;
        private readonly MotionMatchingDebugData _debug = new MotionMatchingDebugData();

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly AnimationClipPlayable[] _slots = new AnimationClipPlayable[SlotCount];
        private readonly int[] _slotClipIndex = new int[SlotCount];
        private readonly double[] _slotTime = new double[SlotCount];
        private int _activeSlot;

        private bool _blending;
        private float _blend01;

        private float _searchTimer;
        private bool _initialized;

        private Vector3 _previousPosition;
        private Vector3 _measuredVelocity;
        private TrajectorySample[] _desiredTrajectory;
        private TrajectorySample[] _candidateTrajectory;

        #endregion

        #region Unity API

        private void Reset()
        {
            _animator = GetComponent<Animator>();
        }

        private void Awake()
        {
            if (_animator == null) _animator = GetComponent<Animator>();
            _locomotion = GetComponent<ILocomotionProvider>();
        }

        private void OnEnable()
        {
            Initialize();
        }

        private void OnDisable()
        {
            Teardown();
        }

        private void Update()
        {
            if (!_initialized) return;
            Tick(Time.deltaTime);
        }

        private void OnDrawGizmos()
        {
            if (!_drawGizmos || !_initialized || !_debug.HasData) return;
            DrawDebugGizmos();
        }

        #endregion

        #region Main API

        /// <summary>Builds the runtime state and the playable graph. Safe to call again after a rebake.</summary>
        public void Initialize()
        {
            if (_initialized) Teardown();
            if (!Application.isPlaying) return;

            if (_database == null || !_database.IsValid)
            {
                Debug.LogWarning($"[MotionMatching] '{name}' has no valid database assigned.", this);
                return;
            }
            if (_animator == null)
            {
                Debug.LogWarning($"[MotionMatching] '{name}' requires an Animator.", this);
                return;
            }

            _matcher = new MotionMatcher(_database, _weights);
            _query = new MotionMatchingQuery(_database.Schema);
            _desiredTrajectory = new TrajectorySample[_database.Schema.TrajectoryPointCount];
            _candidateTrajectory = new TrajectorySample[_database.Schema.TrajectoryPointCount];
            _debug.DesiredTrajectory = _desiredTrajectory;
            _debug.CandidateTrajectory = _candidateTrajectory;
            _debug.Clear();

            CreateGraph();

            _previousPosition = transform.position;
            _searchTimer = 0f;
            _initialized = true;
        }

        /// <summary>Re-applies the database's default weights onto this controller.</summary>
        public void ResetWeightsToDatabaseDefault()
        {
            if (_database != null) Weights = _database.DefaultWeights;
        }

        public void SetLocomotionProvider(ILocomotionProvider provider)
        {
            _locomotion = provider;
        }

        #endregion

        #region Tools and Utilities

        private void Tick(float dt)
        {
            // Measure how fast the root is actually moving; feeds the trajectory prediction.
            if (dt > 0f)
                _measuredVelocity = (transform.position - _previousPosition) / dt;
            _previousPosition = transform.position;

            AdvanceClocks(dt);

            _searchTimer -= dt;
            if (_searchTimer <= 0f)
            {
                _searchTimer += _searchInterval;
                RunSearch();
            }

            UpdateBlend(dt);
            ApplySlotTimes();
            _graph.Evaluate(dt);
        }

        private void AdvanceClocks(float dt)
        {
            float step = dt * _playbackSpeed;
            _slotTime[_activeSlot] = WrapTime(_slotClipIndex[_activeSlot], _slotTime[_activeSlot] + step);
            if (_blending)
            {
                int other = 1 - _activeSlot;
                _slotTime[other] = WrapTime(_slotClipIndex[other], _slotTime[other] + step);
            }
        }

        private void RunSearch()
        {
            CharacterSpace space = CharacterSpace.FromTransform(transform);

            Vector3 desiredVelocity = _locomotion != null ? _locomotion.DesiredVelocity : _desiredVelocity;
            Vector3 desiredFacing = _locomotion != null ? _locomotion.DesiredFacing : Vector3.zero;

            TrajectoryPredictor.Predict(
                space, _measuredVelocity, desiredVelocity, desiredFacing,
                _database.Schema.TrajectoryTimes, _prediction, _desiredTrajectory);

            int currentFrame = MapCurrentFrame();
            _query.SetTrajectory(_database, _desiredTrajectory);
            _query.SetPoseFromFrame(_database, currentFrame);

            MotionMatchResult result = _matcher.Search(_query);

            // Cost of simply continuing the current clip a little further.
            int continuationFrame = ContinuationFrame(currentFrame);
            float continuationCost = continuationFrame >= 0
                ? _matcher.EvaluateCost(_query, continuationFrame)
                : float.MaxValue;

            bool jumped = false;
            if (result.IsValid && ShouldJump(result, currentFrame, continuationCost))
            {
                StartTransitionTo(result.FrameIndex);
                jumped = true;
            }

            UpdateDebug(result, continuationCost, jumped);
        }

        private bool ShouldJump(MotionMatchResult result, int currentFrame, float continuationCost)
        {
            MotionFrameInfo candidate = _database.GetFrame(result.FrameIndex);

            // Candidate is essentially "where we already are" -> keep playing, no visible cut.
            if (candidate.ClipIndex == _slotClipIndex[_activeSlot])
            {
                float dt = Mathf.Abs(candidate.Time - (float)_slotTime[_activeSlot]);
                if (dt <= _continuityWindow) return false;
            }

            if (continuationCost >= float.MaxValue) return true; // current clip has nowhere to continue.
            return result.TotalCost <= continuationCost * (1f - _jumpImprovementThreshold);
        }

        private void StartTransitionTo(int frameIndex)
        {
            MotionFrameInfo frame = _database.GetFrame(frameIndex);
            MotionClipEntry clip = _database.GetClip(frame.ClipIndex);

            int newSlot = 1 - _activeSlot;
            SetSlotClip(newSlot, clip.Clip, frame.ClipIndex, frame.Time);

            _activeSlot = newSlot;
            _blending = _blendTime > 0f;
            // Resume the blend from the incoming slot's current weight to avoid a pop on interruption.
            _blend01 = _blending ? _mixer.GetInputWeight(newSlot) : 1f;
            if (!_blending)
            {
                _mixer.SetInputWeight(_activeSlot, 1f);
                _mixer.SetInputWeight(1 - _activeSlot, 0f);
            }
        }

        private void UpdateBlend(float dt)
        {
            if (!_blending)
            {
                _mixer.SetInputWeight(_activeSlot, 1f);
                _mixer.SetInputWeight(1 - _activeSlot, 0f);
                return;
            }

            _blend01 += _blendTime > 0f ? dt / _blendTime : 1f;
            float w = Mathf.Clamp01(_blend01);
            _mixer.SetInputWeight(_activeSlot, w);
            _mixer.SetInputWeight(1 - _activeSlot, 1f - w);

            if (_blend01 >= 1f) _blending = false;
        }

        private void ApplySlotTimes()
        {
            _slots[_activeSlot].SetTime(_slotTime[_activeSlot]);
            if (_blending)
            {
                int other = 1 - _activeSlot;
                if (_slots[other].IsValid())
                    _slots[other].SetTime(_slotTime[other]);
            }
        }

        private void CreateGraph()
        {
            _graph = PlayableGraph.Create($"MotionMatching ({name})");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

            var output = AnimationPlayableOutput.Create(_graph, "MM Output", _animator);
            _mixer = AnimationMixerPlayable.Create(_graph, SlotCount);
            output.SetSourcePlayable(_mixer);

            MotionClipEntry first = _database.GetClip(0);
            SetSlotClip(0, first.Clip, 0, 0f);
            SetSlotClip(1, first.Clip, 0, 0f);
            _mixer.SetInputWeight(0, 1f);
            _mixer.SetInputWeight(1, 0f);
            _activeSlot = 0;
            _blending = false;
        }

        private void SetSlotClip(int slot, AnimationClip clip, int clipIndex, float time)
        {
            if (_slots[slot].IsValid())
            {
                _graph.Disconnect(_mixer, slot);
                _graph.DestroyPlayable(_slots[slot]);
            }

            var playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetSpeed(0d); // We own the clock; the playable never auto-advances.
            playable.SetTime(time);
            playable.SetTime(time); // Second set primes the pose immediately (avoids a one-frame lag).

            _graph.Connect(playable, 0, _mixer, slot);
            _slots[slot] = playable;
            _slotClipIndex[slot] = clipIndex;
            _slotTime[slot] = time;
        }

        private void Teardown()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _initialized = false;
        }

        private double WrapTime(int clipIndex, double time)
        {
            float length = _database.GetClip(clipIndex).Length;
            if (length <= 0f) return 0d;
            if (time >= length) time -= length * System.Math.Floor(time / length);
            else if (time < 0d) time += length * (System.Math.Floor(-time / length) + 1d);
            return time;
        }

        private int MapCurrentFrame()
        {
            return _database.MapClipTimeToFrame(_slotClipIndex[_activeSlot], (float)_slotTime[_activeSlot]);
        }

        /// <summary>The frame one search-interval ahead in the current clip, or -1 if it runs off the end.</summary>
        private int ContinuationFrame(int currentFrame)
        {
            MotionClipEntry clip = _database.GetClipOfFrame(currentFrame);
            if (clip.IsLooping) return currentFrame; // looping clips can always continue.

            float aheadTime = (float)_slotTime[_activeSlot] + _searchInterval * _playbackSpeed;
            if (aheadTime >= clip.Length) return -1;
            return _database.MapClipTimeToFrame(_slotClipIndex[_activeSlot], aheadTime);
        }

        private void UpdateDebug(MotionMatchResult result, float continuationCost, bool jumped)
        {
            if (!result.IsValid) return;

            MotionFrameInfo frame = _database.GetFrame(result.FrameIndex);
            MotionClipEntry clip = _database.GetClip(frame.ClipIndex);

            _debug.HasData = true;
            _debug.SelectedFrame = result.FrameIndex;
            _debug.SelectedClipIndex = frame.ClipIndex;
            _debug.SelectedClipName = clip.Name;
            _debug.SelectedTime = frame.Time;
            _debug.TotalCost = result.TotalCost;
            _debug.TrajectoryCost = result.TrajectoryCost;
            _debug.PoseCost = result.PoseCost;
            _debug.ContinuationCost = continuationCost >= float.MaxValue ? -1f : continuationCost;
            _debug.CopyGroupCosts(result.GroupCosts);
            _debug.DidJump = jumped;
            _debug.SearchCount++;

            _database.GetTrajectory(result.FrameIndex, _candidateTrajectory);
        }

        private void DrawDebugGizmos()
        {
            CharacterSpace space = CharacterSpace.FromTransform(transform);
            DrawTrajectory(space, _debug.DesiredTrajectory, _desiredTrajectoryColor);
            DrawTrajectory(space, _debug.CandidateTrajectory, _candidateTrajectoryColor);
        }

        private static void DrawTrajectory(CharacterSpace space, TrajectorySample[] samples, Color color)
        {
            if (samples == null) return;
            Gizmos.color = color;
            Vector3 previous = space.Origin;
            for (int i = 0; i < samples.Length; i++)
            {
                Vector3 point = space.ToWorldPoint(samples[i].Position);
                Gizmos.DrawLine(previous, point);
                Gizmos.DrawSphere(point, 0.04f);
                Vector3 dir = space.ToWorldDirection(samples[i].Direction);
                Gizmos.DrawLine(point, point + dir * 0.25f);
                previous = point;
            }
        }

        #endregion
    }
}
