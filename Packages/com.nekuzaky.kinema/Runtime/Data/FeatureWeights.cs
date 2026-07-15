using System;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Per-group scalar weights applied on top of the normalized feature distance.
    /// One float per <see cref="FeatureGroup"/>. Kept as a plain serializable struct so it
    /// can live on the config, on the database and on the controller without ceremony.
    /// </summary>
    [Serializable]
    public struct FeatureWeights
    {
        #region Public

        [Min(0f)] public float TrajectoryPosition;
        [Min(0f)] public float TrajectoryDirection;
        [Min(0f)] public float BonePosition;
        [Min(0f)] public float BoneVelocity;
        [Min(0f)] public float RootVelocity;

        [Tooltip("Penalty for candidates at a different point of the step cycle. Discourages jumps that cut a stride mid-step; needs foot phases baked (a rebake on older databases). 0 disables.")]
        [Min(0f)] public float FootPhase;

        public static FeatureWeights Default => new FeatureWeights
        {
            TrajectoryPosition = 1.0f,
            TrajectoryDirection = 1.0f,
            BonePosition = 1.0f,
            BoneVelocity = 0.6f,
            RootVelocity = 0.8f,
            FootPhase = 0.5f
        };

        #endregion

        #region Main API

        public float Get(FeatureGroup group)
        {
            switch (group)
            {
                case FeatureGroup.TrajectoryPosition: return TrajectoryPosition;
                case FeatureGroup.TrajectoryDirection: return TrajectoryDirection;
                case FeatureGroup.BonePosition: return BonePosition;
                case FeatureGroup.BoneVelocity: return BoneVelocity;
                case FeatureGroup.RootVelocity: return RootVelocity;
                default: return 0f;
            }
        }

        #endregion
    }
}
