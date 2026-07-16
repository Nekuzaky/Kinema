using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// Pins <see cref="PoseCostMode.InertializationCost"/>: the layout it produces, the constant it
    /// derives, and the one behaviour that is the entire reason it exists.
    ///
    /// This is the layer everything indexes into blindly, and the two modes write different numbers
    /// into the same slots. Every failure mode here is silent - nothing throws, the character just
    /// matches against a yardstick it was not baked with.
    /// </summary>
    public class PoseCostModeTests
    {
        private const int T = 3;
        private const int B = 3;

        private static FeatureSchema MakeSchema(PoseCostMode mode, float halflife = 0.15f)
        {
            return new FeatureSchema
            {
                TrajectoryTimes = new[] { -0.2f, 0.2f, 0.6f },
                BoneNames = new[] { "LeftFoot", "RightFoot", "Hips" },
                BoneWeights = new[] { 2f, 2f, 1f },
                PoseMode = mode,
                InertializationHalflife = halflife
            };
        }

        #region Layout

        [Test]
        public void Composite_Dimension_DropsTheVelocityBlock()
        {
            Assert.AreEqual(4 * T + 6 * B + 2, MakeSchema(PoseCostMode.Naive).Dimension);
            Assert.AreEqual(4 * T + 3 * B + 2, MakeSchema(PoseCostMode.InertializationCost).Dimension);
        }

        [Test]
        public void Composite_BoneVelocityGroup_IsEmptyAndRootFollowsBonePosition()
        {
            FeatureSchema schema = MakeSchema(PoseCostMode.InertializationCost);

            Assert.AreEqual(0, schema.GetGroupLength(FeatureGroup.BoneVelocity));
            Assert.AreEqual(3 * B, schema.GetGroupLength(FeatureGroup.BonePosition));
            Assert.AreEqual(schema.BonePositionOffset + 3 * B, schema.RootVelocityOffset,
                "with no velocity block, root velocity must sit straight after the pose composite");
        }

        [Test]
        public void Composite_EveryDimensionStillResolvesToAGroup()
        {
            FeatureSchema schema = MakeSchema(PoseCostMode.InertializationCost);

            // An empty group must not swallow a dimension: the BoneVelocity range is zero-width, so
            // every index has to land in a real group or the weights get applied to the wrong slots.
            for (int d = 0; d < schema.Dimension; d++)
                Assert.AreNotEqual(FeatureGroup.BoneVelocity, schema.GetGroupOf(d),
                    $"dimension {d} resolved to the empty velocity group");
        }

        [Test]
        public void Composite_PerDimensionWeights_CoverExactlyTheVector()
        {
            FeatureSchema schema = MakeSchema(PoseCostMode.InertializationCost);
            float[] w = schema.BuildPerDimensionWeights(FeatureWeights.Default);

            Assert.AreEqual(schema.Dimension, w.Length);
            foreach (float v in w) Assert.GreaterOrEqual(v, 0f);
        }

        #endregion

        #region The constant

        [Test]
        public void HalfDamping_MatchesHoldensSpringFormula()
        {
            // Holden, "Spring-It-On": damping = 4*ln2/halflife, and y = damping/2.
            const float halflife = 0.15f;
            float expected = 4f * 0.69314718056f / halflife / 2f;

            Assert.AreEqual(expected, MakeSchema(PoseCostMode.InertializationCost, halflife).HalfDamping, 1e-4f);
        }

        [Test]
        public void HalfDamping_DoesNotDivideByZero()
        {
            Assert.IsFalse(float.IsInfinity(MakeSchema(PoseCostMode.InertializationCost, 0f).HalfDamping));
        }

        #endregion

        #region The behaviour it exists for

        [Test]
        public void Naive_PoseValue_IsThePositionUntouched()
        {
            FeatureSchema schema = MakeSchema(PoseCostMode.Naive);
            var pos = new Vector3(0.1f, -0.2f, 0.3f);

            Assert.AreEqual(pos, schema.BonePoseValue(pos, new Vector3(9f, 9f, 9f)),
                "in Naive the velocity lives in its own block and must not leak into the position");
        }

        [Test]
        public void Composite_PoseValue_MatchesTheFormula()
        {
            FeatureSchema schema = MakeSchema(PoseCostMode.InertializationCost);
            var pos = new Vector3(0.1f, -0.2f, 0.3f);
            var vel = new Vector3(1f, 2f, -3f);
            float y = schema.HalfDamping;

            Vector3 expected = 2f * pos / y + vel / (y * y);
            Vector3 actual = schema.BonePoseValue(pos, vel);

            Assert.AreEqual(expected.x, actual.x, 1e-5f);
            Assert.AreEqual(expected.y, actual.y, 1e-5f);
            Assert.AreEqual(expected.z, actual.z, 1e-5f);
        }

        /// <summary>
        /// The whole point, and the thing the naive layout cannot express: a position offset the
        /// spring will absorb because the velocity offset is set to cancel it should cost the same as
        /// no offset at all. Two states a spring settles identically must read identically.
        /// </summary>
        [Test]
        public void Composite_StatesTheSpringResolvesAlike_ReadAlike()
        {
            FeatureSchema schema = MakeSchema(PoseCostMode.InertializationCost);
            float y = schema.HalfDamping;

            // 2p/y + v/y^2: offsetting position by p and velocity by -2*p*y leaves the total at zero.
            var p = new Vector3(0.1f, 0f, 0f);
            Vector3 v = -2f * p * y;

            Vector3 offset = schema.BonePoseValue(p, v);
            Vector3 settled = schema.BonePoseValue(Vector3.zero, Vector3.zero);

            Assert.AreEqual(settled.x, offset.x, 1e-4f,
                "a position offset cancelled by its velocity offset must be free");

            // And the naive layout, for contrast, calls the same pair a real difference - which is
            // exactly the transition it would wrongly reject.
            FeatureSchema naive = MakeSchema(PoseCostMode.Naive);
            float naiveGap = Mathf.Abs(naive.BonePoseValue(p, v).x - naive.BonePoseValue(Vector3.zero, Vector3.zero).x);
            Assert.Greater(naiveGap, 0.05f,
                "the naive pose value sees only the position offset, so it reads this as a real difference");
        }

        #endregion

        #region Compatibility

        [Test]
        public void LayoutCompatibility_IsFalseAcrossModes()
        {
            // The trap: identical counts, identical dimension arithmetic in the head, different
            // meaning per slot. Nothing downstream would throw.
            FeatureSchema naive = MakeSchema(PoseCostMode.Naive);
            FeatureSchema composite = MakeSchema(PoseCostMode.InertializationCost);

            Assert.IsFalse(naive.IsLayoutCompatibleWith(composite));
            Assert.IsFalse(composite.IsLayoutCompatibleWith(naive));
        }

        [Test]
        public void LayoutCompatibility_IsFalseAcrossHalflives()
        {
            // Same layout, but every baked number was scaled by a different y.
            FeatureSchema a = MakeSchema(PoseCostMode.InertializationCost, 0.15f);
            FeatureSchema b = MakeSchema(PoseCostMode.InertializationCost, 0.3f);

            Assert.IsFalse(a.IsLayoutCompatibleWith(b));
        }

        [Test]
        public void Clone_CarriesTheModeAndHalflife()
        {
            FeatureSchema schema = MakeSchema(PoseCostMode.InertializationCost, 0.22f);
            FeatureSchema clone = schema.Clone();

            Assert.AreEqual(schema.PoseMode, clone.PoseMode);
            Assert.AreEqual(schema.InertializationHalflife, clone.InertializationHalflife, 1e-6f);
            Assert.IsTrue(schema.IsLayoutCompatibleWith(clone));
        }

        #endregion
    }
}
