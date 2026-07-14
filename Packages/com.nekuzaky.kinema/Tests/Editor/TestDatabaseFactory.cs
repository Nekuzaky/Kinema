using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// Builds small, fully synthetic <see cref="MotionMatchingDatabase"/> instances directly via
    /// <see cref="MotionMatchingDatabase.SetBakedData"/>, bypassing the offline bake pipeline (which
    /// needs a real rig and clips) so database and matcher logic can be unit tested in isolation.
    /// </summary>
    internal static class TestDatabaseFactory
    {
        /// <summary>1 trajectory point, 1 bone -> 12 dimensions. Three frames with distinct trajectory positions.</summary>
        public static MotionMatchingDatabase CreateSimple(out FeatureSchema schema)
        {
            schema = new FeatureSchema
            {
                TrajectoryTimes = new[] { 0.2f },
                BoneNames = new[] { "Foot" },
                BoneWeights = new[] { 1f }
            };
            int dim = schema.Dimension; // 12

            // Trivial normalization (mean 0, std 1) so raw values pass through unchanged - keeps
            // the matcher tests' expected nearest-neighbour obvious to reason about.
            var mean = new float[dim];
            var std = new float[dim];
            for (int i = 0; i < dim; i++) std[i] = 1f;

            var features = new float[3 * dim];
            // Frame 0: trajectory position (0, 0) - "far" from a query near (1, 1).
            // Frame 1: trajectory position (5, 5) - "farther still".
            // Frame 2: trajectory position (1, 1) - exact match for the query used in matcher tests.
            features[0 * dim + schema.TrajectoryPositionOffset] = 0f;
            features[0 * dim + schema.TrajectoryPositionOffset + 1] = 0f;
            features[1 * dim + schema.TrajectoryPositionOffset] = 5f;
            features[1 * dim + schema.TrajectoryPositionOffset + 1] = 5f;
            features[2 * dim + schema.TrajectoryPositionOffset] = 1f;
            features[2 * dim + schema.TrajectoryPositionOffset + 1] = 1f;

            var frames = new[]
            {
                new MotionFrameInfo(0, 0.00f),
                new MotionFrameInfo(0, 0.10f),
                new MotionFrameInfo(0, 0.20f)
            };
            var clips = new[]
            {
                new MotionClipEntry { Clip = null, Name = "TestClip", StartFrame = 0, FrameCount = 3, Length = 0.3f, IsLooping = true }
            };

            var db = ScriptableObject.CreateInstance<MotionMatchingDatabase>();
            db.SetBakedData(schema, features, mean, std, frames, clips,
                FeatureWeights.Default, bakeFrameRate: 10, bakeDateUtc: "test", totalDuration: 0.3f);
            return db;
        }
    }
}
