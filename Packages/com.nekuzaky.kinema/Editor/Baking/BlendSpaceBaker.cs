using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Bakes a <see cref="MotionMatchingBlendSpace"/> into real, playable <see cref="AnimationClip"/>
    /// assets - one per grid point - so a blend space becomes matchable data like any other clip.
    ///
    /// The blend happens in POSE space, not feature space. That distinction is the whole reason this
    /// exists: the database stores feature vectors for searching, but playback replays the matched
    /// frame's actual AnimationClip, so a grid point blended only as features would be matchable and
    /// then unplayable. Here each source clip is sampled on the rig at each time step, the resulting
    /// poses are blended by the grid point's weights (<see cref="BlendSpaceMath.BlendPositions"/> /
    /// <see cref="BlendSpaceMath.BlendRotations"/>), and the blended performance is written out with
    /// <see cref="PoseClipBaker"/> - a clip that plays.
    ///
    /// Inherits PoseClipBaker's limitation: transform-curve (Generic) clips. A Humanoid Animator
    /// ignores transform curves in favour of muscle data, so the baked grid plays on a rig read as
    /// Generic. Callers are told rather than left to discover it on an empty playback.
    /// </summary>
    public static class BlendSpaceBaker
    {
        #region Main API

        public struct Result
        {
            public bool Success;
            public string Error;
            /// <summary>Baked grid clips, one per grid point, in <see cref="BlendSpaceMath.BuildGrid"/> order.</summary>
            public List<AnimationClip> Clips;
            public List<string> Warnings;
        }

        /// <summary>
        /// Samples the blend space's source clips on <paramref name="rigPrefab"/> and writes one
        /// blended clip per grid point into <paramref name="outputFolder"/> (created if absent,
        /// existing clips overwritten in place so re-baking doesn't orphan references).
        /// Grid clip length is the longest source clip: shorter sources hold their last pose rather
        /// than snapping back to their start, which would read as a hitch mid-blend.
        /// </summary>
        public static Result Bake(MotionMatchingBlendSpace blendSpace, GameObject rigPrefab, string outputFolder, int frameRate = 30)
        {
            var result = new Result { Warnings = new List<string>(), Clips = new List<AnimationClip>() };

            if (blendSpace == null) { result.Error = "Blend space is null."; return result; }
            if (!blendSpace.IsReadyToBake(out string reason)) { result.Error = reason; return result; }
            if (rigPrefab == null) { result.Error = "No rig prefab assigned."; return result; }

            Vector2[] samplePositions = blendSpace.EntryPositions();
            Vector2[] grid = BlendSpaceMath.BuildGrid(samplePositions, blendSpace.GridResolution);
            if (grid.Length == 0) { result.Error = "Grid is empty."; return result; }

            GameObject instance = null;
            try
            {
                instance = Object.Instantiate(rigPrefab);
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                Transform[] bones = CollectBones(instance, out string[] bonePaths);
                if (bones.Length == 0) { result.Error = "Rig has no child bones to sample."; return result; }

                EnsureFolder(outputFolder);

                float duration = LongestSourceLength(blendSpace);
                int frameCount = Mathf.Max(2, Mathf.CeilToInt(duration * frameRate));

                for (int g = 0; g < grid.Length; g++)
                {
                    EditorUtility.DisplayProgressBar("Baking Blend Space",
                        $"Grid point {g + 1}/{grid.Length}", (g + 1f) / grid.Length);

                    float[] weights = BlendSpaceMath.ComputeWeights(grid[g], samplePositions);
                    PoseTake take = SampleBlendedTake(instance, bones, bonePaths, blendSpace, weights, frameCount, frameRate);

                    string path = $"{outputFolder}/{blendSpace.Name}_{FormatCoordinate(grid[g].x)}_{FormatCoordinate(grid[g].y)}.anim";
                    AnimationClip clip = PoseClipBaker.Bake(take, path, loop: true);
                    if (clip != null) result.Clips.Add(clip);
                }

                result.Success = result.Clips.Count > 0;
                if (!result.Success)
                {
                    result.Error = "No grid clips were produced.";
                    return result;
                }

                // On success only: a failed report carrying "Baked 0 grid clips as transform-curve
                // clips" is noise stacked on top of the actual error.
                result.Warnings.Add(
                    $"Baked {result.Clips.Count} grid clips as transform-curve (Generic) clips - they play on a rig " +
                    "read as Generic, not through a Humanoid Animator. Add them to the config's clip list and rebake " +
                    "the database to make them matchable.");
                return result;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        #endregion

        #region Tools and Utilities

        /// <summary>
        /// One blended performance: at each time step every source clip is sampled on the rig and its
        /// pose read off, then the poses are blended by <paramref name="weights"/>. Sampling is
        /// destructive (SampleAnimation writes the rig), which is why each source's pose is read out
        /// completely before the next is sampled.
        /// </summary>
        private static PoseTake SampleBlendedTake(
            GameObject rig, Transform[] bones, string[] bonePaths,
            MotionMatchingBlendSpace blendSpace, float[] weights, int frameCount, int frameRate)
        {
            int entryCount = blendSpace.Entries.Count;
            int boneCount = bones.Length;
            float dt = 1f / frameRate;

            var take = new PoseTake
            {
                BonePaths = bonePaths,
                RootPositions = new Vector3[frameCount],
                RootRotations = new Quaternion[frameCount],
                BoneRotations = new Quaternion[frameCount * boneCount],
                Times = new float[frameCount],
                SourceRigName = rig.name
            };

            var rootPositions = new Vector3[entryCount];
            var rootRotations = new Quaternion[entryCount];
            var boneRotations = new Quaternion[entryCount * boneCount];

            for (int f = 0; f < frameCount; f++)
            {
                float time = f * dt;

                for (int e = 0; e < entryCount; e++)
                {
                    AnimationClip source = blendSpace.Entries[e].Clip;
                    // Past its end a shorter source holds its final pose - wrapping it back to the
                    // start instead would inject a discontinuity into the blended result.
                    source.SampleAnimation(rig, Mathf.Min(time, source.length));

                    rootPositions[e] = rig.transform.localPosition;
                    rootRotations[e] = rig.transform.localRotation;
                    for (int b = 0; b < boneCount; b++)
                        boneRotations[e * boneCount + b] = bones[b].localRotation;
                }

                take.Times[f] = time;
                take.RootPositions[f] = BlendSpaceMath.BlendPositions(rootPositions, weights);
                take.RootRotations[f] = BlendSpaceMath.BlendRotations(rootRotations, weights);

                var perBone = new Quaternion[entryCount];
                for (int b = 0; b < boneCount; b++)
                {
                    for (int e = 0; e < entryCount; e++) perBone[e] = boneRotations[e * boneCount + b];
                    take.BoneRotations[f * boneCount + b] = BlendSpaceMath.BlendRotations(perBone, weights);
                }
            }

            return take;
        }

        /// <summary>Every transform under the rig root, with the root-relative paths AnimationClip
        /// curves are keyed by. The root itself is excluded - it is keyed separately as the take's
        /// root position/rotation.</summary>
        private static Transform[] CollectBones(GameObject rig, out string[] bonePaths)
        {
            var bones = new List<Transform>();
            var paths = new List<string>();

            foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (t == rig.transform) continue;
                bones.Add(t);
                paths.Add(AnimationUtility.CalculateTransformPath(t, rig.transform));
            }

            bonePaths = paths.ToArray();
            return bones.ToArray();
        }

        private static float LongestSourceLength(MotionMatchingBlendSpace blendSpace)
        {
            float longest = 0f;
            for (int i = 0; i < blendSpace.Entries.Count; i++)
                longest = Mathf.Max(longest, blendSpace.Entries[i].Clip.length);
            return Mathf.Max(longest, 0.1f);
        }

        /// <summary>Grid coordinate as a filename-safe fixed-decimal string ("-0p50"): a raw
        /// ToString gives "-0.5" or "-0,5" depending on the machine's locale, and one of those makes
        /// asset paths that differ between contributors.</summary>
        private static string FormatCoordinate(float value)
        {
            return value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', 'p');
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        #endregion
    }
}
