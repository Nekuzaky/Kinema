using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// The database is a pure data container read by the matcher, the debug UI and the runtime IK -
    /// every accessor here must decode exactly what SetBakedData was given.
    /// </summary>
    public class MotionMatchingDatabaseTests
    {
        private MotionMatchingDatabase _db;

        [TearDown]
        public void TearDown()
        {
            if (_db != null) Object.DestroyImmediate(_db);
        }

        [Test]
        public void SetBakedData_ProducesAValidDatabase()
        {
            _db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);

            Assert.IsTrue(_db.IsValid);
            Assert.AreEqual(schema.Dimension, _db.Dimension);
            Assert.AreEqual(3, _db.FrameCount);
            Assert.AreEqual(1, _db.ClipCount);
        }

        [Test]
        public void NormalizeThenDenormalize_RoundTripsWithNonTrivialStatistics()
        {
            _db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            // Overwrite dimension 0's statistics via a fresh bake call to exercise a non-trivial mean/std.
            int dim = schema.Dimension;
            var mean = new float[dim];
            var std = new float[dim];
            mean[0] = 2.5f;
            std[0] = 3f;
            for (int i = 1; i < dim; i++) std[i] = 1f;
            var features = new float[3 * dim];
            var frames = new[] { new MotionFrameInfo(0, 0f) };
            var clips = new[] { new MotionClipEntry { Name = "C", StartFrame = 0, FrameCount = 1, Length = 0.1f, IsLooping = false } };
            _db.SetBakedData(schema, features, mean, std, frames, clips, FeatureWeights.Default, 10, "t", 0.1f);

            float raw = 7.25f;
            float normalized = _db.NormalizeValue(0, raw);
            float back = _db.DenormalizeValue(0, normalized);

            Assert.AreEqual(raw, back, 1e-4f);
        }

        [Test]
        public void GetFrame_And_GetClip_ReturnWhatWasBaked()
        {
            _db = TestDatabaseFactory.CreateSimple(out _);

            MotionFrameInfo frame1 = _db.GetFrame(1);
            Assert.AreEqual(0, frame1.ClipIndex);
            Assert.AreEqual(0.10f, frame1.Time, 1e-5f);

            MotionClipEntry clip = _db.GetClip(0);
            Assert.AreEqual("TestClip", clip.Name);
            Assert.AreEqual(3, clip.FrameCount);
            Assert.AreEqual(0, clip.EndFrameExclusive - clip.FrameCount);
            Assert.IsTrue(clip.ContainsFrame(2));
            Assert.IsFalse(clip.ContainsFrame(3));
        }

        [Test]
        public void MapClipTimeToFrame_RoundsAndClampsToTheClipRange()
        {
            _db = TestDatabaseFactory.CreateSimple(out _); // bakeFrameRate = 10 -> frameDt = 0.1s

            Assert.AreEqual(0, _db.MapClipTimeToFrame(0, 0.03f), "0.3 rounds down to frame 0");
            Assert.AreEqual(1, _db.MapClipTimeToFrame(0, 0.12f), "1.2 rounds to frame 1");
            Assert.AreEqual(2, _db.MapClipTimeToFrame(0, 0.27f), "would round past the clip end - clamps to the last frame");
        }

        [Test]
        public void GetTrajectory_DenormalizesThePositionThatWasBaked()
        {
            _db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            var buffer = new TrajectorySample[schema.TrajectoryPointCount];

            _db.GetTrajectory(2, buffer); // frame 2 was baked with trajectory position (1, 1)

            Assert.AreEqual(1f, buffer[0].Position.x, 1e-4f);
            Assert.AreEqual(1f, buffer[0].Position.y, 1e-4f);
        }

        [Test]
        public void Contacts_AreAbsentUntilBakedWithThem()
        {
            _db = TestDatabaseFactory.CreateSimple(out _);
            Assert.IsFalse(_db.HasContacts);
            Assert.AreEqual(0, _db.GetContacts(0));
        }

        [Test]
        public void Tags_RoundTripThroughGetFrameTagsAndGetTagMask()
        {
            _db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            var frames = new[] { new MotionFrameInfo(0, 0f), new MotionFrameInfo(0, 0.1f), new MotionFrameInfo(0, 0.2f) };
            var clips = new[] { new MotionClipEntry { Name = "C", StartFrame = 0, FrameCount = 3, Length = 0.3f, IsLooping = true } };
            int dim = schema.Dimension;
            var mean = new float[dim];
            var std = new float[dim];
            for (int i = 0; i < dim; i++) std[i] = 1f;
            var features = new float[3 * dim];
            string[] tagNames = { "Strafe", "Crouch" };
            ulong[] frameTags = { 0ul, 1ul << 1, (1ul << 0) | (1ul << 1) };

            _db.SetBakedData(schema, features, mean, std, frames, clips, FeatureWeights.Default, 10, "t", 0.3f,
                frameTags: frameTags, tagNames: tagNames);

            Assert.IsTrue(_db.HasTags);
            Assert.AreEqual(1ul << 1, _db.GetTagMask("Crouch"));
            Assert.AreEqual(0ul, _db.GetFrameTags(0));
            Assert.AreEqual((1ul << 0) | (1ul << 1), _db.GetFrameTags(2));
        }

        [Test]
        public void MirroredTwin_IsIdentityWhenDatabaseHasNoMirroredFrames()
        {
            _db = TestDatabaseFactory.CreateSimple(out _);
            Assert.IsFalse(_db.HasMirroredFrames);
            Assert.AreEqual(1, _db.GetMirroredTwin(1));
        }

        [Test]
        public void MirroredTwin_PairsFirstAndSecondHalfWhenFlagged()
        {
            _db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            int dim = schema.Dimension;
            var mean = new float[dim];
            var std = new float[dim];
            for (int i = 0; i < dim; i++) std[i] = 1f;
            var features = new float[4 * dim];
            var frames = new[]
            {
                new MotionFrameInfo(0, 0f), new MotionFrameInfo(0, 0.1f),
                new MotionFrameInfo(0, 0f, isMirrored: true), new MotionFrameInfo(0, 0.1f, isMirrored: true)
            };
            var clips = new[] { new MotionClipEntry { Name = "C", StartFrame = 0, FrameCount = 4, Length = 0.4f, IsLooping = true } };

            _db.SetBakedData(schema, features, mean, std, frames, clips, FeatureWeights.Default, 10, "t", 0.4f);

            Assert.IsTrue(_db.HasMirroredFrames);
            Assert.AreEqual(2, _db.GetMirroredTwin(0));
            Assert.AreEqual(0, _db.GetMirroredTwin(2));
        }
    }
}
