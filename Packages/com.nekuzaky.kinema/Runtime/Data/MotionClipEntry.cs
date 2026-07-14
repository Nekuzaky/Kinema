using System;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Database record for one source <see cref="AnimationClip"/>: the clip reference kept for
    /// playback plus the baked range it occupies in the flat frame arrays.
    /// </summary>
    [Serializable]
    public struct MotionClipEntry
    {
        #region Public

        public AnimationClip Clip;
        public string Name;

        /// <summary>Index of this clip's first frame inside the database frame arrays.</summary>
        public int StartFrame;

        /// <summary>Number of baked frames belonging to this clip.</summary>
        public int FrameCount;

        /// <summary>Clip length in seconds at bake time.</summary>
        public float Length;

        public bool IsLooping;

        #endregion

        #region Tools and Utilities

        public int EndFrameExclusive => StartFrame + FrameCount;

        public bool ContainsFrame(int frameIndex)
        {
            return frameIndex >= StartFrame && frameIndex < EndFrameExclusive;
        }

        #endregion
    }
}
