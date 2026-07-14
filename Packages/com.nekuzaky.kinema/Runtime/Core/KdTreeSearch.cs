using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Optional acceleration structure: a KD-tree over weight-scaled features (coordinates are
    /// pre-multiplied by sqrt(weight), so plain Euclidean distance in tree space equals the weighted
    /// metric). Splits cycle through the highest-variance dimensions. Rebuilt when weights change.
    ///
    /// Honest tradeoff: with 30+ dimensions a KD-tree prunes weakly and the Burst linear scan is
    /// usually faster below ~50k frames; this exists for very large databases. It also cannot apply
    /// tag filters - the matcher falls back to the linear job whenever tag masks are active.
    /// </summary>
    public sealed class KdTreeSearch
    {
        #region Private and Protected

        private const int MaxSplitAxes = 16;

        private float[] _points;   // frame-major, weight-scaled
        private int[] _order;      // implicit balanced tree as a permutation of frame indices
        private int[] _splitAxes;  // top-variance dimensions, cycled by depth
        private int _dim;
        private int _count;

        private float _bestDist;
        private int _bestFrame;
        private float[] _query;

        #endregion

        #region Public

        public bool IsBuilt => _points != null;

        #endregion

        #region Main API

        /// <summary>Builds the tree from normalized features and the current per-dimension weights.</summary>
        public void Build(float[] features, int frameCount, int dim, float[] dimensionWeights)
        {
            _dim = dim;
            _count = frameCount;
            _points = new float[features.Length];
            _order = new int[frameCount];
            _query = new float[dim];

            var scale = new float[dim];
            for (int i = 0; i < dim; i++) scale[i] = Mathf.Sqrt(Mathf.Max(0f, dimensionWeights[i]));
            for (int f = 0; f < frameCount; f++)
            {
                int o = f * dim;
                for (int i = 0; i < dim; i++)
                    _points[o + i] = features[o + i] * scale[i];
            }

            for (int f = 0; f < frameCount; f++) _order[f] = f;
            _splitAxes = SelectSplitAxes(dim, frameCount);
            BuildRange(0, frameCount, 0);
        }

        /// <summary>Nearest frame to the (unscaled, normalized) query under the weighted metric.</summary>
        public int Nearest(float[] normalizedQuery, float[] dimensionWeights, out float cost)
        {
            for (int i = 0; i < _dim; i++)
                _query[i] = normalizedQuery[i] * Mathf.Sqrt(Mathf.Max(0f, dimensionWeights[i]));

            _bestDist = float.MaxValue;
            _bestFrame = -1;
            SearchRange(0, _count, 0);
            cost = _bestDist;
            return _bestFrame;
        }

        public void Invalidate() => _points = null;

        #endregion

        #region Tools and Utilities

        private int[] SelectSplitAxes(int dim, int frameCount)
        {
            // Variance per dimension over the scaled points; split on the widest ones.
            var variance = new float[dim];
            var mean = new float[dim];
            for (int f = 0; f < frameCount; f++)
                for (int i = 0; i < dim; i++) mean[i] += _points[f * dim + i];
            for (int i = 0; i < dim; i++) mean[i] /= Mathf.Max(1, frameCount);
            for (int f = 0; f < frameCount; f++)
                for (int i = 0; i < dim; i++)
                {
                    float d = _points[f * dim + i] - mean[i];
                    variance[i] += d * d;
                }

            int axisCount = Mathf.Min(MaxSplitAxes, dim);
            var axes = new int[axisCount];
            var used = new bool[dim];
            for (int a = 0; a < axisCount; a++)
            {
                int best = -1;
                for (int i = 0; i < dim; i++)
                    if (!used[i] && (best < 0 || variance[i] > variance[best])) best = i;
                used[best] = true;
                axes[a] = best;
            }
            return axes;
        }

        private void BuildRange(int start, int end, int depth)
        {
            int count = end - start;
            if (count <= 1) return;

            int axis = _splitAxes[depth % _splitAxes.Length];
            int mid = start + count / 2;
            QuickSelect(start, end - 1, mid, axis);

            BuildRange(start, mid, depth + 1);
            BuildRange(mid + 1, end, depth + 1);
        }

        /// <summary>Partial sort: places the k-th element (by axis value) at position k.</summary>
        private void QuickSelect(int lo, int hi, int k, int axis)
        {
            while (lo < hi)
            {
                float pivot = Axis(_order[(lo + hi) / 2], axis);
                int i = lo, j = hi;
                while (i <= j)
                {
                    while (Axis(_order[i], axis) < pivot) i++;
                    while (Axis(_order[j], axis) > pivot) j--;
                    if (i <= j)
                    {
                        (_order[i], _order[j]) = (_order[j], _order[i]);
                        i++; j--;
                    }
                }
                if (k <= j) hi = j;
                else if (k >= i) lo = i;
                else return;
            }
        }

        private void SearchRange(int start, int end, int depth)
        {
            int count = end - start;
            if (count <= 0) return;
            if (count == 1)
            {
                Consider(_order[start]);
                return;
            }

            int axis = _splitAxes[depth % _splitAxes.Length];
            int mid = start + count / 2;
            int frame = _order[mid];

            Consider(frame);

            float delta = _query[axis] - Axis(frame, axis);
            if (delta <= 0f)
            {
                SearchRange(start, mid, depth + 1);
                if (delta * delta < _bestDist) SearchRange(mid + 1, end, depth + 1);
            }
            else
            {
                SearchRange(mid + 1, end, depth + 1);
                if (delta * delta < _bestDist) SearchRange(start, mid, depth + 1);
            }
        }

        private void Consider(int frame)
        {
            int o = frame * _dim;
            float dist = 0f;
            for (int i = 0; i < _dim; i++)
            {
                float d = _query[i] - _points[o + i];
                dist += d * d;
                if (dist >= _bestDist) return;
            }
            _bestDist = dist;
            _bestFrame = frame;
        }

        private float Axis(int frame, int axis) => _points[frame * _dim + axis];

        #endregion
    }
}
