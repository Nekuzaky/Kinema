using System;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// A captured skeleton performance: the root's motion plus every bone's local rotation, frame by
    /// frame. Deliberately plain data with no Unity asset behind it - recording happens at runtime
    /// where assets cannot be written, so the take lives in memory until an editor tool bakes it into
    /// an <see cref="AnimationClip"/>.
    ///
    /// Only local rotations are stored per bone. Bone translations do not change during animation on
    /// a rigid skeleton, so recording them would multiply the data for nothing; the bind pose already
    /// carries them.
    /// </summary>
    [Serializable]
    public sealed class PoseTake
    {
        #region Public

        /// <summary>Transform paths of the recorded bones, relative to the character root.</summary>
        public string[] BonePaths = Array.Empty<string>();

        /// <summary>Root position per frame, in character-local space.</summary>
        public Vector3[] RootPositions = Array.Empty<Vector3>();

        /// <summary>Root rotation per frame.</summary>
        public Quaternion[] RootRotations = Array.Empty<Quaternion>();

        /// <summary>Bone local rotations, laid out frame-major: [frame * BoneCount + bone].</summary>
        public Quaternion[] BoneRotations = Array.Empty<Quaternion>();

        /// <summary>Seconds since the take started, per frame.</summary>
        public float[] Times = Array.Empty<float>();

        public string SourceRigName = "";

        public int BoneCount => BonePaths.Length;
        public int FrameCount => Times.Length;
        public float Duration => FrameCount > 0 ? Times[FrameCount - 1] : 0f;
        public bool IsValid => FrameCount > 1 && BoneCount > 0;

        #endregion

        #region Main API

        /// <summary>Bone local rotation at a frame.</summary>
        public Quaternion GetBoneRotation(int frame, int bone) => BoneRotations[frame * BoneCount + bone];

        /// <summary>Total ground distance the root covered, for reporting.</summary>
        public float DistanceTravelled()
        {
            float distance = 0f;
            for (int i = 1; i < FrameCount; i++)
            {
                Vector3 step = RootPositions[i] - RootPositions[i - 1];
                step.y = 0f;
                distance += step.magnitude;
            }
            return distance;
        }

        #endregion
    }
}
