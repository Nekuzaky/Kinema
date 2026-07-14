using UnityEngine;

namespace Kinema.MotionMatching.Samples
{
    /// <summary>
    /// Minimal smoothed follow camera for the demo: sits at a fixed world offset behind the target
    /// and eases toward it. Deliberately dumb (no orbit, no input) so the demo shows off the motion
    /// matching, not the camera.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Samples/Follow Camera")]
    public sealed class FollowCamera : MonoBehaviour
    {
        #region Public

        [SerializeField] private Transform _target;
        [SerializeField] private Vector3 _worldOffset = new Vector3(0f, 3f, -6f);
        [SerializeField] private float _lookHeight = 1.2f;
        [SerializeField, Min(0f)] private float _followSharpness = 6f;

        public void SetTarget(Transform target) => _target = target;

        #endregion

        #region Private and Protected

        private Vector3 _velocity;

        #endregion

        #region Unity API

        private void LateUpdate()
        {
            if (_target == null) return;

            Vector3 desired = _target.position + _worldOffset;
            float t = 1f - Mathf.Exp(-_followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.LookAt(_target.position + Vector3.up * _lookHeight);
        }

        #endregion
    }
}
