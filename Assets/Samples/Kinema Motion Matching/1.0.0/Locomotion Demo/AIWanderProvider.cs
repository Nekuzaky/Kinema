using Kinema.MotionMatching;
using UnityEngine;

namespace Kinema.MotionMatching.Samples
{
    /// <summary>
    /// AI locomotion intent that wanders: picks a random point in a radius, walks to it, pauses,
    /// picks another. Same <see cref="ILocomotionProvider"/> contract the player uses, so it drives
    /// the identical motion matching stack with no input - and because it stops, turns and starts
    /// again on its own, it exercises exactly the idle/walk/turn transitions that are hardest to get
    /// right. Drop a crowd of these in a scene to watch the matcher run on many characters at once.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Samples/AI Wander Provider")]
    [RequireComponent(typeof(MotionMatchingController))]
    public sealed class AIWanderProvider : MonoBehaviour, ILocomotionProvider
    {
        #region Public

        [Tooltip("Wander within this radius of the home point (set on Start to the spawn position).")]
        [SerializeField, Min(1f)] private float _radius = 8f;

        [SerializeField, Min(0f)] private float _maxSpeed = 2.4f;

        [Tooltip("Stop when closer than this to the current goal (meters).")]
        [SerializeField, Min(0.1f)] private float _arriveDistance = 0.6f;

        [Tooltip("Ease speed down within this distance of the goal (meters).")]
        [SerializeField, Min(0.5f)] private float _slowRadius = 2f;

        [Tooltip("Seconds to idle at each goal before choosing the next.")]
        [SerializeField, Min(0f)] private float _pauseSeconds = 1.2f;

        public Vector3 DesiredVelocity { get; private set; }
        public Vector3 DesiredFacing => Vector3.zero; // face travel direction

        #endregion

        #region Private and Protected

        private Vector3 _home;
        private Vector3 _goal;
        private float _pauseUntil;
        private bool _homeSet;

        #endregion

        #region Unity API

        private void Start()
        {
            _home = transform.position;
            _homeSet = true;
            PickGoal();
        }

        private void Update()
        {
            if (!_homeSet) return;

            Vector3 toGoal = _goal - transform.position;
            toGoal.y = 0f;
            float distance = toGoal.magnitude;

            if (distance <= _arriveDistance)
            {
                DesiredVelocity = Vector3.zero;
                // Arrived: idle for a beat, then wander on. The pause is what makes the character
                // return to idle instead of pin-balling between goals.
                if (_pauseUntil <= 0f) _pauseUntil = Time.time + _pauseSeconds;
                else if (Time.time >= _pauseUntil) { _pauseUntil = 0f; PickGoal(); }
                return;
            }

            float speed = _maxSpeed * Mathf.Clamp01((distance - _arriveDistance) / _slowRadius);
            DesiredVelocity = toGoal / Mathf.Max(distance, 1e-4f) * speed;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.4f);
            Gizmos.DrawWireSphere(Application.isPlaying ? _home : transform.position, _radius);
            if (Application.isPlaying) Gizmos.DrawLine(transform.position, _goal);
        }

        #endregion

        #region Tools and Utilities

        private void PickGoal()
        {
            Vector2 disc = Random.insideUnitCircle * _radius;
            _goal = _home + new Vector3(disc.x, 0f, disc.y);
        }

        #endregion
    }
}
