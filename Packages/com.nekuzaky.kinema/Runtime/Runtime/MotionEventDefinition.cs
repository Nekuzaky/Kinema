using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// A triggered action clip (vault, interaction, attack) played outside the matching loop.
    /// While the event runs, matching is suspended and the root motion is warped so that the clip's
    /// contact moment lands exactly on the requested target (MxM-style event warping).
    /// </summary>
    [CreateAssetMenu(
        fileName = "MotionEvent",
        menuName = "Kinema/Motion Matching/Motion Event",
        order = 10)]
    public sealed class MotionEventDefinition : ScriptableObject
    {
        #region Public

        [Tooltip("The action clip. Does not need to be part of the matching database.")]
        [SerializeField] private AnimationClip _clip;

        [Tooltip("Seconds into the clip where the contact/alignment must land on the target.")]
        [SerializeField, Min(0f)] private float _contactTime = 0.5f;

        [Tooltip("Transition duration into the event clip.")]
        [SerializeField, Range(0f, 0.4f)] private float _blendIn = 0.15f;

        [Tooltip("Warp the horizontal position toward the target until contact.")]
        [SerializeField] private bool _warpPosition = true;

        [Tooltip("Warp the yaw toward the target facing until contact.")]
        [SerializeField] private bool _warpRotation = true;

        public AnimationClip Clip => _clip;
        public float ContactTime => _clip != null ? Mathf.Min(_contactTime, _clip.length) : _contactTime;
        public float BlendIn => _blendIn;
        public bool WarpPosition => _warpPosition;
        public bool WarpRotation => _warpRotation;

        public bool IsValid => _clip != null && _clip.length > 0.01f;

        #endregion
    }
}
