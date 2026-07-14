using System;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// The baked motion matching database. Stores every frame's feature vector in one flat,
    /// cache-friendly float array plus the sidecar metadata needed to interpret and play it
    /// back. Features are stored already normalized (zero mean, unit std per dimension); the
    /// normalization stats are kept so the runtime can normalize a fresh query the same way.
    ///
    /// This is a pure data container: it is written by the baker and read by the matcher. It
    /// deliberately holds no matching logic of its own.
    /// </summary>
    [PreferBinarySerialization]
    public sealed class MotionMatchingDatabase : ScriptableObject
    {
        #region Public

        [SerializeField] private FeatureSchema _schema = new FeatureSchema();
        [SerializeField] private int _dimension;
        [SerializeField] private int _frameCount;

        [SerializeField] private float[] _features = Array.Empty<float>();       // frameCount * dimension, normalized
        [SerializeField] private float[] _featureMean = Array.Empty<float>();    // dimension
        [SerializeField] private float[] _featureStd = Array.Empty<float>();     // dimension

        [SerializeField] private MotionFrameInfo[] _frames = Array.Empty<MotionFrameInfo>();
        [SerializeField] private MotionClipEntry[] _clips = Array.Empty<MotionClipEntry>();

        [Header("Bake Metadata")]
        [SerializeField] private FeatureWeights _defaultWeights = FeatureWeights.Default;
        [SerializeField] private int _bakeFrameRate;
        [SerializeField] private string _bakeDateUtc;
        [SerializeField] private float _totalDurationSeconds;

        public FeatureSchema Schema => _schema;
        public int Dimension => _dimension;
        public int FrameCount => _frameCount;
        public int ClipCount => _clips.Length;

        public float[] Features => _features;
        public float[] FeatureMean => _featureMean;
        public float[] FeatureStd => _featureStd;

        public FeatureWeights DefaultWeights => _defaultWeights;
        public int BakeFrameRate => _bakeFrameRate;
        public string BakeDateUtc => _bakeDateUtc;
        public float TotalDurationSeconds => _totalDurationSeconds;

        public bool IsValid => _frameCount > 0 && _dimension > 0 && _features.Length == _frameCount * _dimension;

        #endregion

        #region Main API

        public MotionFrameInfo GetFrame(int frameIndex) => _frames[frameIndex];

        public MotionClipEntry GetClip(int clipIndex) => _clips[clipIndex];

        public MotionClipEntry GetClipOfFrame(int frameIndex) => _clips[_frames[frameIndex].ClipIndex];

        /// <summary>Start index of the given frame's feature row inside <see cref="Features"/>.</summary>
        public int GetFeatureOffset(int frameIndex) => frameIndex * _dimension;

        public float DenormalizeValue(int dimension, float normalized)
        {
            return normalized * _featureStd[dimension] + _featureMean[dimension];
        }

        public float NormalizeValue(int dimension, float raw)
        {
            return (raw - _featureMean[dimension]) / _featureStd[dimension];
        }

        /// <summary>
        /// Maps a clip-local time to the nearest baked frame index of that clip. Used by the
        /// controller to look up the pose portion of the query while a clip is playing.
        /// </summary>
        public int MapClipTimeToFrame(int clipIndex, float clipTime)
        {
            MotionClipEntry clip = _clips[clipIndex];
            if (clip.FrameCount <= 1) return clip.StartFrame;
            float frameDt = _bakeFrameRate > 0 ? 1f / _bakeFrameRate : 1f / 30f;
            int local = Mathf.RoundToInt(clipTime / frameDt);
            local = Mathf.Clamp(local, 0, clip.FrameCount - 1);
            return clip.StartFrame + local;
        }

        /// <summary>
        /// Reconstructs the candidate trajectory of a baked frame in character space, denormalized
        /// and ready for gizmo drawing or debug inspection.
        /// </summary>
        public void GetTrajectory(int frameIndex, TrajectorySample[] buffer)
        {
            int points = _schema.TrajectoryPointCount;
            int baseOffset = GetFeatureOffset(frameIndex);
            int posOffset = _schema.TrajectoryPositionOffset;
            int dirOffset = _schema.TrajectoryDirectionOffset;

            for (int p = 0; p < points && p < buffer.Length; p++)
            {
                int px = posOffset + p * 2;
                int dx = dirOffset + p * 2;
                Vector2 pos = new Vector2(
                    DenormalizeValue(px, _features[baseOffset + px]),
                    DenormalizeValue(px + 1, _features[baseOffset + px + 1]));
                Vector2 dir = new Vector2(
                    DenormalizeValue(dx, _features[baseOffset + dx]),
                    DenormalizeValue(dx + 1, _features[baseOffset + dx + 1]));
                buffer[p] = new TrajectorySample(pos, dir);
            }
        }

        /// <summary>Reconstructs the sampled bone local positions (character space, denormalized) of a baked frame.</summary>
        public void GetBonePositions(int frameIndex, Vector3[] buffer)
        {
            int baseOffset = GetFeatureOffset(frameIndex);
            int posOffset = _schema.BonePositionOffset;
            int count = _schema.BoneCount;
            for (int b = 0; b < count && b < buffer.Length; b++)
            {
                int o = posOffset + b * 3;
                buffer[b] = new Vector3(
                    DenormalizeValue(o, _features[baseOffset + o]),
                    DenormalizeValue(o + 1, _features[baseOffset + o + 1]),
                    DenormalizeValue(o + 2, _features[baseOffset + o + 2]));
            }
        }

        #endregion

        #region Tools and Utilities

        /// <summary>
        /// Populates the database from a completed bake. Kept internal-ish via a single entry point
        /// so callers cannot leave the arrays in an inconsistent state.
        /// </summary>
        public void SetBakedData(
            FeatureSchema schema,
            float[] features,
            float[] mean,
            float[] std,
            MotionFrameInfo[] frames,
            MotionClipEntry[] clips,
            FeatureWeights defaultWeights,
            int bakeFrameRate,
            string bakeDateUtc,
            float totalDuration)
        {
            _schema = schema;
            _dimension = schema.Dimension;
            _frameCount = frames.Length;
            _features = features;
            _featureMean = mean;
            _featureStd = std;
            _frames = frames;
            _clips = clips;
            _defaultWeights = defaultWeights;
            _bakeFrameRate = bakeFrameRate;
            _bakeDateUtc = bakeDateUtc;
            _totalDurationSeconds = totalDuration;
        }

        #endregion
    }
}
