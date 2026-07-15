using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// The live pose query has one correctness contract: a skeleton standing exactly on a baked
    /// frame must produce that frame's pose features. If it does, the query and the candidates live
    /// in the same space and pose costs are meaningful; if it does not, every search is comparing
    /// apples to a different coordinate system.
    /// </summary>
    public sealed class MotionMatchingQueryTests
    {
        [Test]
        public void SetPoseFromSkeleton_MatchesFrameRow_WhenSkeletonSitsOnThatFrame()
        {
            MotionMatchingDatabase db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            var query = new MotionMatchingQuery(schema);

            // Character at origin facing +Z: character space == world space, so the frame's local
            // features can be fed back as world data directly.
            var space = new CharacterSpace(Vector3.zero, Vector3.forward);
            int frame = 0;

            int posOffset = schema.BonePositionOffset;
            int velOffset = schema.BoneVelocityOffset;
            int rootOffset = schema.RootVelocityOffset;

            var bonePositions = new Vector3[schema.BoneCount];
            var boneVelocities = new Vector3[schema.BoneCount];
            for (int b = 0; b < schema.BoneCount; b++)
            {
                bonePositions[b] = new Vector3(
                    db.DenormalizeValue(posOffset + b * 3, db.Features[posOffset + b * 3]),
                    db.DenormalizeValue(posOffset + b * 3 + 1, db.Features[posOffset + b * 3 + 1]),
                    db.DenormalizeValue(posOffset + b * 3 + 2, db.Features[posOffset + b * 3 + 2]));
                boneVelocities[b] = new Vector3(
                    db.DenormalizeValue(velOffset + b * 3, db.Features[velOffset + b * 3]),
                    db.DenormalizeValue(velOffset + b * 3 + 1, db.Features[velOffset + b * 3 + 1]),
                    db.DenormalizeValue(velOffset + b * 3 + 2, db.Features[velOffset + b * 3 + 2]));
            }
            // Local (x = right, y = forward) -> world with forward = +Z.
            var rootVelocity = new Vector3(
                db.DenormalizeValue(rootOffset, db.Features[rootOffset]),
                0f,
                db.DenormalizeValue(rootOffset + 1, db.Features[rootOffset + 1]));

            query.SetPoseFromSkeleton(db, space, bonePositions, boneVelocities, rootVelocity);

            for (int i = schema.BonePositionOffset; i < schema.Dimension; i++)
                Assert.That(query.Values[i], Is.EqualTo(db.Features[frame * schema.Dimension + i]).Within(1e-4f),
                    $"dimension {i} diverged from the baked row");
        }

        [Test]
        public void SetPoseFromSkeleton_RespectsCharacterSpace()
        {
            MotionMatchingDatabase db = TestDatabaseFactory.CreateSimple(out FeatureSchema schema);
            var query = new MotionMatchingQuery(schema);

            // Character rotated 90° (facing +X): a world velocity along +X is "forward" locally.
            var space = new CharacterSpace(Vector3.zero, Vector3.right);
            var positions = new Vector3[schema.BoneCount];
            var velocities = new Vector3[schema.BoneCount];

            query.SetPoseFromSkeleton(db, space, positions, velocities, new Vector3(2f, 0f, 0f));

            int r = schema.RootVelocityOffset;
            Assert.That(db.DenormalizeValue(r, query.Values[r]), Is.EqualTo(0f).Within(1e-4f), "sideways component");
            Assert.That(db.DenormalizeValue(r + 1, query.Values[r + 1]), Is.EqualTo(2f).Within(1e-4f), "forward component");
        }
    }
}
