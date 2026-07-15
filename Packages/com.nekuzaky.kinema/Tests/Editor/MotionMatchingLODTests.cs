using NUnit.Framework;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// <see cref="MotionMatchingLOD.EvaluateMultiplier"/> is the only part of the LOD system whose
    /// correctness can be judged without a running scene - it is pure distance-to-multiplier math.
    /// Whether the resulting cadence still reads as acceptable on screen is not something these tests
    /// can decide; that is tracked as an open item in TODO.md.
    /// </summary>
    public sealed class MotionMatchingLODTests
    {
        private static readonly float[] Tiers = { 10f, 25f, 50f };
        private static readonly float[] Multipliers = { 1f, 2f, 4f };

        [Test]
        public void BelowFirstTier_ReturnsFirstMultiplier()
        {
            Assert.AreEqual(1f, MotionMatchingLOD.EvaluateMultiplier(0f, Tiers, Multipliers));
            Assert.AreEqual(1f, MotionMatchingLOD.EvaluateMultiplier(10f, Tiers, Multipliers));
        }

        [Test]
        public void AtOrAboveLastTier_ReturnsLastMultiplier()
        {
            Assert.AreEqual(4f, MotionMatchingLOD.EvaluateMultiplier(50f, Tiers, Multipliers));
            Assert.AreEqual(4f, MotionMatchingLOD.EvaluateMultiplier(500f, Tiers, Multipliers));
        }

        [Test]
        public void BetweenTiers_InterpolatesLinearly()
        {
            // Midpoint between 10 (x1) and 25 (x2) -> x1.5
            float mid = MotionMatchingLOD.EvaluateMultiplier(17.5f, Tiers, Multipliers);
            Assert.AreEqual(1.5f, mid, 1e-4f);

            // Midpoint between 25 (x2) and 50 (x4) -> x3
            float mid2 = MotionMatchingLOD.EvaluateMultiplier(37.5f, Tiers, Multipliers);
            Assert.AreEqual(3f, mid2, 1e-4f);
        }

        [Test]
        public void ExactTierBoundary_ReturnsThatTiersMultiplier()
        {
            Assert.AreEqual(2f, MotionMatchingLOD.EvaluateMultiplier(25f, Tiers, Multipliers), 1e-4f);
        }

        [TestCase(null, new float[] { 1f })]
        [TestCase(new float[0], new float[0])]
        public void MismatchedOrEmptyInput_FallsBackToNoDegradation(float[] tiers, float[] multipliers)
        {
            Assert.AreEqual(1f, MotionMatchingLOD.EvaluateMultiplier(100f, tiers, multipliers));
        }

        [Test]
        public void MismatchedLengths_FallsBackToNoDegradation()
        {
            Assert.AreEqual(1f, MotionMatchingLOD.EvaluateMultiplier(100f, Tiers, new float[] { 1f, 2f }));
        }

        [Test]
        public void SingleTier_ActsAsConstantBeyondIt()
        {
            var singleTier = new float[] { 20f };
            var singleMultiplier = new float[] { 3f };
            Assert.AreEqual(3f, MotionMatchingLOD.EvaluateMultiplier(0f, singleTier, singleMultiplier));
            Assert.AreEqual(3f, MotionMatchingLOD.EvaluateMultiplier(20f, singleTier, singleMultiplier));
            Assert.AreEqual(3f, MotionMatchingLOD.EvaluateMultiplier(1000f, singleTier, singleMultiplier));
        }
    }
}
