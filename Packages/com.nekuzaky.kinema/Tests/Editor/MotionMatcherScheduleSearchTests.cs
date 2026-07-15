using NUnit.Framework;
using Unity.Jobs;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// <see cref="MotionMatcher.ScheduleSearch"/>/<see cref="MotionMatcher.CompleteSearch"/> exist so
    /// several matchers can have their Burst jobs scheduled together and completed together, instead
    /// of each blocking the main thread in series the way <see cref="MotionMatcher.Search"/> does -
    /// addresses the "many characters searching concurrently" profiling gap in TODO.md. Correctness
    /// is what these tests can verify without a scene: does the non-blocking path return exactly what
    /// <see cref="MotionMatcher.Search"/> would for the same query, including when several matchers'
    /// jobs are actually in flight at once. Whether batching is worth adopting in the controller (a
    /// real architecture change, not attempted here) is a profiling/design question, not a
    /// correctness one.
    /// </summary>
    public sealed class MotionMatcherScheduleSearchTests
    {
        [Test]
        public void ScheduleThenComplete_MatchesSynchronousSearch()
        {
            var db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            try
            {
                using var matcher = new MotionMatcher(db, FeatureWeights.Default);
                var query = new MotionMatchingQuery(schema);
                query.Values[schema.TrajectoryPositionOffset] = 1f;
                query.Values[schema.TrajectoryPositionOffset + 1] = 1f;

                MotionMatchResult expected = matcher.Search(query);

                JobHandle handle = matcher.ScheduleSearch(query);
                MotionMatchResult actual = matcher.CompleteSearch(handle, query);

                Assert.AreEqual(expected.FrameIndex, actual.FrameIndex);
                Assert.AreEqual(expected.TotalCost, actual.TotalCost, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(db);
            }
        }

        [Test]
        public void TwoMatchers_ScheduledTogether_BothReturnCorrectResults()
        {
            // Simulates the batching pattern: schedule N matchers' jobs before completing any of
            // them, so their Burst chunks can genuinely run concurrently.
            var dbA = TestDatabaseFactory.CreateSimple(out FeatureSchema schemaA);
            var dbB = TestDatabaseFactory.CreateSimple(out FeatureSchema schemaB);
            try
            {
                using var matcherA = new MotionMatcher(dbA, FeatureWeights.Default);
                using var matcherB = new MotionMatcher(dbB, FeatureWeights.Default);

                var queryA = new MotionMatchingQuery(schemaA);
                queryA.Values[schemaA.TrajectoryPositionOffset] = 1f;
                queryA.Values[schemaA.TrajectoryPositionOffset + 1] = 1f;

                var queryB = new MotionMatchingQuery(schemaB);
                queryB.Values[schemaB.TrajectoryPositionOffset] = 0f;
                queryB.Values[schemaB.TrajectoryPositionOffset + 1] = 0f;

                JobHandle handleA = matcherA.ScheduleSearch(queryA);
                JobHandle handleB = matcherB.ScheduleSearch(queryB);

                // Neither completed yet - both jobs may be in flight simultaneously here.
                MotionMatchResult resultA = matcherA.CompleteSearch(handleA, queryA);
                MotionMatchResult resultB = matcherB.CompleteSearch(handleB, queryB);

                Assert.IsTrue(resultA.IsValid);
                Assert.IsTrue(resultB.IsValid);
                Assert.AreEqual(2, resultA.FrameIndex, "frame 2 was baked at trajectory position (1,1)");
                Assert.AreEqual(matcherB.Search(queryB).FrameIndex, resultB.FrameIndex,
                    "batched result should match what an independent synchronous Search would return");
            }
            finally
            {
                Object.DestroyImmediate(dbA);
                Object.DestroyImmediate(dbB);
            }
        }

        [Test]
        public void ScheduleSearch_RespectsIgnoreRangeAndTags_LikeSearch()
        {
            var db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            try
            {
                using var matcher = new MotionMatcher(db, FeatureWeights.Default);
                var query = new MotionMatchingQuery(schema);
                query.Values[schema.TrajectoryPositionOffset] = 1f;
                query.Values[schema.TrajectoryPositionOffset + 1] = 1f;

                JobHandle handle = matcher.ScheduleSearch(query, ignoreFrame: 2, ignoreRadius: 0);
                MotionMatchResult result = matcher.CompleteSearch(handle, query);

                Assert.IsTrue(result.IsValid);
                Assert.AreNotEqual(2, result.FrameIndex, "the ignored frame should be excluded exactly as Search does");
            }
            finally
            {
                Object.DestroyImmediate(db);
            }
        }
    }
}
