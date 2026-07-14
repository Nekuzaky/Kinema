using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// A tagged time range inside a clip. Tag semantics (stance, strafe, injured, terrain...) are
    /// project-defined: the config declares tag names (up to 64) and ranges reference them by index.
    /// </summary>
    [Serializable]
    public struct TagRange
    {
        [Tooltip("Index into the config's tag name list.")]
        public int TagIndex;

        [Tooltip("Range start inside the clip, seconds.")]
        public float Start;

        [Tooltip("Range end inside the clip, seconds.")]
        public float End;

        public bool Contains(float time) => time >= Start && time <= End;
    }

    /// <summary>All tag ranges authored on one clip.</summary>
    [Serializable]
    public sealed class ClipTagTrack
    {
        public AnimationClip Clip;
        public List<TagRange> Ranges = new List<TagRange>();

        /// <summary>Builds the 64-bit tag mask for a clip-local time.</summary>
        public ulong MaskAt(float time)
        {
            ulong mask = 0;
            for (int i = 0; i < Ranges.Count; i++)
                if (Ranges[i].Contains(time) && Ranges[i].TagIndex >= 0 && Ranges[i].TagIndex < 64)
                    mask |= 1ul << Ranges[i].TagIndex;
            return mask;
        }
    }
}
