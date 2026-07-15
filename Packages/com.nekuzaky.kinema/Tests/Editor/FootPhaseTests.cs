using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// The foot-phase term has three contracts: circular distance (phase 0.9 is close to 0.1),
    /// inertness when no phase data exists, and actually steering the search toward candidates at
    /// the same point of the step cycle.
    /// </summary>
    public sealed class FootPhaseTests
    {
        private static MotionMatchingDatabase CreatePhasedDatabase(out FeatureSchema schema)
        {
            // Identical features on every frame: only the phase term can distinguish candidates.
            MotionMatchingDatabase db = TestDatabaseFactory.Create(10, new[] { 10 });
            schema = db.Schema;

            var phases = new float[10];
            for (int f = 0; f < 10; f++) phases[f] = f / 10f;

            db.SetBakedData(schema, new float[10 * schema.Dimension], new float[schema.Dimension],
                Ones(schema.Dimension), Frames(10), Clips(10),
                FeatureWeights.Default, 10, "test", 1f, footPhases: phases);
            return db;
        }

        private static float[] Ones(int n) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = 1f; return a; }
        private static MotionFrameInfo[] Frames(int n) { var a = new MotionFrameInfo[n]; for (int i = 0; i < n; i++) a[i] = new MotionFrameInfo(0, i * 0.1f); return a; }
        private static MotionClipEntry[] Clips(int n) => new[] { new MotionClipEntry { Name = "C", StartFrame = 0, FrameCount = n, Length = n * 0.1f, IsLooping = true } };

        [Test]
        public void Search_PrefersCandidatesAtTheSamePhase()
        {
            MotionMatchingDatabase db = CreatePhasedDatabase(out FeatureSchema schema);
            var weights = FeatureWeights.Default;
            weights.FootPhase = 1f;

            using var matcher = new MotionMatcher(db, weights);
            var query = new MotionMatchingQuery(schema) { FootPhase = 0.62f };

            MotionMatchResult result = matcher.Search(query);
            Assert.AreEqual(6, result.FrameIndex, "frame 6 (phase 0.6) is the closest point of the cycle");
        }

        [Test]
        public void PhaseDistance_IsCircular()
        {
            MotionMatchingDatabase db = CreatePhasedDatabase(out FeatureSchema schema);
            var weights = FeatureWeights.Default;
            weights.FootPhase = 1f;

            using var matcher = new MotionMatcher(db, weights);
            var query = new MotionMatchingQuery(schema) { FootPhase = 0.98f };

            // 0.98 is closer to phase 0.0 (distance 0.02 around the circle) than to 0.9.
            MotionMatchResult result = matcher.Search(query);
            Assert.AreEqual(0, result.FrameIndex, "the cycle wraps: phase 0.98 should land on frame 0");
        }

        [Test]
        public void PhaseTerm_IsInert_WithoutData_OrWeight_OrQueryPhase()
        {
            // No phases baked -> term contributes nothing even with weight and query phase set.
            MotionMatchingDatabase bare = TestDatabaseFactory.Create(5, new[] { 5 });
            var weights = FeatureWeights.Default;
            weights.FootPhase = 5f;
            using (var matcher = new MotionMatcher(bare, weights))
            {
                var query = new MotionMatchingQuery(bare.Schema) { FootPhase = 0.5f };
                Assert.AreEqual(0f, matcher.EvaluateCost(query, 3), 1e-6f, "no baked phases");
            }

            // Phases baked but query phase unknown -> also inert.
            MotionMatchingDatabase phased = CreatePhasedDatabase(out FeatureSchema schema);
            using (var matcher = new MotionMatcher(phased, weights))
            {
                var query = new MotionMatchingQuery(schema) { FootPhase = -1f };
                Assert.AreEqual(0f, matcher.EvaluateCost(query, 3), 1e-6f, "unknown query phase");
            }
        }
    }
}
