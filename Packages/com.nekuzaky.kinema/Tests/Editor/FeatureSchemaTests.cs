using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// The feature vector layout is the foundation everything else trusts blindly (bake, matcher,
    /// database accessors all index into it via the offsets computed here). A silent off-by-one
    /// here would corrupt every downstream read without ever throwing.
    /// </summary>
    public class FeatureSchemaTests
    {
        private static FeatureSchema MakeSchema()
        {
            return new FeatureSchema
            {
                TrajectoryTimes = new[] { -0.2f, 0.2f, 0.6f },
                BoneNames = new[] { "LeftFoot", "RightFoot", "Hips" },
                BoneWeights = new[] { 2f, 2f, 1f }
            };
        }

        [Test]
        public void Dimension_MatchesLayoutFormula()
        {
            FeatureSchema schema = MakeSchema();
            // [ TrajPos 2*T | TrajDir 2*T | BonePos 3*B | BoneVel 3*B | RootVel 2 ]
            int expected = 2 * 3 + 2 * 3 + 3 * 3 + 3 * 3 + 2;
            Assert.AreEqual(expected, schema.Dimension);
        }

        [Test]
        public void GroupOffsets_AreContiguousAndNonOverlapping()
        {
            FeatureSchema schema = MakeSchema();
            int[] offsets =
            {
                schema.TrajectoryPositionOffset, schema.TrajectoryDirectionOffset,
                schema.BonePositionOffset, schema.BoneVelocityOffset, schema.RootVelocityOffset
            };

            for (int i = 1; i < offsets.Length; i++)
                Assert.Greater(offsets[i], offsets[i - 1], "offsets must be strictly increasing");

            Assert.AreEqual(0, offsets[0], "trajectory position starts the vector");
        }

        [Test]
        public void GetGroupOf_ReturnsCorrectGroupAtEveryDimension()
        {
            FeatureSchema schema = MakeSchema();
            for (int gi = 0; gi < FeatureGroupExtensions.Count; gi++)
            {
                var group = (FeatureGroup)gi;
                int offset = schema.GetGroupOffset(group);
                int length = schema.GetGroupLength(group);
                if (length == 0) continue;

                Assert.AreEqual(group, schema.GetGroupOf(offset), $"{group} start dimension");
                Assert.AreEqual(group, schema.GetGroupOf(offset + length - 1), $"{group} last dimension");
            }
        }

        [Test]
        public void BuildPerDimensionWeights_AppliesGroupWeightToWholeGroup()
        {
            FeatureSchema schema = MakeSchema();
            var weights = new FeatureWeights { TrajectoryPosition = 3f, TrajectoryDirection = 1f, BonePosition = 1f, BoneVelocity = 1f, RootVelocity = 1f };

            float[] table = schema.BuildPerDimensionWeights(weights);

            int offset = schema.TrajectoryPositionOffset;
            int length = schema.GetGroupLength(FeatureGroup.TrajectoryPosition);
            for (int i = 0; i < length; i++)
                Assert.AreEqual(3f, table[offset + i], "every trajectory-position dimension carries the group weight");
        }

        [Test]
        public void BuildPerDimensionWeights_ScalesBoneDimensionsByPerBoneWeight()
        {
            FeatureSchema schema = MakeSchema();
            float[] table = schema.BuildPerDimensionWeights(FeatureWeights.Default);

            int posOffset = schema.BonePositionOffset;
            // Bone 0 (LeftFoot) has BoneWeights[0] = 2, bone 2 (Hips) has BoneWeights[2] = 1.
            float boneGroupWeight = FeatureWeights.Default.BonePosition;
            Assert.AreEqual(boneGroupWeight * 2f, table[posOffset + 0 * 3], 1e-5f, "LeftFoot x scaled by its bone weight");
            Assert.AreEqual(boneGroupWeight * 1f, table[posOffset + 2 * 3], 1e-5f, "Hips x scaled by its bone weight");
        }

        [Test]
        public void GetBoneWeight_DefaultsToOneWhenMissingOrOutOfRange()
        {
            var schema = new FeatureSchema { BoneNames = new[] { "Foot" }, BoneWeights = null };
            Assert.AreEqual(1f, schema.GetBoneWeight(0));
            Assert.AreEqual(1f, schema.GetBoneWeight(5));
        }

        [Test]
        public void Clone_ProducesAnIndependentCopy()
        {
            FeatureSchema schema = MakeSchema();
            FeatureSchema clone = schema.Clone();

            clone.TrajectoryTimes[0] = 99f;

            Assert.AreNotEqual(clone.TrajectoryTimes[0], schema.TrajectoryTimes[0], "mutating the clone must not affect the original");
            Assert.AreEqual(schema.Dimension, clone.Dimension);
        }

        [Test]
        public void IsLayoutCompatibleWith_ComparesPointAndBoneCountsOnly()
        {
            FeatureSchema a = MakeSchema();
            var b = new FeatureSchema { TrajectoryTimes = new[] { -0.5f, 0.5f, 1.5f }, BoneNames = new[] { "A", "B", "C" } };
            var c = new FeatureSchema { TrajectoryTimes = new[] { 0.2f }, BoneNames = new[] { "A", "B", "C" } };

            Assert.IsTrue(a.IsLayoutCompatibleWith(b), "same point/bone counts, different values -> compatible");
            Assert.IsFalse(a.IsLayoutCompatibleWith(c), "different trajectory point count -> incompatible");
            Assert.IsFalse(a.IsLayoutCompatibleWith(null));
        }
    }
}
