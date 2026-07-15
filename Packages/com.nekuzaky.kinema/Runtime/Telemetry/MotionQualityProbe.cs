using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Measures locomotion quality instead of eyeballing it.
    ///
    /// The headline metric is <b>foot sliding</b>: while the baked contacts flag a foot as grounded,
    /// that foot must not travel in world space. Whatever distance it does travel is slide - the
    /// single number animation teams use to judge whether locomotion reads as real. Kinema can
    /// measure it because the contacts are baked, so "should this foot be planted right now" is
    /// data, not a guess.
    ///
    /// Also tracks jump rate (clip flicker) and average matching cost (how well the database covers
    /// what the player is asking for).
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Motion Quality Probe")]
    [RequireComponent(typeof(MotionMatchingController))]
    public sealed class MotionQualityProbe : MonoBehaviour
    {
        #region Public

        [Tooltip("Ignore slide spikes above this per-frame distance (meters): those are transitions, not sliding.")]
        [SerializeField, Min(0.01f)] private float _spikeRejectDistance = 0.15f;

        /// <summary>Mean sliding speed of planted feet, in metres per grounded-second. Lower is better; under ~0.05 reads as planted.</summary>
        public float FootSlideRate => _groundedSeconds > 1e-4f ? _slideMetres / _groundedSeconds : 0f;

        /// <summary>Worst instantaneous sliding speed observed (m/s).</summary>
        public float PeakFootSlideRate { get; private set; }

        /// <summary>Total distance planted feet have travelled (meters) - raw slide budget.</summary>
        public float SlideMetres => _slideMetres;

        /// <summary>Seconds of foot-ground contact sampled so far.</summary>
        public float GroundedSeconds => _groundedSeconds;

        /// <summary>Clip switches per second. High values mean flicker; raise the clip-change cost.</summary>
        public float JumpsPerSecond => _elapsed > 1e-4f ? _jumps / _elapsed : 0f;

        /// <summary>Mean matching cost. Rising cost means the database does not cover the requested motion.</summary>
        public float AverageCost => _searches > 0 ? _costSum / _searches : 0f;

        public float PeakCost { get; private set; }

        #endregion

        #region Private and Protected

        private MotionMatchingController _controller;
        private Transform[] _contactBones;
        private Vector3[] _previousPositions;
        private bool[] _wasGrounded;
        private bool _resolved;

        private float _slideMetres, _groundedSeconds, _elapsed, _costSum;
        private int _jumps, _searches, _lastSearchCount;

        #endregion

        #region Unity API

        private void Awake() => _controller = GetComponent<MotionMatchingController>();

        private void LateUpdate()
        {
            if (!_controller.IsInitialized) return;

            MotionMatchingDatabase db = _controller.Database;
            if (db == null || !db.HasContacts) return;
            if (!_resolved) Resolve(db);

            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _elapsed += dt;

            SampleSearchMetrics();
            SampleFootSlide(db, dt);
        }

        #endregion

        #region Main API

        public void ResetMetrics()
        {
            _slideMetres = _groundedSeconds = _elapsed = _costSum = 0f;
            _jumps = _searches = 0;
            PeakFootSlideRate = PeakCost = 0f;
            _lastSearchCount = _controller != null && _controller.LastDebug != null ? _controller.LastDebug.SearchCount : 0;
            for (int i = 0; i < (_wasGrounded?.Length ?? 0); i++) _wasGrounded[i] = false;
        }

        #endregion

        #region Tools and Utilities

        private void Resolve(MotionMatchingDatabase db)
        {
            _resolved = true;
            int count = db.ContactBoneCount;
            _contactBones = new Transform[count];
            _previousPositions = new Vector3[count];
            _wasGrounded = new bool[count];

            for (int i = 0; i < count; i++)
                _contactBones[i] = FindDeep(transform, db.GetContactBoneName(i));
        }

        /// <summary>Costs and jumps are per-search, so only accumulate when a new search happened.</summary>
        private void SampleSearchMetrics()
        {
            MotionMatchingDebugData debug = _controller.LastDebug;
            if (debug == null || !debug.HasData || debug.SearchCount == _lastSearchCount) return;

            _lastSearchCount = debug.SearchCount;
            _searches++;
            _costSum += debug.TotalCost;
            if (debug.TotalCost > PeakCost) PeakCost = debug.TotalCost;
            if (debug.DidJump) _jumps++;
        }

        private void SampleFootSlide(MotionMatchingDatabase db, float dt)
        {
            byte contacts = db.GetContacts(_controller.CurrentFrame);

            for (int i = 0; i < _contactBones.Length; i++)
            {
                Transform bone = _contactBones[i];
                if (bone == null) continue;

                bool grounded = (contacts & (1 << i)) != 0;
                Vector3 position = bone.position;

                if (grounded && _wasGrounded[i])
                {
                    // A planted foot should be world-static: everything it moves is slide.
                    float distance = Vector3.Distance(position, _previousPositions[i]);
                    if (distance <= _spikeRejectDistance) // discard transition pops
                    {
                        _slideMetres += distance;
                        _groundedSeconds += dt;
                        float rate = distance / dt;
                        if (rate > PeakFootSlideRate) PeakFootSlideRate = rate;
                    }
                }

                _previousPositions[i] = position;
                _wasGrounded[i] = grounded;
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        #endregion
    }
}
