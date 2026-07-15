using System.Collections.Generic;
using Kinema.MotionMatching;
using Kinema.MotionMatching.Editor;
using NUnit.Framework;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// Pins the pruner's comparison semantics. It once read the "last kept frame" through an index
    /// that belonged to the output list while dereferencing the input list - identical until the
    /// first removal, then drifting further off with every one, so pruning decisions were made
    /// against the wrong frames. These tests fail against that bug.
    /// </summary>
    public class IdlePruningTests
    {
        private static FeatureSchema Schema()
        {
            return new FeatureSchema
            {
                TrajectoryTimes = new[] { 0.2f },
                BoneNames = new[] { "Hips" },
                BoneWeights = new[] { 1f }
            };
        }

        /// <summary>One frame, all features at <paramref name="value"/>, root speed ~0 (idle).</summary>
        private static void AddIdleFrame(List<float> features, FeatureSchema schema, float value)
        {
            for (int i = 0; i < schema.Dimension; i++) features.Add(value);
            // Root velocity dims must read idle.
            features[features.Count - 1] = 0f;
            features[features.Count - 2] = 0f;
        }

        private static (List<float> features, List<byte> contacts, List<ulong> tags,
                        List<MotionFrameInfo> frames, List<MotionClipEntry> clips)
            Build(int frameCount, System.Func<int, float> valueOf)
        {
            FeatureSchema schema = Schema();
            var features = new List<float>();
            var contacts = new List<byte>();
            var tags = new List<ulong>();
            var frames = new List<MotionFrameInfo>();
            for (int f = 0; f < frameCount; f++)
            {
                AddIdleFrame(features, schema, valueOf(f));
                contacts.Add(1);
                tags.Add(0);
                frames.Add(new MotionFrameInfo(0, f / 30f));
            }
            var clips = new List<MotionClipEntry>
            {
                new MotionClipEntry { Name = "Idle", StartFrame = 0, FrameCount = frameCount, Length = frameCount / 30f }
            };
            return (features, contacts, tags, frames, clips);
        }

        [Test]
        public void IdenticalIdleFramesCollapse()
        {
            var (features, contacts, tags, frames, clips) = Build(50, _ => 0.5f);
            MotionMatchingBaker.PruneIdleDuplicates(Schema(), features, contacts, tags, frames, clips);

            // First frame plus the always-kept final frame.
            Assert.AreEqual(2, frames.Count);
            Assert.AreEqual(2, clips[0].FrameCount);
            Assert.AreEqual(features.Count, frames.Count * Schema().Dimension);
        }

        [Test]
        public void ComparisonIsAgainstLastKeptFrame_NotADriftedIndex()
        {
            // Pairs of duplicates: A A B B C C ... Each pair's second frame must go, each new value
            // must stay. With the drifted-index bug the comparison target desynchronizes after the
            // first removal and the kept count comes out wrong.
            var (features, contacts, tags, frames, clips) = Build(12, f => (f / 2) * 10f);
            MotionMatchingBaker.PruneIdleDuplicates(Schema(), features, contacts, tags, frames, clips);

            // 6 distinct values + the final anchor frame (a duplicate, kept by the duration rule).
            Assert.AreEqual(7, frames.Count);
        }

        [Test]
        public void MovingFramesAreNeverPruned()
        {
            FeatureSchema schema = Schema();
            var (features, contacts, tags, frames, clips) = Build(30, _ => 0.5f);
            // Give every frame real root speed: identical poses, but travelling.
            for (int f = 0; f < 30; f++)
            {
                int row = f * schema.Dimension;
                features[row + schema.RootVelocityOffset] = 2f;
            }
            MotionMatchingBaker.PruneIdleDuplicates(schema, features, contacts, tags, frames, clips);
            Assert.AreEqual(30, frames.Count);
        }
    }
}
