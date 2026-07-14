using System.Collections.Generic;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Authoring asset for a motion matching setup. It owns three things:
    ///   1. the feature schema (what gets baked and matched),
    ///   2. the default matching weights,
    ///   3. the bake job (which clips, sampled on which rig).
    /// The baker consumes this asset and produces a <see cref="MotionMatchingDatabase"/>; the
    /// controller reads the schema and default weights from the database it is given, so this
    /// asset stays purely an authoring concern.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MotionMatchingConfig",
        menuName = "Kinema/Motion Matching/Config",
        order = 0)]
    public sealed class MotionMatchingConfig : ScriptableObject
    {
        #region Public

        [Header("Feature Schema")]
        [Tooltip("Layout of the feature vector: trajectory horizons and sampled bones.")]
        [SerializeField] private FeatureSchema _schema = new FeatureSchema();

        [Header("Default Matching Weights")]
        [Tooltip("Starting weights written into the baked database; can be overridden per controller.")]
        [SerializeField] private FeatureWeights _defaultWeights = FeatureWeights.Default;

        [Header("Bake Job")]
        [Tooltip("Sampling rate used when baking clips into frames (frames per second).")]
        [SerializeField, Range(10, 120)] private int _bakeFrameRate = 30;

        [Tooltip("Rig used to sample poses. A humanoid or generic prefab whose bone names match the schema.")]
        [SerializeField] private GameObject _rigPrefab;

        [Tooltip("Clips baked into the database. Locomotion clips (walk / run / turns / idle) for a V1.")]
        [SerializeField] private List<AnimationClip> _clips = new List<AnimationClip>();

        [Header("Calibration")]
        [Tooltip("Named weight presets baked into the database; switch at runtime with SetCalibrationProfile.")]
        [SerializeField] private List<CalibrationProfile> _calibrationProfiles = new List<CalibrationProfile>();

        [Header("Storage")]
        [Tooltip("Store features as 16-bit halves: halves the asset size, decoded once at load. Negligible quality impact on normalized features.")]
        [SerializeField] private bool _halfPrecision;

        [Header("Mirroring (experimental)")]
        [Tooltip("Bake a mirrored variant of every frame (doubles coverage). Playback mirrors the pose at runtime; requires a left/right symmetric rig. Experimental.")]
        [SerializeField] private bool _generateMirroredVariants;

        [Header("Tags")]
        [Tooltip("Project tag vocabulary (max 64). Ranges on clips reference these by index.")]
        [SerializeField] private List<string> _tagNames = new List<string>();

        [Tooltip("Tag ranges authored per clip. Edited visually in the window's Tags tab.")]
        [SerializeField] private List<ClipTagTrack> _tagTracks = new List<ClipTagTrack>();

        public FeatureSchema Schema => _schema;
        public FeatureWeights DefaultWeights => _defaultWeights;
        public int BakeFrameRate => _bakeFrameRate;
        public GameObject RigPrefab => _rigPrefab;
        public IReadOnlyList<AnimationClip> Clips => _clips;
        public bool GenerateMirroredVariants => _generateMirroredVariants;
        public IReadOnlyList<CalibrationProfile> CalibrationProfiles => _calibrationProfiles;
        public bool HalfPrecision => _halfPrecision;
        public IReadOnlyList<string> TagNames => _tagNames;
        public IReadOnlyList<ClipTagTrack> TagTracks => _tagTracks;

        /// <summary>Tag track for a clip, or null when none authored.</summary>
        public ClipTagTrack FindTagTrack(AnimationClip clip)
        {
            for (int i = 0; i < _tagTracks.Count; i++)
                if (_tagTracks[i].Clip == clip) return _tagTracks[i];
            return null;
        }

        #endregion

        #region Main API

        public bool IsReadyToBake(out string reason)
        {
            if (_rigPrefab == null)
            {
                reason = "No rig prefab assigned.";
                return false;
            }
            if (_clips == null || _clips.Count == 0)
            {
                reason = "No clips assigned.";
                return false;
            }
            for (int i = 0; i < _clips.Count; i++)
            {
                if (_clips[i] == null)
                {
                    reason = $"Clip slot {i} is empty.";
                    return false;
                }
            }
            if (_schema == null || _schema.Dimension <= 0)
            {
                reason = "Feature schema is empty (no trajectory points and no bones).";
                return false;
            }
            reason = null;
            return true;
        }

        #endregion
    }
}
