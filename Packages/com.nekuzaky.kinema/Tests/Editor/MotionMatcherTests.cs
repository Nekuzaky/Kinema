using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// End-to-end matching correctness on a synthetic database: does the Burst-parallel search
    /// actually return the nearest frame under the weighted metric, and does it respect the
    /// ignore-range and tag filters that the controller relies on for jump/continue decisions.
    /// </summary>
    public class MotionMatcherTests
    {
        private MotionMatchingDatabase _db;
        private MotionMatcher _matcher;

        [TearDown]
        public void TearDown()
        {
            _matcher?.Dispose();
            if (_db != null) Object.DestroyImmediate(_db);
        }

        [Test]
        public void Search_ReturnsTheFrameClosestToTheQuery()
        {
            _db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            _matcher = new MotionMatcher(_db, FeatureWeights.Default);

            var query = new MotionMatchingQuery(schema);
            // Frame 2 was baked with trajectory position exactly (1, 1).
            query.Values[schema.TrajectoryPositionOffset] = 1f;
            query.Values[schema.TrajectoryPositionOffset + 1] = 1f;

            MotionMatchResult result = _matcher.Search(query);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(2, result.FrameIndex);
            Assert.AreEqual(0f, result.TotalCost, 1e-3f, "an exact match should cost ~0");
        }

        [Test]
        public void Search_ExcludesTheIgnoreRange()
        {
            _db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            _matcher = new MotionMatcher(_db, FeatureWeights.Default);

            var query = new MotionMatchingQuery(schema);
            query.Values[schema.TrajectoryPositionOffset] = 1f;
            query.Values[schema.TrajectoryPositionOffset + 1] = 1f;

            // Frame 2 is the exact match; excluding it must fall back to the next best (frame 0).
            MotionMatchResult result = _matcher.Search(query, ignoreFrame: 2, ignoreRadius: 0);

            Assert.IsTrue(result.IsValid);
            Assert.AreNotEqual(2, result.FrameIndex);
        }

        [Test]
        public void Search_RequiredTags_OnlyReturnsMatchingFrames()
        {
            _db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            int dim = schema.Dimension;
            var mean = new float[dim];
            var std = new float[dim];
            for (int i = 0; i < dim; i++) std[i] = 1f;
            var features = new float[3 * dim];
            // Frame 2 (index 2) is still the closest by raw distance, but only frame 0 carries the tag.
            features[2 * dim + schema.TrajectoryPositionOffset] = 1f;
            features[2 * dim + schema.TrajectoryPositionOffset + 1] = 1f;
            var frames = new[] { new MotionFrameInfo(0, 0f), new MotionFrameInfo(0, 0.1f), new MotionFrameInfo(0, 0.2f) };
            var clips = new[] { new MotionClipEntry { Name = "C", StartFrame = 0, FrameCount = 3, Length = 0.3f, IsLooping = true } };
            ulong[] frameTags = { 1ul, 0ul, 0ul };
            _db.SetBakedData(schema, features, mean, std, frames, clips, FeatureWeights.Default, 10, "t", 0.3f,
                frameTags: frameTags, tagNames: new[] { "Required" });

            _matcher = new MotionMatcher(_db, FeatureWeights.Default);
            var query = new MotionMatchingQuery(schema);
            query.Values[schema.TrajectoryPositionOffset] = 1f;
            query.Values[schema.TrajectoryPositionOffset + 1] = 1f;

            MotionMatchResult result = _matcher.Search(query, requiredTags: 1ul);

            Assert.AreEqual(0, result.FrameIndex, "only frame 0 carries the required tag, even though frame 2 is numerically closer");
        }

        [Test]
        public void UpdateWeights_ChangesWhichFrameWins()
        {
            _db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            _matcher = new MotionMatcher(_db, FeatureWeights.Default);

            var query = new MotionMatchingQuery(schema);
            query.Values[schema.TrajectoryPositionOffset] = 1f;
            query.Values[schema.TrajectoryPositionOffset + 1] = 1f;

            // Zeroing every weight collapses all costs to 0 - the first frame scanned (0) must win ties.
            _matcher.UpdateWeights(new FeatureWeights());
            MotionMatchResult result = _matcher.Search(query);

            Assert.AreEqual(0, result.FrameIndex);
        }

        [Test]
        public void GroupCosts_SumToTheTotalCost()
        {
            _db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            _matcher = new MotionMatcher(_db, FeatureWeights.Default);

            var query = new MotionMatchingQuery(schema);
            query.Values[schema.TrajectoryPositionOffset] = 4f;
            query.Values[schema.TrajectoryPositionOffset + 1] = -2f;

            MotionMatchResult result = _matcher.Search(query);

            float sum = 0f;
            foreach (float g in result.GroupCosts) sum += g;
            Assert.AreEqual(result.TotalCost, sum, 1e-3f);
        }
    }
}
