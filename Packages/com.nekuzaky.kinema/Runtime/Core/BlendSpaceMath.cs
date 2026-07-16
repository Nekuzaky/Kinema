using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Pure math backing <see cref="MotionMatchingBlendSpace"/> baking: 2D sample weighting (Gradient
    /// Band Interpolation, the same technique Unity's own 2D Freeform Cartesian blend trees use) and
    /// combining already-extracted feature rows by those weights. No Unity Editor, rig or
    /// <c>AnimationClip</c> dependency - blending happens on baked feature vectors (bone positions are
    /// already Euclidean, so a linear combination is a reasonable approximation of blending the
    /// underlying poses), not on the animation graph, so this is fully unit-testable without a rig.
    /// </summary>
    public static class BlendSpaceMath
    {
        /// <summary>
        /// Gradient Band Interpolation: for each sample i, starts at weight 1 and is multiplied down
        /// by every other sample j based on how far past the perpendicular bisector of (i, j) the
        /// query point falls. Exact at a sample position (that sample gets 1, the rest 0), smooth
        /// in between, and - unlike plain inverse-distance weighting - respects directional structure
        /// (a query between "walk forward" and "walk left" doesn't get pulled toward an unrelated
        /// "walk backward" sample just because it happens to be equidistant).
        /// </summary>
        public static float[] ComputeWeights(Vector2 query, Vector2[] samplePositions)
        {
            int n = samplePositions.Length;
            var weights = new float[n];
            if (n == 0) return weights;
            if (n == 1) { weights[0] = 1f; return weights; }

            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = 1f;
                Vector2 toQuery = query - samplePositions[i];
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    Vector2 toOther = samplePositions[j] - samplePositions[i];
                    float lenSq = toOther.sqrMagnitude;
                    if (lenSq < 1e-8f) continue; // duplicate sample position - ignore as a divider
                    float t = Vector2.Dot(toQuery, toOther) / lenSq;
                    w *= Mathf.Clamp01(1f - t);
                }
                weights[i] = w;
                sum += w;
            }

            if (sum < 1e-8f)
            {
                // Degenerate (query far outside a one-sample-direction space, or all-zero weights):
                // fall back to the nearest sample rather than returning an all-zero blend.
                int nearest = 0;
                float bestDist = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    float d = (query - samplePositions[i]).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; nearest = i; }
                }
                weights[nearest] = 1f;
                return weights;
            }

            for (int i = 0; i < n; i++) weights[i] /= sum;
            return weights;
        }

        /// <summary>
        /// Weighted sum of same-length feature rows (one per blend space sample, all for the same
        /// frame index/time). Rows with a ~zero weight are skipped rather than multiplied, so a source
        /// clip that's baked but numerically irrelevant at a given grid point costs nothing.
        /// </summary>
        public static void BlendFrame(float[] destination, float[][] sourceRows, float[] weights)
        {
            int dim = destination.Length;
            for (int i = 0; i < dim; i++) destination[i] = 0f;

            for (int s = 0; s < sourceRows.Length; s++)
            {
                float w = weights[s];
                if (w <= 1e-6f) continue;
                float[] row = sourceRows[s];
                for (int i = 0; i < dim; i++) destination[i] += w * row[i];
            }
        }

        /// <summary>
        /// Weighted blend of rotations, for blending POSES (what a playable grid point needs) rather
        /// than feature rows. Accumulating normalized quaternions weighted this way approximates the
        /// true rotation average and is what every runtime blend tree does - exact for two rotations,
        /// close enough for the small angular spreads a blend space's neighbours actually sit at.
        ///
        /// Each quaternion is sign-aligned to the highest-weight one before accumulating: q and -q are
        /// the same rotation but cancel each other when summed, so an unaligned average of two visually
        /// close poses can collapse to nonsense. Returns identity if every weight is ~0.
        /// </summary>
        public static Quaternion BlendRotations(Quaternion[] rotations, float[] weights)
        {
            if (rotations == null || weights == null || rotations.Length == 0 ||
                rotations.Length != weights.Length)
            {
                return Quaternion.identity;
            }

            int reference = DominantSample(weights);
            Quaternion referenceRotation = rotations[reference];

            float x = 0f, y = 0f, z = 0f, w = 0f;
            for (int i = 0; i < rotations.Length; i++)
            {
                float weight = weights[i];
                if (weight <= 1e-6f) continue;

                Quaternion q = rotations[i];
                // Dot < 0 means q sits on the opposite hemisphere from the reference: same rotation,
                // opposite sign. Flip it so the two reinforce instead of cancelling.
                if (Quaternion.Dot(referenceRotation, q) < 0f) weight = -weight;

                x += q.x * weight;
                y += q.y * weight;
                z += q.z * weight;
                w += q.w * weight;
            }

            float magnitude = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (magnitude < 1e-6f) return Quaternion.identity;
            return new Quaternion(x / magnitude, y / magnitude, z / magnitude, w / magnitude);
        }

        /// <summary>Weighted sum of positions - the translation counterpart of
        /// <see cref="BlendRotations"/>. Weights are assumed normalized (<see cref="ComputeWeights"/>
        /// guarantees it); near-zero weights are skipped so an irrelevant sample costs nothing.</summary>
        public static Vector3 BlendPositions(Vector3[] positions, float[] weights)
        {
            if (positions == null || weights == null || positions.Length == 0 ||
                positions.Length != weights.Length)
            {
                return Vector3.zero;
            }

            Vector3 result = Vector3.zero;
            for (int i = 0; i < positions.Length; i++)
            {
                if (weights[i] <= 1e-6f) continue;
                result += positions[i] * weights[i];
            }
            return result;
        }

        /// <summary>Index of the highest-weight sample - used for data that can't be linearly blended
        /// (discrete foot-contact bits, tag masks): nearest-sample wins rather than being averaged.</summary>
        public static int DominantSample(float[] weights)
        {
            int best = 0;
            for (int i = 1; i < weights.Length; i++)
                if (weights[i] > weights[best]) best = i;
            return best;
        }

        /// <summary>Regular grid of query points covering the bounding box of <paramref name="samplePositions"/>,
        /// <paramref name="resolution"/> points per axis (min 1). A single sample or a degenerate
        /// (zero-area) bounding box collapses to that one point.</summary>
        public static Vector2[] BuildGrid(Vector2[] samplePositions, Vector2Int resolution)
        {
            int rx = Mathf.Max(1, resolution.x);
            int ry = Mathf.Max(1, resolution.y);

            if (samplePositions.Length == 0) return new Vector2[0];

            Vector2 min = samplePositions[0], max = samplePositions[0];
            for (int i = 1; i < samplePositions.Length; i++)
            {
                min = Vector2.Min(min, samplePositions[i]);
                max = Vector2.Max(max, samplePositions[i]);
            }

            if (rx == 1 && ry == 1) return new[] { (min + max) * 0.5f };

            var grid = new Vector2[rx * ry];
            for (int y = 0; y < ry; y++)
            {
                float ty = ry == 1 ? 0.5f : y / (float)(ry - 1);
                for (int x = 0; x < rx; x++)
                {
                    float tx = rx == 1 ? 0.5f : x / (float)(rx - 1);
                    grid[y * rx + x] = new Vector2(
                        Mathf.Lerp(min.x, max.x, tx),
                        Mathf.Lerp(min.y, max.y, ty));
                }
            }
            return grid;
        }
    }
}
