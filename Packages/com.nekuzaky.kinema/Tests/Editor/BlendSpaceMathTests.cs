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
