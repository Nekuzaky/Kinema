using System;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Lightweight descriptor for one baked frame. The heavy per-frame data (the feature
    /// vector) lives in a flat array on the database; this struct is the sidecar that maps a
    /// feature row back to a clip and a local playback time.
    /// <para>
    /// Extension-ready: mirroring, tags and section ids can be added here without touching the
    /// feature layout, because they are metadata rather than matched dimensions.
    /// </para>
    /// </summary>
    [Serializable]
    public struct MotionFrameInfo
    {
        #region Public

        /// <summary>Index into <see cref="MotionMatchingDatabase.Clips"/>.</summary>
        public int ClipIndex;

        /// <summary>Local playback time inside the source clip, in seconds.</summary>
        public float Time;

        /// <summary>True when this frame is a mirrored variant of its source clip (reserved for V2).</summary>
        public bool IsMirrored;

        public MotionFrameInfo(int clipIndex, float time, bool isMirrored = false)
        {
            ClipIndex = clipIndex;
            Time = time;
            IsMirrored = isMirrored;
        }

        #endregion
    }
}
