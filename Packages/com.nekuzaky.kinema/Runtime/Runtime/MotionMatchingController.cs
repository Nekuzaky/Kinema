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

        [Tooltip("Optional extra databases (stances, states). Switch at runtime with SwitchDatabase.")]
        [SerializeField] private MotionMatchingDatabase[] _additionalDatabases = System.Array.Empty<MotionMatchingDatabase>();

        [SerializeField] private Animator _animator;

        [Tooltip("Search strategy. BurstLinear is the right default; KdTree targets very large databases.")]
        [SerializeField] private SearchAcceleration _searchAcceleration = SearchAcceleration.BurstLinear;

        [Header("Matching")]
        [Tooltip("Per-group weights. Initialized from the database default; tweak live to feel the effect.")]
        [SerializeField] private FeatureWeights _weights = FeatureWeights.Default;

        [Tooltip("Seconds between database searches. 0.1 (10 Hz) is a good default.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _searchInterval = 0.1f;

        [Tooltip("A candidate must beat continuing the current clip by this fraction to trigger a jump.")]
        [SerializeField, Range(0f, 0.5f)] private float _jumpImprovementThreshold = 0.02f;

        [Tooltip("Candidates within this many seconds of the current frame (same clip) count as 'keep playing'.")]
        [SerializeField, Range(0f, 0.3f)] private float _continuityWindow = 0.12f;

        [Tooltip("Extra cost added to candidates from a different clip. Raise to reduce clip flicker; lower for snappier switching.")]
        [SerializeField, Range(0f, 1f)] private float _clipChangeCost = 0.1f;

        public enum TransitionMode
        {
            /// <summary>Two-slot mixer crossfade. Simple, always safe.</summary>
            Crossfade,
            /// <summary>Hard switch + decaying pose offset (AAA standard). Velocity-continuous transitions.</summary>
            Inertialization
        }

        [Header("Playback")]
        [Tooltip("How jumps are smoothed. Inertialization = hard switch + decaying pose offset (AAA standard).")]
        [SerializeField] private TransitionMode _transitionMode = TransitionMode.Inertialization;

        [Tooltip("Crossfade duration when jumping to a new frame.")]
        [SerializeField, Range(0f, 0.4f)] private float _blendTime = 0.15f;

        [SerializeField, Range(0.1f, 2f)] private float _playbackSpeed = 1f;

        [Header("Stride Warping")]
        [Tooltip("Scale clip playback so the baked stride delivers exactly the requested speed. A database with a 1.4 m/s walk and a 4 m/s run can then hit any speed between them, and the legs cycle in sync with the travel instead of sliding.")]
        [SerializeField] private bool _strideWarping = true;

        [Tooltip("How far playback may be scaled (min, max). Beyond ~±30% the motion starts reading as slow-motion or fast-forward.")]
        [SerializeField] private Vector2 _strideWarpRange = new Vector2(0.75f, 1.3f);

        [Tooltip("Below this speed (m/s) warping is meaningless - idle and near-idle play at their authored rate.")]
        [SerializeField, Range(0.05f, 1f)] private float _strideWarpMinSpeed = 0.25f;

        [Tooltip("How quickly the warp factor follows a speed change. Higher = snappier, lower = smoother.")]
        [SerializeField, Range(1f, 30f)] private float _strideWarpSharpness = 10f;

        [Header("Trajectory Prediction")]
        [SerializeField] private TrajectoryPredictionSettings _prediction = TrajectoryPredictionSettings.Default;

        [Header("Pose Query")]
        [Tooltip("Build the query's pose half from the skeleton actually rendered (after inertialization, stride warp and IK) instead of copying the current database frame. Honest pose costs; falls back to the frame copy when the schema's bones are missing on the rig.")]
        [SerializeField] private bool _livePoseQuery = true;

        [Header("Events")]
        [Tooltip("Relay AnimationEvents of the active clip via SendMessage, matching Mecanim semantics.")]
        [SerializeField] private bool _relayAnimationEvents = true;

        [Header("Manual Intent (used when no ILocomotionProvider is present)")]
        [SerializeField] private Vector3 _desiredVelocity;

        [Header("Debug")]
        [SerializeField] private bool _drawGizmos = true;
        [SerializeField] private Color _desiredTrajectoryColor = new Color(0.2f, 0.8f, 1f);
        [SerializeField] private Color _candidateTrajectoryColor = new Color(1f, 0.7f, 0.1f);
        [SerializeField] private Color _boneColor = new Color(0.4f, 1f, 0.5f);

        public MotionMatchingDatabase Database => _database;
        public bool IsInitialized => _initialized;
        public MotionMatchingDebugData LastDebug => _debug;

        public int CurrentFrame => _initialized ? MapCurrentFrame() : -1;

        /// <summary>Contact bitmask of the frame currently playing (bit b = contact bone b grounded).</summary>
        public byte CurrentContacts => _initialized ? _database.GetContacts(MapCurrentFrame()) : (byte)0;

        /// <summary>Candidate frames must carry ALL these tag bits. 0 = no requirement.</summary>
        public ulong RequiredTags { get; set; }

        /// <summary>Frames carrying ANY of these tag bits are skipped. 0 = nothing excluded.</summary>
        public ulong ExcludedTags { get; set; }

        /// <summary>Convenience: resolves a tag name against the database and adds it to the required mask.</summary>
        public void RequireTag(string tagName, bool required = true)
        {
            if (_database == null) return;
            ulong mask = _database.GetTagMask(tagName);
            if (required) RequiredTags |= mask; else RequiredTags &= ~mask;
        }

        /// <summary>True while a triggered event clip is playing (matching suspended).</summary>
        public bool IsPlayingEvent => _activeEvent != null;

        /// <summary>True while a clip is being force-played by <see cref="PlayClipOverride"/>.</summary>
        public bool IsOverridingClip => _clipOverride;

        /// <summary>Freezes the override clock so an editor timeline can scrub. Only read while overriding.</summary>
        public bool OverridePaused { get; set; }

        /// <summary>Ring buffer of the last matching decisions, for the snapshot debugger.</summary>
        public SearchSnapshotRecorder Snapshots => _snapshots;

        /// <summary>True while a recorded snapshot is being previewed (live ticking is paused).</summary>
        public bool IsPreviewing => _previewing;

        /// <summary>
        /// Selection count per database frame, accumulated since the last <see cref="ResetTelemetry"/>.
        /// Feed it to <see cref="CoverageReport"/> to see which data the matcher actually uses.
        /// </summary>
        public int[] FrameUsage => _frameUsage;

        public int TotalSearches => _totalSearches;
        public int TotalJumps => _totalJumps;

        /// <summary>Current clip-playback scale applied by stride warping (1 = authored rate).</summary>
        public float CurrentStrideWarp => _currentStrideWarp;
        public float AverageCost => _totalSearches > 0 ? _costSum / _totalSearches : 0f;

        /// <summary>Clears coverage counts and cost accumulators.</summary>
        public void ResetTelemetry()
        {
            if (_frameUsage != null) System.Array.Clear(_frameUsage, 0, _frameUsage.Length);
            _totalSearches = 0;
            _totalJumps = 0;
            _costSum = 0f;
        }

        /// <summary>Applies a named weight preset baked into the database. Returns false when unknown.</summary>
        public bool SetCalibrationProfile(string profileName)
        {
            CalibrationProfile profile = _database != null ? _database.FindProfile(profileName) : null;
            if (profile == null) return false;
            Weights = profile.Weights;
            return true;
        }

        /// <summary>
        /// Switches to another database (stance/state change). Rebuilds the runtime; the next search
        /// picks the best frame in the new set and the inertializer absorbs the transition.
        /// </summary>
        public bool SwitchDatabase(MotionMatchingDatabase database)
        {
            if (database == null || !database.IsValid) return false;
            _database = database;
            Initialize();
            return true;
        }

        /// <summary>Switch to a database from the additional list by index.</summary>
        public bool SwitchDatabase(int additionalIndex)
        {
            if (additionalIndex < 0 || additionalIndex >= _additionalDatabases.Length) return false;
            return SwitchDatabase(_additionalDatabases[additionalIndex]);
        }

        /// <summary>
        /// Fades matching in or out against the Animator's own controller (Mecanim interop): at
        /// weight 0 the AnimatorController underneath drives the character (cinematics, scripted
        /// states), at 1 motion matching does. The graph keeps running either way.
        /// </summary>
        public void SetMatchingActive(bool active, float fadeTime = 0.2f)
        {
            _outputTarget = active ? 1f : 0f;
            _outputFade = Mathf.Max(0.01f, fadeTime);
        }
        public int CurrentClipIndex => _initialized ? _slotClipIndex[_activeSlot] : -1;
        public float CurrentClipTime => _initialized ? LocalClipTime(_slotClipIndex[_activeSlot], _slotTime[_activeSlot]) : 0f;

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
        private AnimationLayerMixerPlayable _layerMixer;
        private AnimationClipPlayable _overlayPlayable;
        private float _overlayWeight, _overlayTarget, _overlayFade;
        private PoseInertializer _inertializer;
        private MirrorPose _mirror;
        private bool _playingMirrored;

        // Event playback state (matching suspended while active).
        private MotionEventDefinition _activeEvent;
        private Vector3 _eventTargetPosition;
        private Quaternion _eventTargetRotation;
        private float _eventClipLength;

        private SearchSnapshotRecorder _snapshots;

        // Coverage telemetry: which frames the matcher actually reaches for.
        private int[] _frameUsage;
        private int _totalSearches, _totalJumps;
        private float _costSum;
        private float _currentStrideWarp = 1f;

        // Live state saved while previewing a snapshot, restored by StopPreview.
        private bool _previewing;
        private bool _clipOverride;
        private Vector3 _liveCharacterPosition;
        private Quaternion _liveCharacterRotation;
        private int _liveActiveSlot;
        private int _liveSlot0ClipIndex; private double _liveSlot0Time;
        private int _liveSlot1ClipIndex; private double _liveSlot1Time;
        private float _liveBlend01;
        private bool _liveMirrored;

        private AnimationPlayableOutput _output;
        private float _outputWeight = 1f, _outputTarget = 1f, _outputFade = 0.2f;
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

        private TrajectoryHistory _history;
        private int _lastCurrentFrame = -1;
        private Vector3[] _currentBones;

        // Live pose sampling: the schema's bones resolved on this rig, plus last-frame world
        // positions for finite-difference velocities.
        private Transform[] _queryBones;
        private Vector3[] _queryBoneWorldPositions;
        private Vector3[] _queryBonePreviousPositions;
        private Vector3[] _queryBoneVelocities;
        private bool _queryBonesValid;
        private bool _queryBonesPrimed;
        private Vector3[] _candidateBones;

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
            if (!_initialized || _previewing) return;
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

            _matcher = new MotionMatcher(_database, _weights) { Acceleration = _searchAcceleration };
            _query = new MotionMatchingQuery(_database.Schema);
            _desiredTrajectory = new TrajectorySample[_database.Schema.TrajectoryPointCount];
            _candidateTrajectory = new TrajectorySample[_database.Schema.TrajectoryPointCount];
            int boneCount = _database.Schema.BoneCount;
            _currentBones = new Vector3[boneCount];
            _candidateBones = new Vector3[boneCount];
            ResolveQueryBones();
            _history = new TrajectoryHistory(128);
            _snapshots = new SearchSnapshotRecorder(240, FeatureGroupExtensions.Count, _database.Schema.TrajectoryPointCount);
            _frameUsage = new int[_database.FrameCount];
            _totalSearches = 0; _totalJumps = 0; _costSum = 0f;
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

        /// <summary>
        /// Re-syncs runtime state after serialized fields were edited externally (editor window,
        /// inspector during play): rebuilds the weight table and propagates the acceleration mode.
        /// </summary>
        public void NotifySerializedFieldsChanged()
        {
            if (_matcher == null) return;
            _matcher.UpdateWeights(_weights);
            _matcher.Acceleration = _searchAcceleration;
        }

        /// <summary>
        /// Plays a triggered action clip, warping the root so the event's contact moment lands on
        /// the given target. Matching is suspended until the clip finishes, then resumes with an
        /// immediate search (the inertializer absorbs the seam).
        /// </summary>
        public bool PlayEvent(MotionEventDefinition definition, Vector3 targetPosition, Quaternion targetRotation)
        {
            if (!_initialized || definition == null || !definition.IsValid) return false;

            _activeEvent = definition;
            _eventTargetPosition = targetPosition;
            _eventTargetRotation = targetRotation;
            _eventClipLength = definition.Clip.length;

            int newSlot = 1 - _activeSlot;
            SetSlotClip(newSlot, definition.Clip, -1, 0f); // -1: external clip, not a database index.
            _activeSlot = newSlot;
            _blending = false;
            _mixer.SetInputWeight(_activeSlot, 1f);
            _mixer.SetInputWeight(1 - _activeSlot, 0f);
            _inertializer?.RequestTransition(definition.BlendIn);
            _mirror?.SetMirrored(false);
            _playingMirrored = false;
            return true;
        }

        /// <summary>
        /// Plays an overlay clip on a separate layer (upper body via avatar mask, for example)
        /// while matching keeps driving the base layer.
        /// </summary>
        public void PlayOverlay(AnimationClip clip, AvatarMask mask, float weight = 1f, float fadeTime = 0.15f)
        {
            if (!_initialized || clip == null || !_layerMixer.IsValid()) return;

            if (_overlayPlayable.IsValid())
            {
                _graph.Disconnect(_layerMixer, 1);
                _graph.DestroyPlayable(_overlayPlayable);
            }

            _overlayPlayable = AnimationClipPlayable.Create(_graph, clip);
            _overlayPlayable.SetApplyFootIK(false);
            _graph.Connect(_overlayPlayable, 0, _layerMixer, 1);
            if (mask != null) _layerMixer.SetLayerMaskFromAvatarMask(1, mask);

            _overlayTarget = Mathf.Clamp01(weight);
            _overlayFade = Mathf.Max(0.01f, fadeTime);
        }

        /// <summary>Fades the overlay layer out.</summary>
        public void StopOverlay(float fadeTime = 0.15f)
        {
            _overlayTarget = 0f;
            _overlayFade = Mathf.Max(0.01f, fadeTime);
        }

        public void SetLocomotionProvider(ILocomotionProvider provider)
        {
            _locomotion = provider;
        }

        /// <summary>
        /// Points the controller at another Animator before it initializes - the ghost-on-a-different-rig
        /// path, where the serialized reference still names the source character's Animator.
        /// </summary>
        public void SetAnimator(Animator animator)
        {
            _animator = animator;
        }

        #endregion

        #region Tools and Utilities

        private void Tick(float dt)
        {
            // Measure how fast the root is actually moving; feeds the trajectory prediction.
            if (dt > 0f)
                _measuredVelocity = (transform.position - _previousPosition) / dt;
            _previousPosition = transform.position;
            _history.Record(Time.time, transform.position, transform.forward);

            // Sample the skeleton as rendered last frame (IK included) for the live pose query.
            if (_livePoseQuery && _queryBonesValid && dt > 0f) SampleQueryBones(dt);

            UpdateStrideWarp(dt);
            AdvanceClocks(dt);

            if (_clipOverride)
            {
                // Inspection mode: the clip owns the pose, the matcher stays out of it.
            }
            else if (_activeEvent != null)
            {
                TickEvent(dt);
            }
            else
            {
                _searchTimer -= dt;
                if (_searchTimer <= 0f)
                {
                    _searchTimer += _searchInterval;
                    RunSearch();
                }
            }

            UpdateBlend(dt);
            UpdateOverlay(dt);
            ApplySlotTimes();
            _graph.Evaluate(dt);
        }

        /// <summary>Warps the root toward the event target until contact, then ends the event at clip end.</summary>
        private void TickEvent(float dt)
        {
            float time = (float)_slotTime[_activeSlot];
            float remainingToContact = _activeEvent.ContactTime - time;

            if (remainingToContact > dt)
            {
                float step = dt / remainingToContact;
                if (_activeEvent.WarpPosition)
                {
                    Vector3 error = _eventTargetPosition - transform.position;
                    error.y = 0f;
                    transform.position += error * step;
                }
                if (_activeEvent.WarpRotation)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, _eventTargetRotation, step);
                }
            }

            if (time >= _eventClipLength - 1e-3f)
            {
                _activeEvent = null;
                _searchTimer = 0f; // resume matching immediately; inertialization absorbs the seam.
            }
        }

        private void UpdateOverlay(float dt)
        {
            // Mecanim interop: fade the whole matching output against the AnimatorController.
            if (!Mathf.Approximately(_outputWeight, _outputTarget))
            {
                _outputWeight = Mathf.MoveTowards(_outputWeight, _outputTarget, dt / _outputFade);
                _output.SetWeight(_outputWeight);
            }

            if (!_layerMixer.IsValid()) return;
            _overlayWeight = Mathf.MoveTowards(_overlayWeight, _overlayTarget, dt / _overlayFade);
            _layerMixer.SetInputWeight(1, _overlayWeight);

            // One-shot overlays fade out on their own once the clip ends.
            if (_overlayPlayable.IsValid() && _overlayTarget > 0f)
            {
                AnimationClip clip = _overlayPlayable.GetAnimationClip();
                if (clip != null && !clip.isLooping && _overlayPlayable.GetTime() >= clip.length)
                    _overlayTarget = 0f;
            }
        }

        /// <summary>
        /// Stride warping: the matcher can only pick speeds that exist in the data, so a database
        /// holding a 1.4 m/s walk and a 4 m/s run can otherwise never travel at 2.5 m/s - it snaps
        /// to one of the two and the feet slide the difference. Scaling clip playback by
        /// requested/baked speed makes the baked stride deliver exactly the requested speed, so the
        /// legs cycle in sync with the travel. This is what lets a handful of clips cover a
        /// continuous speed range.
        /// </summary>
        private void UpdateStrideWarp(float dt)
        {
            float target = 1f;

            if (_strideWarping && _activeEvent == null && !_clipOverride)
            {
                float clipSpeed = _database.GetRootVelocity(MapCurrentFrame()).magnitude;
                Vector3 desired = _locomotion != null ? _locomotion.DesiredVelocity : _desiredVelocity;
                float desiredSpeed = new Vector2(desired.x, desired.z).magnitude;

                // Only warp when both the clip and the request carry real speed; scaling an idle
                // clip toward a walk would just play idle fast.
                if (clipSpeed >= _strideWarpMinSpeed && desiredSpeed >= _strideWarpMinSpeed)
                    target = Mathf.Clamp(desiredSpeed / clipSpeed, _strideWarpRange.x, _strideWarpRange.y);
            }

            float t = 1f - Mathf.Exp(-_strideWarpSharpness * dt);
            _currentStrideWarp = Mathf.Lerp(_currentStrideWarp, target, t);
        }

        private void AdvanceClocks(float dt)
        {
            float step = dt * _playbackSpeed * _currentStrideWarp;
            if (_clipOverride && OverridePaused) step = 0f;
            double before = _slotTime[_activeSlot];
            _slotTime[_activeSlot] = WrapTime(_slotClipIndex[_activeSlot], before + step);
            if (_blending)
            {
                int other = 1 - _activeSlot;
                _slotTime[other] = WrapTime(_slotClipIndex[other], _slotTime[other] + step);
            }

            if (_relayAnimationEvents && step > 0f && _slotClipIndex[_activeSlot] >= 0)
                RelayAnimationEvents(_slotClipIndex[_activeSlot], (float)before, (float)_slotTime[_activeSlot]);
        }

        /// <summary>
        /// Fires the active clip's AnimationEvents crossed this frame via SendMessage (Mecanim
        /// semantics), handling loop wrap. The manual-clock playback bypasses Unity's own dispatch.
        /// </summary>
        private void RelayAnimationEvents(int clipIndex, float from, float to)
        {
            AnimationClip clip = _database.GetClip(clipIndex).Clip;
            if (clip == null) return;
            AnimationEvent[] events = clip.events;
            if (events == null || events.Length == 0) return;

            // The slot clock is monotonic; map to clip-local time before matching event times.
            float fromLocal = LocalClipTime(clipIndex, from);
            float toLocal = LocalClipTime(clipIndex, to);

            if (toLocal >= fromLocal)
            {
                DispatchEventsInRange(events, fromLocal, toLocal);
            }
            else // crossed the loop point this frame
            {
                DispatchEventsInRange(events, fromLocal, clip.length + 1e-4f);
                DispatchEventsInRange(events, -1e-4f, toLocal);
            }
        }

        private void DispatchEventsInRange(AnimationEvent[] events, float from, float to)
        {
            for (int i = 0; i < events.Length; i++)
            {
                AnimationEvent e = events[i];
                if (e.time > from && e.time <= to && !string.IsNullOrEmpty(e.functionName))
                    gameObject.SendMessage(e.functionName, e, SendMessageOptions.DontRequireReceiver);
            }
        }

        private void RunSearch()
        {
            CharacterSpace space = CharacterSpace.FromTransform(transform);

            Vector3 desiredVelocity = _locomotion != null ? _locomotion.DesiredVelocity : _desiredVelocity;
            Vector3 desiredFacing = _locomotion != null ? _locomotion.DesiredFacing : Vector3.zero;

            TrajectoryPredictor.Predict(
                space, _measuredVelocity, desiredVelocity, desiredFacing,
                _database.Schema.TrajectoryTimes, _prediction, _history, Time.time, _desiredTrajectory);

            int currentFrame = MapCurrentFrame();
            _lastCurrentFrame = currentFrame;
            _query.SetTrajectory(_database, _desiredTrajectory);

            // The honest pose: what is on screen, not the frame the clock is on. Needs one primed
            // sample so velocities exist; until then (and when bones are missing) copy the frame.
            if (_livePoseQuery && _queryBonesValid && _queryBonesPrimed)
                _query.SetPoseFromSkeleton(_database, space, _queryBoneWorldPositions, _queryBoneVelocities, _measuredVelocity);
            else
                _query.SetPoseFromFrame(_database, currentFrame);

            MotionMatchResult result = _matcher.Search(_query, requiredTags: RequiredTags, excludedTags: ExcludedTags);

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

        /// <summary>Finds the schema's bones on this rig by name; missing bones disable live sampling.</summary>
        private void ResolveQueryBones()
        {
            string[] names = _database.Schema.BoneNames;
            int count = names?.Length ?? 0;
            _queryBones = new Transform[count];
            _queryBoneWorldPositions = new Vector3[count];
            _queryBonePreviousPositions = new Vector3[count];
            _queryBoneVelocities = new Vector3[count];
            _queryBonesPrimed = false;
            _queryBonesValid = count > 0;

            for (int b = 0; b < count; b++)
            {
                _queryBones[b] = FindDeepChild(transform, names[b]);
                if (_queryBones[b] == null)
                {
                    _queryBonesValid = false;
                    if (_livePoseQuery)
                        Debug.LogWarning($"[MotionMatching] '{name}': bone '{names[b]}' not found on this rig; " +
                                         "the pose query falls back to copying the current database frame.", this);
                    return;
                }
            }
        }

        private void SampleQueryBones(float dt)
        {
            for (int b = 0; b < _queryBones.Length; b++)
            {
                Vector3 position = _queryBones[b].position;
                _queryBoneVelocities[b] = _queryBonesPrimed ? (position - _queryBonePreviousPositions[b]) / dt : Vector3.zero;
                _queryBonePreviousPositions[b] = position;
                _queryBoneWorldPositions[b] = position;
            }
            _queryBonesPrimed = true;
        }

        private static Transform FindDeepChild(Transform root, string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == boneName) return t;
            return null;
        }

        private bool ShouldJump(MotionMatchResult result, int currentFrame, float continuationCost)
        {
            MotionFrameInfo candidate = _database.GetFrame(result.FrameIndex);

            // Candidate is essentially "where we already are" -> keep playing, no visible cut.
            if (candidate.ClipIndex == _slotClipIndex[_activeSlot])
            {
                float local = LocalClipTime(candidate.ClipIndex, _slotTime[_activeSlot]);
                float dt = Mathf.Abs(candidate.Time - local);
                if (dt <= _continuityWindow) return false;
            }

            if (continuationCost >= float.MaxValue) return true; // current clip has nowhere to continue.

            // Penalize crossing into a different clip so the character keeps its rhythm.
            float candidateCost = result.TotalCost;
            if (candidate.ClipIndex != _slotClipIndex[_activeSlot]) candidateCost += _clipChangeCost;
            return candidateCost <= continuationCost * (1f - _jumpImprovementThreshold);
        }

        private void StartTransitionTo(int frameIndex)
        {
            MotionFrameInfo frame = _database.GetFrame(frameIndex);
            MotionClipEntry clip = _database.GetClip(frame.ClipIndex);

            _playingMirrored = frame.IsMirrored;
            _mirror?.SetMirrored(_playingMirrored);

            int newSlot = 1 - _activeSlot;
            SetSlotClip(newSlot, clip.Clip, frame.ClipIndex, frame.Time);

            _activeSlot = newSlot;

            if (_transitionMode == TransitionMode.Inertialization)
            {
                // Hard switch; the inertializer carries the discontinuity as a decaying offset.
                _blending = false;
                _mixer.SetInputWeight(_activeSlot, 1f);
                _mixer.SetInputWeight(1 - _activeSlot, 0f);
                _inertializer?.RequestTransition(_blendTime);
                return;
            }

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

        /// <summary>
        /// Force-plays one database clip and suspends matching until <see cref="StopClipOverride"/>.
        /// The clock keeps running, so the clip plays at its authored rate through the normal graph -
        /// this is inspection of the data as baked, not a matching decision. Stride warping is held
        /// at 1 for the duration so what is on screen is the clip, not a scaled version of it.
        /// </summary>
        public void PlayClipOverride(int clipIndex, float time = 0f)
        {
            if (!_initialized || _database == null) return;
            if (clipIndex < 0 || clipIndex >= _database.ClipCount) return;

            _clipOverride = true;
            _activeEvent = null;
            _blending = false;
            _blend01 = 1f;

            // Drive both slots from the same clip: no stale pose can bleed through the mixer.
            ApplySlotState(_activeSlot, clipIndex, time);
            ApplySlotState(1 - _activeSlot, clipIndex, time);
            _mixer.SetInputWeight(_activeSlot, 1f);
            _mixer.SetInputWeight(1 - _activeSlot, 0f);

            ApplySlotTimes();
        }

        /// <summary>Hands control back to the matcher; the next search runs immediately.</summary>
        public void StopClipOverride()
        {
            if (!_clipOverride) return;
            _clipOverride = false;
            OverridePaused = false;
            _searchTimer = 0f;
        }

        /// <summary>Scrubs the override clip to a local time. Timeline support; no-op unless overriding.</summary>
        public void SetClipOverrideTime(float time)
        {
            if (!_clipOverride) return;
            int clipIndex = _slotClipIndex[_activeSlot];
            if (clipIndex < 0) return;

            ApplySlotState(_activeSlot, clipIndex, time);
            ApplySlotState(1 - _activeSlot, clipIndex, time);
            ApplySlotTimes();
            _graph.Evaluate(0f);
        }

        /// <summary>
        /// Full-state rewind: replays a recorded <see cref="SearchSnapshot"/> by driving the graph
        /// with its captured transform, slot clip/time, blend and mirror state, then forcing one
        /// evaluation - the exact pose that was on screen at that decision reappears. Live ticking
        /// pauses until <see cref="StopPreview"/>; call it again with a different snapshot to scrub.
        /// </summary>
        public void PreviewSnapshot(SearchSnapshot snapshot)
        {
            if (!_initialized || snapshot == null) return;

            if (!_previewing)
            {
                _previewing = true;
                _liveCharacterPosition = transform.position;
                _liveCharacterRotation = transform.rotation;
                _liveActiveSlot = _activeSlot;
                _liveSlot0ClipIndex = _slotClipIndex[0]; _liveSlot0Time = _slotTime[0];
                _liveSlot1ClipIndex = _slotClipIndex[1]; _liveSlot1Time = _slotTime[1];
                _liveBlend01 = _blend01;
                _liveMirrored = _playingMirrored;
            }

            transform.SetPositionAndRotation(snapshot.CharacterPosition, snapshot.CharacterRotation);
            _activeSlot = snapshot.ActiveSlot;
            ApplySlotState(0, snapshot.Slot0ClipIndex, (float)snapshot.Slot0Time);
            ApplySlotState(1, snapshot.Slot1ClipIndex, (float)snapshot.Slot1Time);
            _blend01 = snapshot.Blend01;
            _blending = false; // hold the exact recorded blend weight instead of continuing to interpolate.
            _mixer.SetInputWeight(_activeSlot, Mathf.Clamp01(_blend01));
            _mixer.SetInputWeight(1 - _activeSlot, 1f - Mathf.Clamp01(_blend01));
            _playingMirrored = snapshot.Mirrored;
            _mirror?.SetMirrored(_playingMirrored);

            ApplySlotTimes();
            _graph.Evaluate(0f);
        }

        /// <summary>Restores live playback exactly where <see cref="PreviewSnapshot"/> paused it.</summary>
        public void StopPreview()
        {
            if (!_previewing) return;
            _previewing = false;

            transform.SetPositionAndRotation(_liveCharacterPosition, _liveCharacterRotation);
            _activeSlot = _liveActiveSlot;
            ApplySlotState(0, _liveSlot0ClipIndex, (float)_liveSlot0Time);
            ApplySlotState(1, _liveSlot1ClipIndex, (float)_liveSlot1Time);
            _blend01 = _liveBlend01;
            _playingMirrored = _liveMirrored;
            _mirror?.SetMirrored(_playingMirrored);

            ApplySlotTimes();
            _graph.Evaluate(0f);
        }

        /// <summary>
        /// Restores one slot to a recorded (clipIndex, time). Database clips are recreated exactly;
        /// event clips (index -1) aren't recoverable from a bare index, so the slot is only
        /// re-timed - previewing mid-event lands close but not pixel-exact.
        /// </summary>
        private void ApplySlotState(int slot, int clipIndex, float time)
        {
            if (clipIndex >= 0)
            {
                SetSlotClip(slot, _database.GetClip(clipIndex).Clip, clipIndex, time);
            }
            else if (_slots[slot].IsValid())
            {
                _slots[slot].SetTime(time);
                _slotTime[slot] = time;
            }
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

            _output = AnimationPlayableOutput.Create(_graph, "MM Output", _animator);
            var output = _output;
            _output.SetWeight(_outputWeight);
            _mixer = AnimationMixerPlayable.Create(_graph, SlotCount);

            // Chain: mixer -> [mirror] -> [inertializer] -> layerMixer -> output.
            Playable head = _mixer;

            if (_database.HasMirroredFrames)
            {
                _mirror = new MirrorPose();
                var mirrorNode = _mirror.Create(_graph, _animator);
                _graph.Connect(head, 0, mirrorNode, 0);
                mirrorNode.SetInputWeight(0, 1f);
                head = mirrorNode;
            }

            if (_transitionMode == TransitionMode.Inertialization)
            {
                _inertializer = new PoseInertializer();
                var node = _inertializer.Create(_graph, _animator);
                _graph.Connect(head, 0, node, 0);
                node.SetInputWeight(0, 1f);
                head = node;
            }

            _layerMixer = AnimationLayerMixerPlayable.Create(_graph, 2);
            _graph.Connect(head, 0, _layerMixer, 0);
            _layerMixer.SetInputWeight(0, 1f);
            _layerMixer.SetInputWeight(1, 0f);
            output.SetSourcePlayable(_layerMixer);

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
            _inertializer?.Dispose();
            _inertializer = null;
            _mirror?.Dispose();
            _mirror = null;
            _matcher?.Dispose();
            _matcher = null;
            _activeEvent = null;
            _playingMirrored = false;
            _initialized = false;
        }

        /// <summary>
        /// Advances a slot clock. Looping clips keep a MONOTONIC time: wrapping it manually would
        /// make the Animator see a length-to-zero jump and emit a negative root-motion delta (a
        /// backward teleport every loop). Unity's own loopTime handling produces continuous root
        /// motion from a growing time. Non-looping and event clips clamp at their end.
        /// </summary>
        private double WrapTime(int clipIndex, double time)
        {
            if (clipIndex < 0)
                return System.Math.Min(time, _eventClipLength); // external event clip

            MotionClipEntry clip = _database.GetClip(clipIndex);
            if (clip.Length <= 0f) return 0d;
            if (clip.IsLooping) return System.Math.Max(time, 0d);
            return System.Math.Min(time, clip.Length);
        }

        /// <summary>Clip-local time in [0, length] regardless of the monotonic slot clock.</summary>
        private float LocalClipTime(int clipIndex, double time)
        {
            if (clipIndex < 0) return (float)time;
            MotionClipEntry clip = _database.GetClip(clipIndex);
            if (clip.Length <= 0f) return 0f;
            return clip.IsLooping ? Mathf.Repeat((float)time, clip.Length) : Mathf.Min((float)time, clip.Length);
        }

        private int MapCurrentFrame()
        {
            int clipIndex = _slotClipIndex[_activeSlot];
            if (clipIndex < 0) return _lastCurrentFrame >= 0 ? _lastCurrentFrame : 0; // event clip: keep last known.

            int frame = _database.MapClipTimeToFrame(clipIndex, LocalClipTime(clipIndex, _slotTime[_activeSlot]));
            return _playingMirrored ? _database.GetMirroredTwin(frame) : frame;
        }

        /// <summary>The frame one search-interval ahead in the current clip, or -1 if it runs off the end.</summary>
        private int ContinuationFrame(int currentFrame)
        {
            MotionClipEntry clip = _database.GetClipOfFrame(currentFrame);
            if (clip.IsLooping) return currentFrame; // looping clips can always continue.

            float aheadTime = (float)_slotTime[_activeSlot] + _searchInterval * _playbackSpeed;
            if (aheadTime >= clip.Length) return -1;
            int frame = _database.MapClipTimeToFrame(_slotClipIndex[_activeSlot], aheadTime);
            return _playingMirrored ? _database.GetMirroredTwin(frame) : frame;
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

            // Coverage: count what the matcher actually picked.
            if (_frameUsage != null && result.FrameIndex >= 0 && result.FrameIndex < _frameUsage.Length)
                _frameUsage[result.FrameIndex]++;
            _totalSearches++;
            _costSum += result.TotalCost;
            if (jumped) _totalJumps++;

            _database.GetTrajectory(result.FrameIndex, _candidateTrajectory);
            _database.GetBonePositions(result.FrameIndex, _candidateBones);
            if (_lastCurrentFrame >= 0) _database.GetBonePositions(_lastCurrentFrame, _currentBones);

            _snapshots?.Record(
                Time.time, result.FrameIndex, frame.ClipIndex, frame.Time,
                result.TotalCost, _debug.ContinuationCost, jumped,
                result.GroupCosts, _desiredTrajectory, _candidateTrajectory,
                transform.position, transform.rotation, _activeSlot,
                _slotClipIndex[0], _slotTime[0], _slotClipIndex[1], _slotTime[1],
                _blend01, _playingMirrored);
        }

        private void DrawDebugGizmos()
        {
            CharacterSpace space = CharacterSpace.FromTransform(transform);
            DrawTrajectory(space, _debug.DesiredTrajectory, _desiredTrajectoryColor);
            DrawTrajectory(space, _debug.CandidateTrajectory, _candidateTrajectoryColor);
            DrawBones(space, _candidateBones, _boneColor);
        }

        private static void DrawBones(CharacterSpace space, Vector3[] localBones, Color color)
        {
            if (localBones == null) return;
            Gizmos.color = color;
            for (int i = 0; i < localBones.Length; i++)
                Gizmos.DrawWireSphere(space.ToWorldOffset3D(localBones[i]), 0.06f);
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
