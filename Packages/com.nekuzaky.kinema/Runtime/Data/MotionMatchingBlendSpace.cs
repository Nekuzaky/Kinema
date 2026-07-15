using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Authoring data for a 2D blend space (MxM-style: "blend spaces as matchable data" in
    /// TODO.md): a set of source clips positioned on a plane (e.g. move direction x/y, or
    /// speed/turn). Pairs with <see cref="BlendSpaceMath"/> for the sample-weighting and grid math.
    ///
    /// NOT YET WIRED INTO THE BAKER. Playback (<c>MotionMatchingController.SetSlotClip</c>) replays
    /// the matched frame's *original* <c>AnimationClip</c> at its baked time - the database only
    /// stores feature vectors for search, not poses. Blending in feature space (weighted sum of
    /// already-extracted rows, see <see cref="BlendSpaceMath.BlendFrame"/>) is enough to make a grid
    /// point *matchable* but not enough to make it *playable*: there is no real clip to hand
    /// `AnimationClipPlayable` for a synthetic grid point. Finishing this requires baking each grid
    /// point's blended pose into a real <c>AnimationClip</c> asset first (extending
    /// <c>Editor/Baking/PoseClipBaker.cs</c>, which already does recording-to-clip), then baking
    /// that clip through the normal per-clip path. That's real new Playable-graph plumbing with no
    /// way to verify the resulting blended pose looks right without opening the Editor and watching
    /// it - left undone here rather than shipped unverified. See TODO.md.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MotionMatchingBlendSpace",
        menuName = "Kinema/Motion Matching/Blend Space",
        order = 1)]
    public sealed class MotionMatchingBlendSpace : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public AnimationClip Clip;
            public Vector2 Position;
        }

        [Tooltip("Baked clip names are prefixed with this (e.g. \"Locomotion_x0.50_y1.00\").")]
        [SerializeField] private string _name = "BlendSpace";

        [Tooltip("Source clips and their 2D position (e.g. move direction, or speed/turn-rate).")]
        [SerializeField] private List<Entry> _entries = new List<Entry>();

        [Tooltip("Synthetic clips baked on a regular grid covering the entries' bounding box. Higher resolution = smoother coverage but more baked frames.")]
        [SerializeField] private Vector2Int _gridResolution = new Vector2Int(5, 5);

        public string Name => _name;
        public IReadOnlyList<Entry> Entries => _entries;
        public Vector2Int GridResolution => _gridResolution;

        public bool IsReadyToBake(out string reason)
        {
            if (_entries == null || _entries.Count == 0)
            {
                reason = $"Blend space '{_name}' has no entries.";
                return false;
            }
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Clip == null)
                {
                    reason = $"Blend space '{_name}': entry {i} has no clip assigned.";
                    return false;
                }
            }
            reason = null;
            return true;
        }

        /// <summary>Entry positions, in entry order - the array <see cref="BlendSpaceMath.ComputeWeights"/>
        /// and <see cref="BlendSpaceMath.BuildGrid"/> expect.</summary>
        public Vector2[] EntryPositions()
        {
            var positions = new Vector2[_entries.Count];
            for (int i = 0; i < _entries.Count; i++) positions[i] = _entries[i].Position;
            return positions;
        }
    }
}
