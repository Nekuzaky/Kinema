using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Outcome of a bake, rich enough for the editor window to render a proper report rather than
    /// a bare success/fail.
    /// </summary>
    public struct BakeReport
    {
        public bool Success;
        public string Error;
        public string DatabasePath;
        public MotionMatchingDatabase Database;

        public int ClipCount;
        public int FrameCount;
        public int Dimension;
        public float DurationSeconds;
        public List<string> Warnings;
    }

    /// <summary>
    /// The offline bake pipeline: turns a <see cref="MotionMatchingConfig"/> (rig + clips + schema)
    /// into a normalized <see cref="MotionMatchingDatabase"/> asset. Orchestration only — the
    /// per-clip sampling lives in <see cref="PoseExtractor"/>.
    /// </summary>
    public static class MotionMatchingBaker
    {
        #region Main API

        /// <summary>
        /// Bakes the config into a database asset. When <paramref name="existingDatabase"/> is given it
        /// is updated in place; otherwise a new asset is created next to the config.
        /// </summary>
        public static BakeReport Bake(MotionMatchingConfig config, MotionMatchingDatabase existingDatabase = null)
        {
            var report = new BakeReport { Warnings = new List<string>() };

            if (config == null)
            {
                report.Error = "Config is null.";
                return report;
            }
            if (!config.IsReadyToBake(out string reason))
            {
                report.Error = reason;
                return report;
            }

            GameObject instance = null;
            try
            {
                FeatureSchema schema = config.Schema.Clone();
                int dim = schema.Dimension;
                int fps = config.BakeFrameRate;

                instance = UnityEngine.Object.Instantiate(config.RigPrefab);
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                if (instance.GetComponentInChildren<Animator>() == null)
                    report.Warnings.Add("Rig has no Animator; humanoid clips may not sample correctly.");

                Transform[] bones = PoseExtractor.ResolveBones(instance, schema, out List<string> missing);
                foreach (string m in missing)
                    report.Warnings.Add($"Bone '{m}' not found on rig — sampled as root instead.");

                // Feet = schema bones whose name contains "foot" (max 8 contact slots).
                var contactBones = new List<int>();
                for (int b = 0; b < schema.BoneCount && contactBones.Count < 8; b++)
                    if (schema.BoneNames[b].ToLowerInvariant().Contains("foot"))
                        contactBones.Add(b);
                int[] contactBoneIndices = contactBones.ToArray();

                var allFeatures = new List<float>();
                var allContacts = new List<byte>();
                var allTags = new List<ulong>();
                var frames = new List<MotionFrameInfo>();
                var clipEntries = new List<MotionClipEntry>();
                float totalDuration = 0f;
                int frameCursor = 0;

                for (int c = 0; c < config.Clips.Count; c++)
                {
                    AnimationClip clip = config.Clips[c];
                    EditorUtility.DisplayProgressBar(
                        "Baking Motion Matching Database",
                        $"Sampling '{clip.name}' ({c + 1}/{config.Clips.Count})",
                        (c + 1f) / config.Clips.Count);

                    float[] raw = PoseExtractor.ExtractRawFeatures(
                        instance, instance.transform, bones, clip, schema, fps,
                        contactBoneIndices, out byte[] clipContacts, out int clipFrames);

                    allFeatures.AddRange(raw);
                    allContacts.AddRange(clipContacts);
                    float frameDt = 1f / fps;
                    ClipTagTrack track = config.FindTagTrack(clip);
                    for (int f = 0; f < clipFrames; f++)
                    {
                        float t = f * frameDt;
                        frames.Add(new MotionFrameInfo(c, t));
                        allTags.Add(track?.MaskAt(t) ?? 0ul);
                    }

                    clipEntries.Add(new MotionClipEntry
                    {
                        Clip = clip,
                        Name = clip.name,
                        StartFrame = frameCursor,
                        FrameCount = clipFrames,
                        Length = clip.length,
                        IsLooping = clip.isLooping
                    });

                    frameCursor += clipFrames;
                    totalDuration += clip.length;
                }

                if (frames.Count == 0)
                {
                    report.Error = "No frames were produced (are the clips empty?).";
                    return report;
                }

                if (config.GenerateMirroredVariants)
                {
                    AppendMirroredFrames(schema, allFeatures, allContacts, allTags, frames, contactBoneIndices);
                    report.Warnings.Add("Mirrored variants baked (experimental): frame count doubled.");
                }

                float[] features = allFeatures.ToArray();
                ComputeNormalization(features, frames.Count, dim, out float[] mean, out float[] std);
                Normalize(features, frames.Count, dim, mean, std);

                MotionMatchingDatabase database = existingDatabase != null
                    ? existingDatabase
                    : ScriptableObject.CreateInstance<MotionMatchingDatabase>();

                database.SetBakedData(
                    schema, features, mean, std,
                    frames.ToArray(), clipEntries.ToArray(),
                    config.DefaultWeights, fps,
                    DateTime.UtcNow.ToString("u"), totalDuration,
                    allContacts.ToArray(), contactBoneIndices,
                    allTags.ToArray(), System.Linq.Enumerable.ToArray(config.TagNames));

                string path = SaveDatabase(config, database, existingDatabase != null);

                report.Success = true;
                report.Database = database;
                report.DatabasePath = path;
                report.ClipCount = clipEntries.Count;
                report.FrameCount = frames.Count;
                report.Dimension = dim;
                report.DurationSeconds = totalDuration;
                return report;
            }
            catch (Exception e)
            {
                report.Error = e.Message;
                Debug.LogException(e);
                return report;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        #endregion

        #region Tools and Utilities

        /// <summary>
        /// Duplicates every baked frame as a mirrored variant: trajectory and root velocity X-flipped,
        /// Left/Right bone slots swapped with X-flipped positions/velocities, contacts bit-swapped.
        /// Playback mirrors the pose at runtime (<see cref="MirrorPose"/>).
        /// </summary>
        private static void AppendMirroredFrames(
            FeatureSchema schema, List<float> features, List<byte> contacts, List<ulong> tags,
            List<MotionFrameInfo> frames, int[] contactBoneIndices)
        {
            int dim = schema.Dimension;
            int originalCount = frames.Count;

            // Bone slot permutation: Left <-> Right by name, self when unpaired.
            var bonePair = new int[schema.BoneCount];
            for (int b = 0; b < schema.BoneCount; b++)
            {
                bonePair[b] = b;
                string n = schema.BoneNames[b];
                string m = n.Contains("Left") ? n.Replace("Left", "Right")
                         : n.Contains("Right") ? n.Replace("Right", "Left") : null;
                if (m == null) continue;
                for (int j = 0; j < schema.BoneCount; j++)
                    if (schema.BoneNames[j] == m) { bonePair[b] = j; break; }
            }

            // Contact slot permutation follows the bone permutation.
            var contactPair = new int[contactBoneIndices.Length];
            for (int c = 0; c < contactBoneIndices.Length; c++)
            {
                contactPair[c] = c;
                int mirroredBone = bonePair[contactBoneIndices[c]];
                for (int j = 0; j < contactBoneIndices.Length; j++)
                    if (contactBoneIndices[j] == mirroredBone) { contactPair[c] = j; break; }
            }

            var row = new float[dim];
            for (int f = 0; f < originalCount; f++)
            {
                int src = f * dim;

                // Trajectory: x components flip, y (forward) stays.
                for (int p = 0; p < schema.TrajectoryPointCount; p++)
                {
                    int px = schema.TrajectoryPositionOffset + p * 2;
                    int dx = schema.TrajectoryDirectionOffset + p * 2;
                    row[px] = -features[src + px]; row[px + 1] = features[src + px + 1];
                    row[dx] = -features[src + dx]; row[dx + 1] = features[src + dx + 1];
                }

                // Bones: swap L/R slots, flip x of positions and velocities.
                for (int b = 0; b < schema.BoneCount; b++)
                {
                    int dst = schema.BonePositionOffset + b * 3;
                    int from = schema.BonePositionOffset + bonePair[b] * 3;
                    row[dst] = -features[src + from]; row[dst + 1] = features[src + from + 1]; row[dst + 2] = features[src + from + 2];

                    dst = schema.BoneVelocityOffset + b * 3;
                    from = schema.BoneVelocityOffset + bonePair[b] * 3;
                    row[dst] = -features[src + from]; row[dst + 1] = features[src + from + 1]; row[dst + 2] = features[src + from + 2];
                }

                row[schema.RootVelocityOffset] = -features[src + schema.RootVelocityOffset];
                row[schema.RootVelocityOffset + 1] = features[src + schema.RootVelocityOffset + 1];

                features.AddRange(row);

                byte srcContacts = contacts[f];
                byte mirroredContacts = 0;
                for (int c = 0; c < contactPair.Length && c < 8; c++)
                    if ((srcContacts & (1 << contactPair[c])) != 0) mirroredContacts |= (byte)(1 << c);
                contacts.Add(mirroredContacts);

                tags.Add(tags[f]);
                MotionFrameInfo original = frames[f];
                frames.Add(new MotionFrameInfo(original.ClipIndex, original.Time, isMirrored: true));
            }
        }

        private static void ComputeNormalization(float[] features, int frameCount, int dim, out float[] mean, out float[] std)
        {
            mean = new float[dim];
            std = new float[dim];

            for (int f = 0; f < frameCount; f++)
            {
                int offset = f * dim;
                for (int i = 0; i < dim; i++)
                    mean[i] += features[offset + i];
            }
            for (int i = 0; i < dim; i++) mean[i] /= frameCount;

            for (int f = 0; f < frameCount; f++)
            {
                int offset = f * dim;
                for (int i = 0; i < dim; i++)
                {
                    float d = features[offset + i] - mean[i];
                    std[i] += d * d;
                }
            }
            for (int i = 0; i < dim; i++)
            {
                std[i] = Mathf.Sqrt(std[i] / frameCount);
                if (std[i] < 1e-5f) std[i] = 1f; // constant dimension: leave it untouched by normalization.
            }
        }

        private static void Normalize(float[] features, int frameCount, int dim, float[] mean, float[] std)
        {
            for (int f = 0; f < frameCount; f++)
            {
                int offset = f * dim;
                for (int i = 0; i < dim; i++)
                    features[offset + i] = (features[offset + i] - mean[i]) / std[i];
            }
        }

        private static string SaveDatabase(MotionMatchingConfig config, MotionMatchingDatabase database, bool alreadyAnAsset)
        {
            if (alreadyAnAsset && AssetDatabase.Contains(database))
            {
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssets();
                return AssetDatabase.GetAssetPath(database);
            }

            string configPath = AssetDatabase.GetAssetPath(config);
            string directory = string.IsNullOrEmpty(configPath) ? "Assets" : Path.GetDirectoryName(configPath);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{config.name}Database.asset".Replace("\\", "/"));

            AssetDatabase.CreateAsset(database, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return path;
        }

        #endregion
    }
}
