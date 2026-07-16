using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// The normalized feature vector searched against the database each tick, split into the two
    /// halves motion matching cares about:
    ///   - the trajectory half, built from the desired locomotion (where we want to go),
    ///   - the pose half: either sampled from the skeleton actually on screen (the honest option -
    ///     after inertialization, stride warping and IK the rendered pose is not the database frame
    ///     any more), or copied from the currently playing frame as a fallback when the schema's
    ///     bones cannot be resolved on the rig.
    /// Both paths normalize with the database statistics, so query and candidates always live in
    /// the same normalized space.
    /// </summary>
    public sealed class MotionMatchingQuery
    {
        #region Public

        /// <summary>Normalized query values, one per feature dimension.</summary>
        public float[] Values { get; private set; }

        /// <summary>Desired trajectory in character space, kept for debug drawing.</summary>
        public TrajectorySample[] DesiredTrajectory { get; private set; }

        /// <summary>
        /// Gait phase of the character right now (0..1), or -1 when unknown. Set from the current
        /// frame's baked phase - the phase advances with the clock, so the playing frame is the
        /// authoritative phase state. Used by the matcher's foot-phase cost term.
        /// </summary>
        public float FootPhase { get; set; } = -1f;

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
        /// Builds the pose half from the skeleton actually rendered: bone positions and velocities
        /// in character space, plus the measured root velocity - the same math the baker used, so
        /// the values are directly comparable to the candidates once normalized. This is what makes
        /// the pose cost honest: it measures distance from the pose on screen, not from the frame
        /// the clock happens to be on.
        /// </summary>
        public void SetPoseFromSkeleton(MotionMatchingDatabase database, CharacterSpace space,
            Vector3[] boneWorldPositions, Vector3[] boneWorldVelocities, Vector3 rootWorldVelocity)
        {
            FeatureSchema schema = database.Schema;
            int posOffset = schema.BonePositionOffset;
            int velOffset = schema.BoneVelocityOffset;

            bool naive = schema.PoseMode == PoseCostMode.Naive;

            for (int b = 0; b < schema.BoneCount; b++)
            {
                Vector3 localPos = space.ToLocalOffset3D(boneWorldPositions[b]);
                Vector3 localVel = space.ToLocalVector3D(boneWorldVelocities[b]);

                // Same call the baker makes. The database's normalization was fitted to whatever this
                // returns, so computing it any other way here would normalize live numbers against
                // statistics gathered from different ones.
                Vector3 pose = schema.BonePoseValue(localPos, localVel);

                int p = posOffset + b * 3;
                Values[p] = database.NormalizeValue(p, pose.x);
                Values[p + 1] = database.NormalizeValue(p + 1, pose.y);
                Values[p + 2] = database.NormalizeValue(p + 2, pose.z);

                if (!naive) continue;

                int v = velOffset + b * 3;
                Values[v] = database.NormalizeValue(v, localVel.x);
                Values[v + 1] = database.NormalizeValue(v + 1, localVel.y);
                Values[v + 2] = database.NormalizeValue(v + 2, localVel.z);
            }

            Vector2 localRootVel = space.ToLocalDirection(rootWorldVelocity);
            int r = schema.RootVelocityOffset;
            Values[r] = database.NormalizeValue(r, localRootVel.x);
            Values[r + 1] = database.NormalizeValue(r + 1, localRootVel.y);
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
