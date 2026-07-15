using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Degrades a <see cref="MotionMatchingController"/>'s search cadence as its distance from a
    /// reference point (the main camera by default) grows, so a crowd of matched characters spends
    /// less Burst search budget on individuals far from the eye. Only touches
    /// <see cref="MotionMatchingController.SearchInterval"/> - matching, playback and IK are
    /// untouched, so a character LOD'd down to a coarse cadence still looks correct, just less
    /// reactive to sudden intent changes.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Motion Matching LOD")]
    [DisallowMultipleComponent]
    public sealed class MotionMatchingLOD : MonoBehaviour
    {
        [SerializeField] private MotionMatchingController _controller;

        [Tooltip("Distance reference. Defaults to Camera.main's transform if left empty.")]
        [SerializeField] private Transform _referencePoint;

        [Tooltip("Distance (metres) at each tier, ascending. Between tiers the multiplier is linearly interpolated; below the first or above the last it is clamped to that tier's value.")]
        [SerializeField] private float[] _distanceTiers = { 10f, 25f, 50f };

        [Tooltip("Search interval multiplier at each tier. Must be the same length as Distance Tiers.")]
        [SerializeField] private float[] _intervalMultipliers = { 1f, 2f, 4f };

        [Tooltip("Recompute at most this often (seconds). Distance rarely needs per-frame precision.")]
        [SerializeField, Range(0.05f, 2f)] private float _recomputeInterval = 0.25f;

        private float _baseInterval;
        private float _recomputeTimer;

        private void Awake()
        {
            if (_controller == null) _controller = GetComponent<MotionMatchingController>();
            _baseInterval = _controller != null ? _controller.SearchInterval : 0.1f;
        }

        private void OnEnable()
        {
            _recomputeTimer = 0f;
        }

        private void Update()
        {
            if (_controller == null) return;

            _recomputeTimer -= Time.deltaTime;
            if (_recomputeTimer > 0f) return;
            _recomputeTimer = _recomputeInterval;

            Transform reference = _referencePoint;
            if (reference == null && Camera.main != null) reference = Camera.main.transform;
            if (reference == null) return;

            float distance = Vector3.Distance(transform.position, reference.position);
            float multiplier = EvaluateMultiplier(distance, _distanceTiers, _intervalMultipliers);
            _controller.SearchInterval = _baseInterval * multiplier;
        }

        /// <summary>
        /// Piecewise-linear interpolation of <paramref name="multipliers"/> over
        /// <paramref name="distanceTiers"/> at <paramref name="distance"/>. Below the first tier
        /// returns the first multiplier; at or above the last tier returns the last. Falls back to 1
        /// (no degradation) on malformed input (null, empty, or mismatched array lengths) rather than
        /// throwing, since this runs every frame off serialized inspector data that a designer could
        /// leave in a bad state. Pure function - no Unity scene dependency - so it is unit-testable
        /// without instantiating a controller or camera.
        /// </summary>
        public static float EvaluateMultiplier(float distance, float[] distanceTiers, float[] multipliers)
        {
            if (distanceTiers == null || multipliers == null || distanceTiers.Length == 0 ||
                distanceTiers.Length != multipliers.Length)
            {
                return 1f;
            }

            if (distance <= distanceTiers[0]) return multipliers[0];

            for (int i = 1; i < distanceTiers.Length; i++)
            {
                if (distance <= distanceTiers[i])
                {
                    float span = distanceTiers[i] - distanceTiers[i - 1];
                    float t = span > 0f ? (distance - distanceTiers[i - 1]) / span : 0f;
                    return Mathf.Lerp(multipliers[i - 1], multipliers[i], t);
                }
            }

            return multipliers[multipliers.Length - 1];
        }
    }
}
