using Kinema.MotionMatching;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kinema.MotionMatching.Samples
{
    /// <summary>
    /// Demo vault trigger, MxM-style event workflow: a chest-height ray probes ahead for a low
    /// obstacle; when one is in range and the player presses jump, the controller plays the vault
    /// motion event with its contact warped onto the obstacle's near edge. Matching pauses for the
    /// clip and resumes on landing; the inertializer absorbs both seams.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Samples/Vault Trigger")]
    [RequireComponent(typeof(MotionMatchingController))]
    public sealed class VaultTrigger : MonoBehaviour
    {
        #region Public

        [Tooltip("The vault motion event (clip + contact time + warp settings).")]
        [SerializeField] private MotionEventDefinition _vaultEvent;

        [Tooltip("Free jump while moving, when no obstacle is ahead. Plays with its own root motion, no warp.")]
        [SerializeField] private MotionEventDefinition _jumpMovingEvent;

        [Tooltip("Free jump from standstill, when no obstacle is ahead.")]
        [SerializeField] private MotionEventDefinition _jumpIdleEvent;

        [Tooltip("How far ahead an obstacle can be triggered (meters).")]
        [SerializeField, Range(0.5f, 3f)] private float _maxDistance = 1.4f;

        [Tooltip("Obstacle top must be at least this high above the feet to vault (meters).")]
        [SerializeField, Range(0.1f, 1f)] private float _minObstacleHeight = 0.35f;

        [Tooltip("Obstacle top must be below this height to vault (meters).")]
        [SerializeField, Range(0.5f, 2f)] private float _maxObstacleHeight = 1.15f;

        [Tooltip("Vault automatically when eligible and moving toward the obstacle - for AI characters or hands-free traversal.")]
        [SerializeField] private bool _autoVault;

        /// <summary>True while an eligible obstacle is ahead (drive a UI prompt from this).</summary>
        public bool CanVault { get; private set; }

        #endregion

        #region Private and Protected

        private MotionMatchingController _controller;
        private InputAction _vaultAction;

        #endregion

        #region Unity API

        private void Awake()
        {
            _controller = GetComponent<MotionMatchingController>();

            _vaultAction = new InputAction("Vault", InputActionType.Button);
            _vaultAction.AddBinding("<Keyboard>/space");
            _vaultAction.AddBinding("<Gamepad>/buttonSouth");
        }

        private void OnEnable() => _vaultAction.Enable();
        private void OnDisable() => _vaultAction.Disable();

        private void Update()
        {
            CanVault = false;
            if (!_controller.IsInitialized || _controller.IsPlayingEvent) return;

            if (_vaultEvent != null && ProbeObstacle(out RaycastHit hit))
            {
                CanVault = true;

                // AI path: no input needed - vault as soon as the character is heading into the obstacle.
                bool autoTriggered = _autoVault && Vector3.Dot(_controller.DesiredVelocity, Flatten(transform.forward)) > 0.3f;

                if (autoTriggered || _vaultAction.WasPressedThisFrame())
                {
                    Debug.Log($"[Kinema] Vault over '{hit.collider.name}' (top {hit.collider.bounds.max.y - transform.position.y:F2} m).", this);
                    Vault(hit);
                }
                return;
            }

            // Open ground: the same button is a free jump. The event plays unwarped, so the arc is
            // the clip's own root motion; the motor already trusts vertical root motion during events.
            if (_vaultAction.WasPressedThisFrame())
            {
                bool moving = _controller.DesiredVelocity.sqrMagnitude > 0.25f;
                MotionEventDefinition jump = moving ? _jumpMovingEvent : _jumpIdleEvent;
                if (jump == null) jump = moving ? _jumpIdleEvent : _jumpMovingEvent;
                if (jump != null)
                {
                    Debug.Log($"[Kinema] Free jump ({(moving ? "moving" : "standing")}) with '{jump.name}'.", this);
                    _controller.PlayEvent(jump, transform.position, Quaternion.LookRotation(Flatten(transform.forward), Vector3.up));
                }
                else
                {
                    // Say why nothing happened rather than eating the press silently.
                    Debug.LogWarning("[Kinema] Jump pressed: no obstacle in the vault window and no free-jump event bound.", this);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = CanVault ? Color.green : Color.gray;
            Vector3 origin = transform.position + Vector3.up * 0.6f;
            Gizmos.DrawLine(origin, origin + Flatten(transform.forward) * _maxDistance);
        }

        #endregion

        #region Tools and Utilities

        /// <summary>Chest-height ray; eligible when the hit collider's top edge sits in the vault window.</summary>
        private bool ProbeObstacle(out RaycastHit hit)
        {
            Vector3 origin = transform.position + Vector3.up * 0.6f;
            Vector3 forward = Flatten(transform.forward);

            if (!Physics.Raycast(origin, forward, out hit, _maxDistance)) return false;
            if (hit.collider.isTrigger || hit.collider.transform.IsChildOf(transform)) return false;

            float top = hit.collider.bounds.max.y - transform.position.y;
            return top >= _minObstacleHeight && top <= _maxObstacleHeight;
        }

        private void Vault(RaycastHit hit)
        {
            Vector3 forward = Flatten(transform.forward);

            // Contact lands just past the near edge; height comes from the clip's root arc.
            Vector3 contact = hit.point + forward * 0.25f;
            contact.y = transform.position.y;

            _controller.PlayEvent(_vaultEvent, contact, Quaternion.LookRotation(forward, Vector3.up));
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        #endregion
    }
}
