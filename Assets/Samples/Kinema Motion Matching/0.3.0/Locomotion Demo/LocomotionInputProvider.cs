using UnityEngine;
using UnityEngine.InputSystem;

namespace Kinema.MotionMatching.Samples
{
    /// <summary>
    /// Sample locomotion driver: turns WASD / left-stick input into a camera-relative desired
    /// velocity and feeds it to the controller through <see cref="ILocomotionProvider"/>. Kept in the
    /// Samples assembly so the runtime stays free of an Input System dependency and studios can swap
    /// in their own intent source.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Samples/Locomotion Input Provider")]
    [RequireComponent(typeof(MotionMatchingController))]
    public sealed class LocomotionInputProvider : MonoBehaviour, ILocomotionProvider
    {
        #region Public

        [SerializeField, Min(0f)] private float _maxSpeed = 4f;
        [Tooltip("Movement is relative to this transform's yaw. Defaults to the main camera.")]
        [SerializeField] private Transform _cameraTransform;
        [Tooltip("How quickly the desired velocity chases the input. Higher = snappier.")]
        [SerializeField, Min(0f)] private float _inputSharpness = 12f;

        public Vector3 DesiredVelocity => _desiredVelocity;

        // Face the direction of travel; returning zero lets the controller derive facing from velocity.
        public Vector3 DesiredFacing => Vector3.zero;

        #endregion

        #region Private and Protected

        private InputAction _moveAction;
        private Vector3 _desiredVelocity;

        #endregion

        #region Unity API

        private void Awake()
        {
            _moveAction = new InputAction("Move", InputActionType.Value);
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddBinding("<Gamepad>/leftStick");

            if (_cameraTransform == null && Camera.main != null)
                _cameraTransform = Camera.main.transform;
        }

        private void OnEnable() => _moveAction.Enable();
        private void OnDisable() => _moveAction.Disable();

        private void Update()
        {
            Vector2 move = _moveAction.ReadValue<Vector2>();

            Vector3 forward = _cameraTransform != null ? Flatten(_cameraTransform.forward) : Vector3.forward;
            Vector3 right = _cameraTransform != null ? Flatten(_cameraTransform.right) : Vector3.right;

            Vector3 direction = right * move.x + forward * move.y;
            if (direction.sqrMagnitude > 1f) direction.Normalize();

            Vector3 target = direction * _maxSpeed;
            float t = 1f - Mathf.Exp(-_inputSharpness * Time.deltaTime);
            _desiredVelocity = Vector3.Lerp(_desiredVelocity, target, t);
        }

        #endregion

        #region Tools and Utilities

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : v;
        }

        #endregion
    }
}
