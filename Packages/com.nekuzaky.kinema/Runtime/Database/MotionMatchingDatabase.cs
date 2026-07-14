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

        // Optional 16-bit storage: halves the asset size; decoded once at runtime.
        [SerializeField] private bool _halfPrecision;
        [SerializeField] private ushort[] _featuresHalf = Array.Empty<ushort>();
        [NonSerialized] private float[] _decodedFeatures;

        [SerializeField] private MotionFrameInfo[] _frames = Array.Empty<MotionFrameInfo>();
        [SerializeField] private MotionClipEntry[] _clips = Array.Empty<MotionClipEntry>();

        // Foot contacts: one byte per frame, bit b = contact bone b grounded.
        [SerializeField] private byte[] _contacts = Array.Empty<byte>();
        [SerializeField] private int[] _contactBoneIndices = Array.Empty<int>();

        // Semantic tags: one 64-bit mask per frame, names indexed by bit.
        [SerializeField] private ulong[] _frameTags = Array.Empty<ulong>();
        [SerializeField] private string[] _tagNames = Array.Empty<string>();

        [Header("Bake Metadata")]
        [SerializeField] private FeatureWeights _defaultWeights = FeatureWeights.Default;
        [SerializeField] private int _bakeFrameRate;
        [SerializeField] private string _bakeDateUtc;
        [SerializeField] private float _totalDurationSeconds;

        public FeatureSchema Schema => _schema;
        public int Dimension => _dimension;
        public int FrameCount => _frameCount;
        public int ClipCount => _clips.Length;

        public float[] Features
        {
            get
            {
                if (_features != null && _features.Length > 0) return _features;
                if (_halfPrecision && _featuresHalf.Length > 0)
                {
                    if (_decodedFeatures == null)
                    {
                        _decodedFeatures = new float[_featuresHalf.Length];
                        for (int i = 0; i < _featuresHalf.Length; i++)
                            _decodedFeatures[i] = Mathf.HalfToFloat(_featuresHalf[i]);
                    }
                    return _decodedFeatures;
                }
                return _features ?? Array.Empty<float>();
            }
        }

        public bool IsHalfPrecision => _halfPrecision;
        public float[] FeatureMean => _featureMean;
        public float[] FeatureStd => _featureStd;

        public FeatureWeights DefaultWeights => _defaultWeights;
        public int BakeFrameRate => _bakeFrameRate;
        public string BakeDateUtc => _bakeDateUtc;
        public float TotalDurationSeconds => _totalDurationSeconds;

        public bool IsValid => _frameCount > 0 && _dimension > 0 && Features.Length == _frameCount * _dimension;

        [SerializeField] private CalibrationProfile[] _calibrationProfiles = Array.Empty<CalibrationProfile>();
        public CalibrationProfile[] CalibrationProfiles => _calibrationProfiles;

        /// <summary>Profile by name, or null when unknown.</summary>
        public CalibrationProfile FindProfile(string profileName)
        {
            for (int i = 0; i < _calibrationProfiles.Length; i++)
                if (_calibrationProfiles[i].Name == profileName) return _calibrationProfiles[i];
            return null;
        }

        /// <summary>Schema-bone indices flagged as feet at bake time (max 8).</summary>
        public int[] ContactBoneIndices => _contactBoneIndices;
        public int ContactBoneCount => _contactBoneIndices?.Length ?? 0;
        public bool HasContacts => _contacts != null && _contacts.Length == _frameCount && ContactBoneCount > 0;

        public bool HasTags => _frameTags != null && _frameTags.Length == _frameCount && _tagNames.Length > 0;

        [SerializeField] private bool _hasMirroredFrames;

        /// <summary>True when the second half of the frame range holds mirrored variants.</summary>
        public bool HasMirroredFrames => _hasMirroredFrames;

        /// <summary>Maps an original frame to its mirrored twin (or back). Identity when unmirrored.</summary>
        public int GetMirroredTwin(int frameIndex)
        {
            if (!_hasMirroredFrames) return frameIndex;
            int half = _frameCount / 2;
            return frameIndex < half ? frameIndex + half : frameIndex - half;
        }
        public string[] TagNames => _tagNames;
        public ulong[] FrameTags => _frameTags;

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
            float[] features = Features;

            for (int p = 0; p < points && p < buffer.Length; p++)
            {
                int px = posOffset + p * 2;
                int dx = dirOffset + p * 2;
                Vector2 pos = new Vector2(
                    DenormalizeValue(px, features[baseOffset + px]),
                    DenormalizeValue(px + 1, features[baseOffset + px + 1]));
                Vector2 dir = new Vector2(
                    DenormalizeValue(dx, features[baseOffset + dx]),
                    DenormalizeValue(dx + 1, features[baseOffset + dx + 1]));
                buffer[p] = new TrajectorySample(pos, dir);
            }
        }

        /// <summary>Tag mask of a frame (0 when the database was baked without tags).</summary>
        public ulong GetFrameTags(int frameIndex)
        {
            return HasTags ? _frameTags[frameIndex] : 0ul;
        }

        /// <summary>Mask of a tag by name, or 0 when unknown.</summary>
        public ulong GetTagMask(string tagName)
        {
            for (int i = 0; i < _tagNames.Length && i < 64; i++)
                if (_tagNames[i] == tagName) return 1ul << i;
            return 0ul;
        }

        /// <summary>Contact bitmask of a frame: bit b set = contact bone b grounded.</summary>
        public byte GetContacts(int frameIndex)
        {
            return HasContacts ? _contacts[frameIndex] : (byte)0;
        }

        /// <summary>Name of the schema bone behind contact slot <paramref name="contactSlot"/>.</summary>
        public string GetContactBoneName(int contactSlot)
        {
            if (contactSlot < 0 || contactSlot >= ContactBoneCount) return null;
            int b = _contactBoneIndices[contactSlot];
            return b >= 0 && b < _schema.BoneCount ? _schema.BoneNames[b] : null;
        }

        /// <summary>Reconstructs the sampled bone local positions (character space, denormalized) of a baked frame.</summary>
        public void GetBonePositions(int frameIndex, Vector3[] buffer)
        {
            int baseOffset = GetFeatureOffset(frameIndex);
            int posOffset = _schema.BonePositionOffset;
            int count = _schema.BoneCount;
            float[] features = Features;
            for (int b = 0; b < count && b < buffer.Length; b++)
            {
                int o = posOffset + b * 3;
                buffer[b] = new Vector3(
                    DenormalizeValue(o, features[baseOffset + o]),
                    DenormalizeValue(o + 1, features[baseOffset + o + 1]),
                    DenormalizeValue(o + 2, features[baseOffset + o + 2]));
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
            float totalDuration,
            byte[] contacts = null,
            int[] contactBoneIndices = null,
            ulong[] frameTags = null,
            string[] tagNames = null,
            CalibrationProfile[] calibrationProfiles = null,
            bool halfPrecision = false)
        {
            _calibrationProfiles = calibrationProfiles ?? Array.Empty<CalibrationProfile>();
            _halfPrecision = halfPrecision;
            _decodedFeatures = null;
            if (halfPrecision)
            {
                _featuresHalf = new ushort[features.Length];
                for (int i = 0; i < features.Length; i++)
                    _featuresHalf[i] = Mathf.FloatToHalf(features[i]);
                features = Array.Empty<float>(); // stored half only; decoded on demand.
            }
            else
            {
                _featuresHalf = Array.Empty<ushort>();
            }
            _contacts = contacts ?? Array.Empty<byte>();
            _contactBoneIndices = contactBoneIndices ?? Array.Empty<int>();
            _frameTags = frameTags ?? Array.Empty<ulong>();
            _tagNames = tagNames ?? Array.Empty<string>();
            _hasMirroredFrames = false;
            for (int i = 0; i < frames.Length; i++)
                if (frames[i].IsMirrored) { _hasMirroredFrames = true; break; }
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
