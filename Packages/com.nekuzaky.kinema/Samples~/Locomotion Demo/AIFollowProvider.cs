using Kinema.MotionMatching;
using UnityEngine;

namespace Kinema.MotionMatching.Samples
{
    /// <summary>
    /// AI locomotion intent: seeks a target Transform through the exact same
    /// <see cref="ILocomotionProvider"/> contract the player uses, proving the controller is
    /// input-agnostic. Direct seek with arrival slow-down; swap in a NavMeshAgent's
    /// desiredVelocity here for pathfinding-aware movement. Pair with VaultTrigger's Auto Vault
    /// so the AI clears low obstacles by itself.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Samples/AI Follow Provider")]
    [RequireComponent(typeof(MotionMatchingController))]
    public sealed class AIFollowProvider : MonoBehaviour, ILocomotionProvider
    {
        #region Public

        [Tooltip("What to chase. The player character, a waypoint, anything.")]
        [SerializeField] private Transform _target;

        [SerializeField, Min(0f)] private float _maxSpeed = 3.5f;

        [Tooltip("Stop when closer than this (meters).")]
        [SerializeField, Min(0f)] private float _stopDistance = 1.5f;

        [Tooltip("Start slowing down within this distance beyond the stop radius (meters).")]
        [SerializeField, Min(0.1f)] private float _slowRadius = 2.5f;

        public Vector3 DesiredVelocity { get; private set; }

        // Face the direction of travel.
        public Vector3 DesiredFacing => Vector3.zero;

        public void SetTarget(Transform target) => _target = target;

        #endregion

        #region Unity API

        private void Update()
        {
            if (_target == null)
            {
                DesiredVelocity = Vector3.zero;
                return;
            }

            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            if (distance <= _stopDistance)
            {
                DesiredVelocity = Vector3.zero;
                return;
            }

            float speed = _maxSpeed * Mathf.Clamp01((distance - _stopDistance) / _slowRadius);
            DesiredVelocity = toTarget / Mathf.Max(distance, 1e-4f) * speed;
        }

        #endregion
    }
}
