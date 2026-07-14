using System;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Result of a single database search: the winning frame plus a full per-group cost
    /// breakdown so the debug layer can explain *why* that frame won.
    /// </summary>
    public struct MotionMatchResult
    {
        public int FrameIndex;
        public float TotalCost;

        /// <summary>Cost contribution of each <see cref="FeatureGroup"/> for the winning frame.</summary>
        public float[] GroupCosts;

        public float TrajectoryCost => GroupCosts[(int)FeatureGroup.TrajectoryPosition]
                                     + GroupCosts[(int)FeatureGroup.TrajectoryDirection];

        public float PoseCost => GroupCosts[(int)FeatureGroup.BonePosition]
                               + GroupCosts[(int)FeatureGroup.BoneVelocity]
                               + GroupCosts[(int)FeatureGroup.RootVelocity];

        public bool IsValid => FrameIndex >= 0;
    }

    /// <summary>
    /// Brute-force nearest-neighbour search over the database using a weighted, normalized
    /// squared distance. Linear scan with an early-out is more than fast enough for a V1 (a few
    /// thousand frames per tick); the class is intentionally shaped so a future acceleration
    /// structure (KD-tree, PCA projection) can replace <see cref="Search"/> without touching callers.
    /// </summary>
    public sealed class MotionMatcher
    {
        #region Private and Protected

        private readonly MotionMatchingDatabase _database;
        private float[] _perDimensionWeights;
        private readonly float[] _groupCostScratch = new float[FeatureGroupExtensions.Count];

        #endregion

        #region Public

        public MotionMatcher(MotionMatchingDatabase database, FeatureWeights weights)
        {
            _database = database;
            UpdateWeights(weights);
        }

        public MotionMatchingDatabase Database => _database;

        #endregion

        #region Main API

        /// <summary>Rebuilds the cached per-dimension weight table. Call when weights change.</summary>
        public void UpdateWeights(FeatureWeights weights)
        {
            _perDimensionWeights = _database.Schema.BuildPerDimensionWeights(weights);
        }

        /// <summary>
        /// Finds the lowest-cost frame for the given query. <paramref name="ignoreFrame"/> and its
        /// close neighbours can be excluded to avoid re-selecting the frame already playing.
        /// </summary>
        public MotionMatchResult Search(MotionMatchingQuery query, int ignoreFrame = -1, int ignoreRadius = 0)
        {
            float[] features = _database.Features;
            float[] weights = _perDimensionWeights;
            float[] values = query.Values;
            int dimension = _database.Dimension;
            int frameCount = _database.FrameCount;

            int bestFrame = -1;
            float bestCost = float.MaxValue;

            for (int f = 0; f < frameCount; f++)
            {
                if (ignoreFrame >= 0 && f >= ignoreFrame - ignoreRadius && f <= ignoreFrame + ignoreRadius)
                    continue;

                int offset = f * dimension;
                float cost = 0f;

                // Weighted squared distance with branch-and-bound early-out.
                for (int i = 0; i < dimension; i++)
                {
                    float d = values[i] - features[offset + i];
                    cost += weights[i] * d * d;
                    if (cost >= bestCost) break;
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestFrame = f;
                }
            }

            return BuildResult(query, bestFrame, bestCost);
        }

        /// <summary>Computes the total cost of a single candidate frame (used by the debug tools).</summary>
        public float EvaluateCost(MotionMatchingQuery query, int frameIndex)
        {
            float[] features = _database.Features;
            float[] weights = _perDimensionWeights;
            float[] values = query.Values;
            int dimension = _database.Dimension;
            int offset = frameIndex * dimension;

            float cost = 0f;
            for (int i = 0; i < dimension; i++)
            {
                float d = values[i] - features[offset + i];
                cost += weights[i] * d * d;
            }
            return cost;
        }

        #endregion

        #region Tools and Utilities

        private MotionMatchResult BuildResult(MotionMatchingQuery query, int frameIndex, float totalCost)
        {
            var groupCosts = new float[FeatureGroupExtensions.Count];
            if (frameIndex < 0)
                return new MotionMatchResult { FrameIndex = -1, TotalCost = 0f, GroupCosts = groupCosts };

            FeatureSchema schema = _database.Schema;
            float[] features = _database.Features;
            float[] weights = _perDimensionWeights;
            float[] values = query.Values;
            int dimension = _database.Dimension;
            int offset = frameIndex * dimension;

            for (int i = 0; i < dimension; i++)
            {
                float d = values[i] - features[offset + i];
                groupCosts[(int)schema.GetGroupOf(i)] += weights[i] * d * d;
            }

            return new MotionMatchResult
            {
                FrameIndex = frameIndex,
                TotalCost = totalCost,
                GroupCosts = groupCosts
            };
        }

        #endregion
    }
}
