using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Kinema.MotionMatching.Editor;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// <see cref="AutoTagApplier"/> writes classifier suggestions into a config through the same
    /// SerializedObject path the Tags tab edits through - so the whole accept-suggestions flow is
    /// verifiable here by reading the config back through its public API (tag vocabulary and
    /// <see cref="ClipTagTrack.MaskAt"/>), no window involved.
    /// </summary>
    public sealed class AutoTagApplierTests
    {
        private MotionMatchingConfig _config;
        private MotionMatchingDatabase _db;
        private AnimationClip _clip;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<MotionMatchingConfig>();
            _clip = new AnimationClip { name = "Walk" };
            _db = CreateDatabaseWithClip(_clip);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_db);
            Object.DestroyImmediate(_clip);
        }

        private static MotionMatchingDatabase CreateDatabaseWithClip(AnimationClip clip)
        {
            var schema = new FeatureSchema
            {
                TrajectoryTimes = new[] { 0.2f },
                BoneNames = new[] { "Foot" },
                BoneWeights = new[] { 1f }
            };
            int dim = schema.Dimension;
            var mean = new float[dim];
            var std = new float[dim];
            for (int i = 0; i < dim; i++) std[i] = 1f;

            const int frameCount = 10;
            var features = new float[frameCount * dim];
            var frames = new MotionFrameInfo[frameCount];
            for (int f = 0; f < frameCount; f++) frames[f] = new MotionFrameInfo(0, f * 0.1f);
            var clips = new[]
            {
                new MotionClipEntry { Clip = clip, Name = clip != null ? clip.name : "NoClip", StartFrame = 0, FrameCount = frameCount, Length = 1f, IsLooping = true }
            };

            var db = ScriptableObject.CreateInstance<MotionMatchingDatabase>();
            db.SetBakedData(schema, features, mean, std, frames, clips,
                FeatureWeights.Default, bakeFrameRate: 10, bakeDateUtc: "test", totalDuration: 1f);
            return db;
        }

        private static GaitClassifier.Range MakeRange(float start, float end, GaitClassifier.Gait gait, bool turning = false)
        {
            return new GaitClassifier.Range { ClipIndex = 0, StartTime = start, EndTime = end, Gait = gait, Turning = turning };
        }

        [Test]
        public void Apply_CreatesTagNamesAndRanges()
        {
            var ranges = new List<GaitClassifier.Range>
            {
                MakeRange(0f, 0.4f, GaitClassifier.Gait.Idle),
                MakeRange(0.4f, 1f, GaitClassifier.Gait.Walk)
            };

            int written = AutoTagApplier.Apply(_config, _db, ranges);

            Assert.AreEqual(2, written);
            CollectionAssert.Contains(_config.TagNames, "Idle");
            CollectionAssert.Contains(_config.TagNames, "Walk");

            ClipTagTrack track = _config.FindTagTrack(_clip);
            Assert.IsNotNull(track);
            int idleIndex = IndexOf(_config, "Idle");
            int walkIndex = IndexOf(_config, "Walk");
            Assert.AreEqual(1ul << idleIndex, track.MaskAt(0.2f));
            Assert.AreEqual(1ul << walkIndex, track.MaskAt(0.7f));
        }

        [Test]
        public void Apply_TurningRange_WritesGaitAndTurnTags()
        {
            AutoTagApplier.Apply(_config, _db, new List<GaitClassifier.Range>
            {
                MakeRange(0f, 1f, GaitClassifier.Gait.Walk, turning: true)
            });

            ClipTagTrack track = _config.FindTagTrack(_clip);
            ulong mask = track.MaskAt(0.5f);
            Assert.AreEqual((1ul << IndexOf(_config, "Walk")) | (1ul << IndexOf(_config, AutoTagApplier.TurnTagName)), mask);
        }

        [Test]
        public void Apply_Twice_ReplacesInsteadOfStacking()
        {
            var first = new List<GaitClassifier.Range> { MakeRange(0f, 1f, GaitClassifier.Gait.Run) };
            AutoTagApplier.Apply(_config, _db, first);

            var second = new List<GaitClassifier.Range> { MakeRange(0f, 1f, GaitClassifier.Gait.Walk) };
            AutoTagApplier.Apply(_config, _db, second);

            ClipTagTrack track = _config.FindTagTrack(_clip);
            Assert.AreEqual(1, track.Ranges.Count, "re-applying must replace the previous auto ranges, not stack");
            Assert.AreEqual(1ul << IndexOf(_config, "Walk"), track.MaskAt(0.5f));
        }

        [Test]
        public void Apply_ReusesExistingTagNames()
        {
            AutoTagApplier.Apply(_config, _db, new List<GaitClassifier.Range> { MakeRange(0f, 0.5f, GaitClassifier.Gait.Idle) });
            int countAfterFirst = _config.TagNames.Count;

            AutoTagApplier.Apply(_config, _db, new List<GaitClassifier.Range> { MakeRange(0f, 1f, GaitClassifier.Gait.Idle) });

            Assert.AreEqual(countAfterFirst, _config.TagNames.Count, "same gait must not duplicate its tag name");
        }

        [Test]
        public void Apply_NullClipEntries_AreSkipped()
        {
            var noClipDb = CreateDatabaseWithClip(null);
            try
            {
                int written = AutoTagApplier.Apply(_config, noClipDb, new List<GaitClassifier.Range>
                {
                    MakeRange(0f, 1f, GaitClassifier.Gait.Walk)
                });
                Assert.AreEqual(0, written);
                Assert.AreEqual(0, _config.TagTracks.Count);
            }
            finally
            {
                Object.DestroyImmediate(noClipDb);
            }
        }

        private static int IndexOf(MotionMatchingConfig config, string tagName)
        {
            for (int i = 0; i < config.TagNames.Count; i++)
                if (config.TagNames[i] == tagName) return i;
            return -1;
        }
    }
}
