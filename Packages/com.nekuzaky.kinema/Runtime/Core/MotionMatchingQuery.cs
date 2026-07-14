using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// The normalized feature vector searched against the database each tick, split into the two
    /// halves motion matching cares about:
    ///   - the trajectory half, built from the desired locomotion (where we want to go),
    ///   - the pose half, copied straight from the currently playing frame (where the body is now).
    /// Copying the pose half from the database guarantees the query and the candidates live in the
    /// exact same normalized space, which keeps pose costs meaningful and avoids a live pose sampler.
    /// </summary>
    public sealed class MotionMatchingQuery
    {
        #region Public

        /// <summary>Normalized query values, one per feature dimension.</summary>
        public float[] Values { get; private set; }

        /// <summary>Desired trajectory in character space, kept for debug drawing.</summary>
        public TrajectorySample[] DesiredTrajectory { get; private set; }

        public MotionMatchingQuery(FeatureSchema schema)
        {
            Values = new float[schema.Dimension];
            DesiredTrajectory = new TrajectorySample[schema.TrajectoryPointCount];
        }

        #endregion

        #region Main API

        /// <summary>
        /// Writes the desired trajectory into the trajectory half of the query, normalized with the
        /// database statistics so it is comparable to the baked candidates.
        /// </summary>
        public void SetTrajectory(MotionMatchingDatabase database, TrajectorySample[] desired)
        {
            FeatureSchema schema = database.Schema;
            int points = schema.TrajectoryPointCount;
            int posOffset = schema.TrajectoryPositionOffset;
            int dirOffset = schema.TrajectoryDirectionOffset;

            for (int p = 0; p < points; p++)
            {
                TrajectorySample s = desired[p];
                DesiredTrajectory[p] = s;

                int px = posOffset + p * 2;
                int dx = dirOffset + p * 2;
                Values[px] = database.NormalizeValue(px, s.Position.x);
                Values[px + 1] = database.NormalizeValue(px + 1, s.Position.y);
                Values[dx] = database.NormalizeValue(dx, s.Direction.x);
                Values[dx + 1] = database.NormalizeValue(dx + 1, s.Direction.y);
            }
        }

        /// <summary>
        /// Copies the pose half (bone position, bone velocity, root velocity) from a baked frame.
        /// The values are already normalized in the database, so this is a straight memory copy.
        /// </summary>
        public void SetPoseFromFrame(MotionMatchingDatabase database, int frameIndex)
        {
            int poseStart = database.Schema.BonePositionOffset;
            int dimension = database.Dimension;
            int rowOffset = database.GetFeatureOffset(frameIndex);
            float[] features = database.Features;

            for (int i = poseStart; i < dimension; i++)
                Values[i] = features[rowOffset + i];
        }

        #endregion
    }
}
