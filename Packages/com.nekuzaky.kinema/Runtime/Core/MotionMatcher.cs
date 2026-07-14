using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Result of a single database search: the winning frame plus a full per-group cost
    /// breakdown so the debug layer can explain *why* that frame won.
    /// </summary>
    public struct MotionMatchResult
    {
        public int FrameIndex;
        public float TotalCost;

        /// <summary>Cost contribution of each <see cref="FeatureGroup"/> for the winning frame.</summary>
        public float[] GroupCosts;

        public float TrajectoryCost => GroupCosts[(int)FeatureGroup.TrajectoryPosition]
                                     + GroupCosts[(int)FeatureGroup.TrajectoryDirection];

        public float PoseCost => GroupCosts[(int)FeatureGroup.BonePosition]
                               + GroupCosts[(int)FeatureGroup.BoneVelocity]
                               + GroupCosts[(int)FeatureGroup.RootVelocity];

        public bool IsValid => FrameIndex >= 0;
    }

    /// <summary>
    /// Weighted nearest-neighbour search over the database, executed as a Burst-compiled parallel
    /// job: the frame range is split into chunks, each worker scans its chunk with a weighted squared
    /// distance and a branch-and-bound early-out, and the per-chunk winners are reduced on the main
    /// thread. Features are uploaded once into a persistent <see cref="NativeArray{T}"/>.
    /// Owns native memory: call <see cref="Dispose"/> when done.
    /// </summary>
    public sealed class MotionMatcher : IDisposable
    {
        #region Private and Protected

        private const int ChunkSize = 256;

        private readonly MotionMatchingDatabase _database;
        private float[] _perDimensionWeights;

        private NativeArray<float> _nativeFeatures;
        private NativeArray<float> _nativeWeights;
        private NativeArray<float> _nativeQuery;
        private NativeArray<ulong> _nativeTags;
        private NativeArray<float> _chunkBestCost;
        private NativeArray<int> _chunkBestFrame;
        private readonly int _chunkCount;
        private bool _disposed;

        #endregion

        #region Public

        public MotionMatcher(MotionMatchingDatabase database, FeatureWeights weights)
        {
            _database = database;

            _nativeFeatures = new NativeArray<float>(database.Features, Allocator.Persistent);
            _nativeWeights = new NativeArray<float>(database.Dimension, Allocator.Persistent);
            _nativeQuery = new NativeArray<float>(database.Dimension, Allocator.Persistent);
            _nativeTags = database.HasTags
                ? new NativeArray<ulong>(database.FrameTags, Allocator.Persistent)
                : new NativeArray<ulong>(database.FrameCount, Allocator.Persistent); // zeroed = untagged
            _chunkCount = (database.FrameCount + ChunkSize - 1) / ChunkSize;
            _chunkBestCost = new NativeArray<float>(_chunkCount, Allocator.Persistent);
            _chunkBestFrame = new NativeArray<int>(_chunkCount, Allocator.Persistent);

            UpdateWeights(weights);
        }

        public MotionMatchingDatabase Database => _database;

        #endregion

        #region Main API

        /// <summary>Rebuilds the cached per-dimension weight table. Call when weights change.</summary>
        public void UpdateWeights(FeatureWeights weights)
        {
            _perDimensionWeights = _database.Schema.BuildPerDimensionWeights(weights);
            _nativeWeights.CopyFrom(_perDimensionWeights);
        }

        /// <summary>
        /// Finds the lowest-cost frame for the given query. <paramref name="ignoreFrame"/> and its
        /// close neighbours can be excluded to avoid re-selecting the frame already playing.
        /// <paramref name="requiredTags"/>: candidate frames must carry ALL these tag bits;
        /// <paramref name="excludedTags"/>: frames carrying ANY of these bits are skipped.
        /// </summary>
        public MotionMatchResult Search(
            MotionMatchingQuery query, int ignoreFrame = -1, int ignoreRadius = 0,
            ulong requiredTags = 0ul, ulong excludedTags = 0ul)
        {
            _nativeQuery.CopyFrom(query.Values);

            var job = new SearchJob
            {
                Features = _nativeFeatures,
                Weights = _nativeWeights,
                Query = _nativeQuery,
                Tags = _nativeTags,
                RequiredTags = requiredTags,
                ExcludedTags = excludedTags,
                Dimension = _database.Dimension,
                FrameCount = _database.FrameCount,
                ChunkSize = ChunkSize,
                IgnoreStart = ignoreFrame >= 0 ? ignoreFrame - ignoreRadius : -1,
                IgnoreEnd = ignoreFrame >= 0 ? ignoreFrame + ignoreRadius : -1,
                BestCost = _chunkBestCost,
                BestFrame = _chunkBestFrame
            };

            job.Schedule(_chunkCount, 1).Complete();

            int bestFrame = -1;
            float bestCost = float.MaxValue;
            for (int c = 0; c < _chunkCount; c++)
            {
                if (_chunkBestFrame[c] >= 0 && _chunkBestCost[c] < bestCost)
                {
                    bestCost = _chunkBestCost[c];
                    bestFrame = _chunkBestFrame[c];
                }
            }

            return BuildResult(query, bestFrame, bestCost);
        }

        /// <summary>Computes the total cost of a single candidate frame (used by the debug tools).</summary>
        public float EvaluateCost(MotionMatchingQuery query, int frameIndex)
        {
            float[] features = _database.Features;
            float[] weights = _perDimensionWeights;
            float[] values = query.Values;
            int dimension = _database.Dimension;
            int offset = frameIndex * dimension;

            float cost = 0f;
            for (int i = 0; i < dimension; i++)
            {
                float d = values[i] - features[offset + i];
                cost += weights[i] * d * d;
            }
            return cost;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _nativeFeatures.Dispose();
            _nativeWeights.Dispose();
            _nativeQuery.Dispose();
            _nativeTags.Dispose();
            _chunkBestCost.Dispose();
            _chunkBestFrame.Dispose();
        }

        #endregion

        #region Tools and Utilities

        [BurstCompile]
        private struct SearchJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> Features;
            [ReadOnly] public NativeArray<float> Weights;
            [ReadOnly] public NativeArray<float> Query;
            [ReadOnly] public NativeArray<ulong> Tags;
            public ulong RequiredTags, ExcludedTags;
            public int Dimension, FrameCount, ChunkSize;
            public int IgnoreStart, IgnoreEnd;

            [NativeDisableParallelForRestriction] public NativeArray<float> BestCost;
            [NativeDisableParallelForRestriction] public NativeArray<int> BestFrame;

            public void Execute(int chunk)
            {
                int start = chunk * ChunkSize;
                int end = Mathf.Min(start + ChunkSize, FrameCount);

                float best = float.MaxValue;
                int bestF = -1;

                for (int f = start; f < end; f++)
                {
                    if (f >= IgnoreStart && f <= IgnoreEnd) continue;

                    ulong tags = Tags[f];
                    if ((tags & RequiredTags) != RequiredTags) continue;
                    if ((tags & ExcludedTags) != 0ul) continue;

                    int offset = f * Dimension;
                    float cost = 0f;
                    for (int i = 0; i < Dimension; i++)
                    {
                        float d = Query[i] - Features[offset + i];
                        cost += Weights[i] * d * d;
                        if (cost >= best) break;
                    }
                    if (cost < best)
                    {
                        best = cost;
                        bestF = f;
                    }
                }

                BestCost[chunk] = best;
                BestFrame[chunk] = bestF;
            }
        }

        private MotionMatchResult BuildResult(MotionMatchingQuery query, int frameIndex, float totalCost)
        {
            var groupCosts = new float[FeatureGroupExtensions.Count];
            if (frameIndex < 0)
                return new MotionMatchResult { FrameIndex = -1, TotalCost = 0f, GroupCosts = groupCosts };

            FeatureSchema schema = _database.Schema;
            float[] features = _database.Features;
            float[] weights = _perDimensionWeights;
            float[] values = query.Values;
            int dimension = _database.Dimension;
            int offset = frameIndex * dimension;

            for (int i = 0; i < dimension; i++)
            {
                float d = values[i] - features[offset + i];
                groupCosts[(int)schema.GetGroupOf(i)] += weights[i] * d * d;
            }

            return new MotionMatchResult
            {
                FrameIndex = frameIndex,
                TotalCost = totalCost,
                GroupCosts = groupCosts
            };
        }

        #endregion
    }
}
