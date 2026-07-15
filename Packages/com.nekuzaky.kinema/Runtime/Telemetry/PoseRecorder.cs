using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Records the skeleton as it is actually posed on screen, frame by frame.
    ///
    /// This is a different capture from <see cref="SessionRecorder"/>, and the two answer different
    /// questions. SessionRecorder stores *intent* - the desired velocity you asked for - so replaying
    /// it re-runs the matcher and can produce a different (equally valid) performance. PoseRecorder
    /// stores the *result*: the exact bones, after matching, blending, stride warping and IK. Replay
    /// it and you get that performance back verbatim, which is what makes it bakeable into a clip.
    ///
    /// Runs after everything that touches the pose, IK included, so what is captured is what was
    /// rendered rather than what the graph proposed.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Pose Recorder")]
    [DefaultExecutionOrder(500)]
    public sealed class PoseRecorder : MonoBehaviour
    {
        #region Public

        [Tooltip("Stop recording past this many frames, so a forgotten recorder cannot eat memory.")]
        [SerializeField, Min(60)] private int _maxFrames = 18000;

        [Tooltip("Skip bones whose name contains any of these, to keep takes lean.")]
        [SerializeField] private string[] _excludedBoneFilters = { "IK", "Twist" };

        public bool IsRecording { get; private set; }
        public int RecordedFrameCount => _times.Count;
        public float RecordedDuration => _times.Count > 0 ? _times[^1] : 0f;

        /// <summary>The last completed take, or null until one is recorded.</summary>
        public PoseTake LastTake { get; private set; }

        #endregion

        #region Private and Protected

        private readonly List<float> _times = new();
        private readonly List<Vector3> _rootPositions = new();
        private readonly List<Quaternion> _rootRotations = new();
        private readonly List<Quaternion> _boneRotations = new();

        private Transform[] _bones;
        private string[] _bonePaths;
        private float _startTime;

        #endregion

        #region Unity API

        private void LateUpdate()
        {
            if (!IsRecording) return;

            if (_times.Count >= _maxFrames)
            {
                Debug.LogWarning($"[Kinema] Pose recording hit the {_maxFrames}-frame cap and stopped.", this);
                StopRecording();
                return;
            }

            _times.Add(Time.time - _startTime);
            _rootPositions.Add(transform.localPosition);
            _rootRotations.Add(transform.localRotation);
            foreach (Transform bone in _bones) _boneRotations.Add(bone.localRotation);
        }

        #endregion

        #region Main API

        public void StartRecording()
        {
            if (_bones == null) ResolveBones();
            if (_bones.Length == 0)
            {
                Debug.LogWarning($"[Kinema] '{name}' has no bones under it to record.", this);
                return;
            }

            _times.Clear();
            _rootPositions.Clear();
            _rootRotations.Clear();
            _boneRotations.Clear();
            _startTime = Time.time;
            IsRecording = true;
        }

        /// <summary>Ends the take and publishes it as <see cref="LastTake"/>. Returns null if nothing usable was captured.</summary>
        public PoseTake StopRecording()
        {
            if (!IsRecording) return LastTake;
            IsRecording = false;

            if (_times.Count < 2)
            {
                Debug.LogWarning("[Kinema] Take too short to be usable; discarded.", this);
                return null;
            }

            LastTake = new PoseTake
            {
                BonePaths = _bonePaths,
                Times = _times.ToArray(),
                RootPositions = _rootPositions.ToArray(),
                RootRotations = _rootRotations.ToArray(),
                BoneRotations = _boneRotations.ToArray(),
                SourceRigName = name
            };
            return LastTake;
        }

        #endregion

        #region Tools and Utilities

        private void ResolveBones()
        {
            var bones = new List<Transform>();
            var paths = new List<string>();

            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (bone == transform) continue;      // the root is stored separately
                if (IsExcluded(bone.name)) continue;

                bones.Add(bone);
                paths.Add(BuildPath(bone));
            }

            _bones = bones.ToArray();
            _bonePaths = paths.ToArray();
        }

        private bool IsExcluded(string boneName)
        {
            if (_excludedBoneFilters == null) return false;
            foreach (string filter in _excludedBoneFilters)
                if (!string.IsNullOrEmpty(filter) && boneName.Contains(filter))
                    return true;
            return false;
        }

        /// <summary>Path relative to this transform, which is what AnimationClip curves are keyed by.</summary>
        private string BuildPath(Transform bone)
        {
            var builder = new StringBuilder(bone.name);
            for (Transform parent = bone.parent; parent != null && parent != transform; parent = parent.parent)
                builder.Insert(0, parent.name + "/");
            return builder.ToString();
        }

        #endregion
    }
}
