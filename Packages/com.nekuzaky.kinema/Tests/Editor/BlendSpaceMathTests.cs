using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// <see cref="BlendSpaceMath"/> is pure math (no rig, no AnimationClip, no Editor), so it's fully
    /// covered here. What isn't covered - whether a linear blend of already-extracted feature rows
    /// reads as a correct blended pose on screen - is a judgment call flagged in TODO.md, same as
    /// every other matching-feel change in this project.
    /// </summary>
    public sealed class BlendSpaceMathTests
    {
        [Test]
        public void ComputeWeights_AtASamplePosition_IsOneHot()
        {
            var samples = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) };
            float[] w = BlendSpaceMath.ComputeWeights(new Vector2(1, 0), samples);

            Assert.AreEqual(0f, w[0], 1e-4f);
            Assert.AreEqual(1f, w[1], 1e-4f);
            Assert.AreEqual(0f, w[2], 1e-4f);
        }

        [Test]
        public void ComputeWeights_AlwaysSumsToOne()
        {
            var samples = new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(0, -1) };
            foreach (Vector2 query in new[] { new Vector2(0, 0), new Vector2(0.3f, 0.7f), new Vector2(5f, -3f), new Vector2(0, 0.5f) })
            {
                float[] w = BlendSpaceMath.ComputeWeights(query, samples);
                float sum = 0f;
                foreach (float v in w) sum += v;
                Assert.AreEqual(1f, sum, 1e-4f, $"weights should sum to 1 for query {query}");
            }
        }

        [Test]
        public void ComputeWeights_Midpoint_SplitsEvenlyBetweenTwoSamples()
        {
            var samples = new[] { new Vector2(0, 0), new Vector2(2, 0) };
            float[] w = BlendSpaceMath.ComputeWeights(new Vector2(1, 0), samples);

            Assert.AreEqual(0.5f, w[0], 1e-4f);
            Assert.AreEqual(0.5f, w[1], 1e-4f);
        }

        [Test]
        public void ComputeWeights_SingleSample_AlwaysReturnsOne()
        {
            var samples = new[] { new Vector2(3, 4) };
            float[] w = BlendSpaceMath.ComputeWeights(new Vector2(0, 0), samples);
            Assert.AreEqual(1, w.Length);
            Assert.AreEqual(1f, w[0], 1e-4f);
        }

        [Test]
        public void ComputeWeights_NeverNegative()
        {
            var samples = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1), new Vector2(-0.5f, 1) };
            float[] w = BlendSpaceMath.ComputeWeights(new Vector2(10, 10), samples);
            foreach (float v in w) Assert.GreaterOrEqual(v, 0f);
        }

        [Test]
        public void BlendFrame_WeightedSumOfRows()
        {
            var rows = new[] { new float[] { 1f, 0f, 2f }, new float[] { 0f, 4f, 0f } };
            var weights = new float[] { 0.25f, 0.75f };
            var dest = new float[3];

            BlendSpaceMath.BlendFrame(dest, rows, weights);

            Assert.AreEqual(0.25f, dest[0], 1e-5f);
            Assert.AreEqual(3f, dest[1], 1e-5f);
            Assert.AreEqual(0.5f, dest[2], 1e-5f);
        }

        [Test]
        public void BlendFrame_SkipsNearZeroWeightRows()
        {
            // A NaN-filled row with ~0 weight must not poison the result.
            var rows = new[] { new[] { 1f, 2f }, new[] { float.NaN, float.NaN } };
            var weights = new float[] { 1f, 0f };
            var dest = new float[2];

            BlendSpaceMath.BlendFrame(dest, rows, weights);

            Assert.AreEqual(1f, dest[0], 1e-5f);
            Assert.AreEqual(2f, dest[1], 1e-5f);
        }

        [Test]
        public void DominantSample_ReturnsHighestWeightIndex()
        {
            Assert.AreEqual(2, BlendSpaceMath.DominantSample(new float[] { 0.1f, 0.2f, 0.7f }));
            Assert.AreEqual(0, BlendSpaceMath.DominantSample(new float[] { 0.9f, 0.05f, 0.05f }));
        }

        [Test]
        public void BlendRotations_SingleWeight_ReturnsThatRotation()
        {
            var rotations = new[] { Quaternion.Euler(0f, 40f, 0f), Quaternion.Euler(0f, -90f, 0f) };
            Quaternion result = BlendSpaceMath.BlendRotations(rotations, new[] { 1f, 0f });

            Assert.Less(Quaternion.Angle(rotations[0], result), 0.01f);
        }

        [Test]
        public void BlendRotations_HalfAndHalf_IsTheMidpoint()
        {
            var a = Quaternion.Euler(0f, 0f, 0f);
            var b = Quaternion.Euler(0f, 90f, 0f);

            Quaternion result = BlendSpaceMath.BlendRotations(new[] { a, b }, new[] { 0.5f, 0.5f });

            // Equal weights put the blend on the arc midpoint - 45 degrees from each.
            Assert.AreEqual(45f, Quaternion.Angle(a, result), 0.5f);
            Assert.AreEqual(45f, Quaternion.Angle(b, result), 0.5f);
        }

        [Test]
        public void BlendRotations_LeansTowardTheHeavierSample()
        {
            var a = Quaternion.Euler(0f, 0f, 0f);
            var b = Quaternion.Euler(0f, 90f, 0f);

            Quaternion result = BlendSpaceMath.BlendRotations(new[] { a, b }, new[] { 0.75f, 0.25f });

            Assert.Less(Quaternion.Angle(a, result), Quaternion.Angle(b, result),
                "a 0.75/0.25 blend must sit closer to the heavier rotation");
        }

        [Test]
        public void BlendRotations_OppositeSignQuaternions_DoNotCancel()
        {
            // q and -q are the same rotation. Summed without sign alignment they annihilate, and the
            // blend collapses; the reference-alignment step is what prevents that.
            var q = Quaternion.Euler(0f, 30f, 0f);
            var negated = new Quaternion(-q.x, -q.y, -q.z, -q.w);

            Quaternion result = BlendSpaceMath.BlendRotations(new[] { q, negated }, new[] { 0.5f, 0.5f });

            Assert.Less(Quaternion.Angle(q, result), 0.01f,
                "blending a rotation with its own negation must return that same rotation");
        }

        [Test]
        public void BlendRotations_AllZeroWeights_ReturnsIdentity()
        {
            var rotations = new[] { Quaternion.Euler(0f, 30f, 0f), Quaternion.Euler(0f, 60f, 0f) };
            Assert.AreEqual(Quaternion.identity, BlendSpaceMath.BlendRotations(rotations, new[] { 0f, 0f }));
        }

        [Test]
        public void BlendRotations_MismatchedInput_ReturnsIdentity()
        {
            Assert.AreEqual(Quaternion.identity, BlendSpaceMath.BlendRotations(null, new[] { 1f }));
            Assert.AreEqual(Quaternion.identity,
                BlendSpaceMath.BlendRotations(new[] { Quaternion.identity }, new[] { 1f, 0f }));
        }

        [Test]
        public void BlendPositions_IsTheWeightedSum()
        {
            var positions = new[] { new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 8f) };
            Vector3 result = BlendSpaceMath.BlendPositions(positions, new[] { 0.75f, 0.25f });

            Assert.AreEqual(new Vector3(1f, 0f, 2f).x, result.x, 1e-4f);
            Assert.AreEqual(new Vector3(1f, 0f, 2f).z, result.z, 1e-4f);
        }

        [Test]
        public void BlendPositions_MismatchedInput_ReturnsZero()
        {
            Assert.AreEqual(Vector3.zero, BlendSpaceMath.BlendPositions(new[] { Vector3.one }, new[] { 1f, 0f }));
        }

        [Test]
        public void BuildGrid_SingleResolution_ReturnsCentre()
        {
            var samples = new[] { new Vector2(0, 0), new Vector2(2, 4) };
            Vector2[] grid = BlendSpaceMath.BuildGrid(samples, new Vector2Int(1, 1));
            Assert.AreEqual(1, grid.Length);
            Assert.AreEqual(new Vector2(1, 2), grid[0]);
        }

        [Test]
        public void BuildGrid_CoversBoundingBoxCorners()
        {
            var samples = new[] { new Vector2(-1, -2), new Vector2(3, 5) };
            Vector2[] grid = BlendSpaceMath.BuildGrid(samples, new Vector2Int(2, 2));

            Assert.AreEqual(4, grid.Length);
            CollectionAssert.Contains(grid, new Vector2(-1, -2));
            CollectionAssert.Contains(grid, new Vector2(3, -2));
            CollectionAssert.Contains(grid, new Vector2(-1, 5));
            CollectionAssert.Contains(grid, new Vector2(3, 5));
        }

        [Test]
        public void BuildGrid_EmptySamples_ReturnsEmpty()
        {
            Vector2[] grid = BlendSpaceMath.BuildGrid(new Vector2[0], new Vector2Int(3, 3));
            Assert.AreEqual(0, grid.Length);
        }
    }
}
