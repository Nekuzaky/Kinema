using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// <see cref="GaitClassifier"/> reads denormalized root velocities off a database and reduces
    /// them to consolidated gait ranges - all pure math, covered here on synthetic databases with
    /// hand-authored velocity tracks. Whether the default thresholds produce *useful* tags on real
    /// mocap is an authoring judgment; the classifier only ever proposes.
    /// </summary>
    public sealed class GaitClassifierTests
    {
        private const int Fps = 10;
        private MotionMatchingDatabase _db;

        [TearDown]
        public void TearDown()
        {
            if (_db != null) Object.DestroyImmediate(_db);
        }

        /// <summary>Database whose per-frame root velocity (m/s, character space) is exactly
        /// <paramref name="velocities"/> - trivial normalization (mean 0, std 1) so values pass through.</summary>
        private MotionMatchingDatabase CreateWithVelocities(Vector2[] velocities)
        {
            var schema = new FeatureSchema
            {
                TrajectoryTimes = new[] { 0.2f },
                BoneNames = new[] { "Foot" },
                BoneWeights = new[] { 1f }
            };
            int dim = schema.Dimension;
            int n = velocities.Length;

            var mean = new float[dim];
            var std = new float[dim];
            for (int i = 0; i < dim; i++) std[i] = 1f;

            var features = new float[n * dim];
            var frames = new MotionFrameInfo[n];
            for (int f = 0; f < n; f++)
            {
                frames[f] = new MotionFrameInfo(0, f / (float)Fps);
                features[f * dim + schema.RootVelocityOffset] = velocities[f].x;
                features[f * dim + schema.RootVelocityOffset + 1] = velocities[f].y;
            }

            var clips = new[]
            {
                new MotionClipEntry { Clip = null, Name = "Test", StartFrame = 0, FrameCount = n, Length = n / (float)Fps, IsLooping = false }
            };

            _db = ScriptableObject.CreateInstance<MotionMatchingDatabase>();
            _db.SetBakedData(schema, features, mean, std, frames, clips,
                FeatureWeights.Default, bakeFrameRate: Fps, bakeDateUtc: "test", totalDuration: n / (float)Fps);
            return _db;
        }

        private static Vector2[] Repeat(Vector2 velocity, int count)
        {
            var result = new Vector2[count];
            for (int i = 0; i < count; i++) result[i] = velocity;
            return result;
        }

        [Test]
        public void ClassifySpeed_UsesThresholds()
        {
            var s = GaitClassifier.Settings.Default; // idle <= 0.15, walk <= 2.5
            Assert.AreEqual(GaitClassifier.Gait.Idle, GaitClassifier.ClassifySpeed(0f, s));
            Assert.AreEqual(GaitClassifier.Gait.Idle, GaitClassifier.ClassifySpeed(0.15f, s));
            Assert.AreEqual(GaitClassifier.Gait.Walk, GaitClassifier.ClassifySpeed(1.4f, s));
            Assert.AreEqual(GaitClassifier.Gait.Run, GaitClassifier.ClassifySpeed(4f, s));
        }

        [Test]
        public void UniformWalk_YieldsOneWalkRange()
        {
            var db = CreateWithVelocities(Repeat(new Vector2(0f, 1.4f), 20));

            List<GaitClassifier.Range> ranges = GaitClassifier.Classify(db, GaitClassifier.Settings.Default);

            Assert.AreEqual(1, ranges.Count);
            Assert.AreEqual(GaitClassifier.Gait.Walk, ranges[0].Gait);
            Assert.IsFalse(ranges[0].Turning);
            Assert.AreEqual(0f, ranges[0].StartTime, 1e-4f);
            Assert.AreEqual(2f, ranges[0].EndTime, 1e-4f);
        }

        [Test]
        public void IdleThenRun_SplitsAtTheBoundary()
        {
            var velocities = new List<Vector2>();
            velocities.AddRange(Repeat(Vector2.zero, 10));               // 0.0-1.0 s idle
            velocities.AddRange(Repeat(new Vector2(0f, 4f), 10));        // 1.0-2.0 s run
            var db = CreateWithVelocities(velocities.ToArray());

            List<GaitClassifier.Range> ranges = GaitClassifier.Classify(db, GaitClassifier.Settings.Default);

            Assert.AreEqual(2, ranges.Count);
            Assert.AreEqual(GaitClassifier.Gait.Idle, ranges[0].Gait);
            Assert.AreEqual(1f, ranges[0].EndTime, 1e-4f);
            Assert.AreEqual(GaitClassifier.Gait.Run, ranges[1].Gait);
            Assert.AreEqual(1f, ranges[1].StartTime, 1e-4f);
        }

        [Test]
        public void SingleFrameFlicker_IsSmoothedAway()
        {
            var velocities = new List<Vector2>();
            velocities.AddRange(Repeat(new Vector2(0f, 1.4f), 10)); // walk
            velocities.Add(new Vector2(0f, 4f));                    // 1-frame "run" blip
            velocities.AddRange(Repeat(new Vector2(0f, 1.4f), 10)); // walk again
            var db = CreateWithVelocities(velocities.ToArray());

            List<GaitClassifier.Range> ranges = GaitClassifier.Classify(db, GaitClassifier.Settings.Default);

            Assert.AreEqual(1, ranges.Count, "a 1-frame blip below MinRangeFrames should merge into the surrounding walk");
            Assert.AreEqual(GaitClassifier.Gait.Walk, ranges[0].Gait);
        }

        [Test]
        public void SteadyDirectionChange_FlagsTurning()
        {
            // Rotate a 1.4 m/s walk velocity by 10 degrees per frame = 100 deg/s at 10 fps - well
            // above the 45 deg/s default threshold.
            var velocities = new Vector2[20];
            for (int f = 0; f < velocities.Length; f++)
            {
                float angle = f * 10f * Mathf.Deg2Rad;
                velocities[f] = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 1.4f;
            }
            var db = CreateWithVelocities(velocities);

            List<GaitClassifier.Range> ranges = GaitClassifier.Classify(db, GaitClassifier.Settings.Default);

            // Frame 0 has no previous frame so it can't be turning; everything after is one turning walk.
            Assert.IsTrue(ranges.Count >= 1);
            GaitClassifier.Range last = ranges[ranges.Count - 1];
            Assert.AreEqual(GaitClassifier.Gait.Walk, last.Gait);
            Assert.IsTrue(last.Turning, "a sustained 100 deg/s direction change should read as turning");
        }

        [Test]
        public void IdleFrames_AreNeverTurning()
        {
            // Direction flips wildly but speed is ~0: idle noise, not a turn.
            var velocities = new Vector2[12];
            for (int f = 0; f < velocities.Length; f++)
                velocities[f] = (f % 2 == 0 ? Vector2.up : Vector2.left) * 0.05f;
            var db = CreateWithVelocities(velocities);

            List<GaitClassifier.Range> ranges = GaitClassifier.Classify(db, GaitClassifier.Settings.Default);

            foreach (GaitClassifier.Range r in ranges)
            {
                Assert.AreEqual(GaitClassifier.Gait.Idle, r.Gait);
                Assert.IsFalse(r.Turning);
            }
        }
    }
}
