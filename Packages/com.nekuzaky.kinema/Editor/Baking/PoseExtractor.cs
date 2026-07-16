using System.Collections.Generic;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Samples a single <see cref="AnimationClip"/> on a rig and turns it into a flat array of
    /// raw (un-normalized) feature rows following a <see cref="FeatureSchema"/>. Pure sampling and
    /// math: normalization and database assembly are the baker's job.
    /// </summary>
    public static class PoseExtractor
    {
        #region Main API

        /// <summary>
        /// Resolves the schema's bone names against a rig hierarchy. Missing bones are reported so the
        /// baker can surface them; their slot is returned as null and later sampled as the root.
        /// </summary>
        public static Transform[] ResolveBones(GameObject rig, FeatureSchema schema, out List<string> missing)
        {
            missing = new List<string>();
            var byName = new Dictionary<string, Transform>();
            foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
                byName[t.name] = t; // last-wins is fine; rigs rarely duplicate meaningful bone names.

            var bones = new Transform[schema.BoneCount];
            for (int b = 0; b < schema.BoneCount; b++)
            {
                if (byName.TryGetValue(schema.BoneNames[b], out Transform bone))
                    bones[b] = bone;
                else
                    missing.Add(schema.BoneNames[b]);
            }
            return bones;
        }

        /// <summary>Foot-contact detection thresholds (character space, rig at origin).</summary>
        public const float ContactMaxSpeed = 0.30f;   // m/s
        public const float ContactMaxHeight = 0.15f;  // m above ground

        /// <summary>
        /// Samples <paramref name="clip"/> at <paramref name="fps"/> and writes one feature row per
        /// frame into a freshly allocated array of length frameCount * schema.Dimension.
        /// <paramref name="contactBones"/> lists schema-bone indices treated as feet;
        /// <paramref name="contacts"/> receives one byte per frame (bit b = contact bone b grounded).
        /// </summary>
        public static float[] ExtractRawFeatures(
            GameObject rig, Transform root, Transform[] bones,
            AnimationClip clip, FeatureSchema schema, int fps,
            int[] contactBones, out byte[] contacts, out int frameCount)
        {
            frameCount = Mathf.Max(1, Mathf.CeilToInt(clip.length * fps));
            int boneCount = schema.BoneCount;
            float dt = 1f / fps;

            // --- Pass 1: sample world-space root + bone transforms for every frame. ---
            var rootPos = new Vector3[frameCount];
            var rootFwd = new Vector3[frameCount];
            var bonePos = new Vector3[frameCount * boneCount];

            for (int f = 0; f < frameCount; f++)
            {
                float time = Mathf.Min(f * dt, clip.length);
                clip.SampleAnimation(rig, time);

                rootPos[f] = root.position;
                rootFwd[f] = root.forward;
                for (int b = 0; b < boneCount; b++)
                    bonePos[f * boneCount + b] = bones[b] != null ? bones[b].position : root.position;
            }

            // --- Pass 2: assemble features in schema layout, in character space. ---
            int dim = schema.Dimension;
            var features = new float[frameCount * dim];
            float[] times = schema.TrajectoryTimes;

            for (int f = 0; f < frameCount; f++)
            {
                int rowOffset = f * dim;
                var space = new CharacterSpace(rootPos[f], rootFwd[f]);

                WriteTrajectory(features, rowOffset, schema, space, rootPos, rootFwd, f, frameCount, times, fps);
                WriteBones(features, rowOffset, schema, space, bonePos, boneCount, f, frameCount, dt);
                WriteRootVelocity(features, rowOffset, schema, space, rootPos, f, frameCount, dt);
            }

            contacts = ExtractContacts(bonePos, boneCount, frameCount, dt, contactBones);
            return features;
        }

        /// <summary>Flags grounded feet: low world height and near-zero world speed.</summary>
        private static byte[] ExtractContacts(Vector3[] bonePos, int boneCount, int frameCount, float dt, int[] contactBones)
        {
            var contacts = new byte[frameCount];
            if (contactBones == null || contactBones.Length == 0) return contacts;

            for (int f = 0; f < frameCount; f++)
            {
                int prev = Mathf.Max(f - 1, 0);
                int next = Mathf.Min(f + 1, frameCount - 1);
                float span = Mathf.Max((next - prev) * dt, 1e-5f);

                byte mask = 0;
                for (int c = 0; c < contactBones.Length && c < 8; c++)
                {
                    int b = contactBones[c];
                    if (b < 0 || b >= boneCount) continue;
                    Vector3 pos = bonePos[f * boneCount + b];
                    float speed = (bonePos[next * boneCount + b] - bonePos[prev * boneCount + b]).magnitude / span;
                    if (speed <= ContactMaxSpeed && pos.y <= ContactMaxHeight)
                        mask |= (byte)(1 << c);
                }
                contacts[f] = mask;
            }
            return contacts;
        }

        #endregion

        #region Tools and Utilities

        private static void WriteTrajectory(
            float[] features, int rowOffset, FeatureSchema schema, CharacterSpace space,
            Vector3[] rootPos, Vector3[] rootFwd, int f, int frameCount, float[] times, int fps)
        {
            int posOffset = rowOffset + schema.TrajectoryPositionOffset;
            int dirOffset = rowOffset + schema.TrajectoryDirectionOffset;

            for (int p = 0; p < times.Length; p++)
            {
                int j = Mathf.Clamp(f + Mathf.RoundToInt(times[p] * fps), 0, frameCount - 1);
                Vector2 localPos = space.ToLocalPoint(rootPos[j]);
                Vector2 localDir = space.ToLocalDirection(rootFwd[j]);

                features[posOffset + p * 2] = localPos.x;
                features[posOffset + p * 2 + 1] = localPos.y;
                features[dirOffset + p * 2] = localDir.x;
                features[dirOffset + p * 2 + 1] = localDir.y;
            }
        }

        private static void WriteBones(
            float[] features, int rowOffset, FeatureSchema schema, CharacterSpace space,
            Vector3[] bonePos, int boneCount, int f, int frameCount, float dt)
        {
            int posOffset = rowOffset + schema.BonePositionOffset;
            int velOffset = rowOffset + schema.BoneVelocityOffset;

            int prev = Mathf.Max(f - 1, 0);
            int next = Mathf.Min(f + 1, frameCount - 1);
            float span = Mathf.Max((next - prev) * dt, 1e-5f);

            bool naive = schema.PoseMode == PoseCostMode.Naive;

            for (int b = 0; b < boneCount; b++)
            {
                Vector3 localPos = space.ToLocalOffset3D(bonePos[f * boneCount + b]);
                Vector3 worldVel = (bonePos[next * boneCount + b] - bonePos[prev * boneCount + b]) / span;
                Vector3 localVel = space.ToLocalVector3D(worldVel);

                // Through the schema, never inline: the live query writes the same slots from the
                // same method, and a formula that drifted between the two would not look like a bug -
                // it would look like the character simply matching badly.
                Vector3 pose = schema.BonePoseValue(localPos, localVel);
                features[posOffset + b * 3] = pose.x;
                features[posOffset + b * 3 + 1] = pose.y;
                features[posOffset + b * 3 + 2] = pose.z;

                if (!naive)
                    continue; // the composite already carries the velocity; the group is empty.

                features[velOffset + b * 3] = localVel.x;
                features[velOffset + b * 3 + 1] = localVel.y;
                features[velOffset + b * 3 + 2] = localVel.z;
            }
        }

        private static void WriteRootVelocity(
            float[] features, int rowOffset, FeatureSchema schema, CharacterSpace space,
            Vector3[] rootPos, int f, int frameCount, float dt)
        {
            int prev = Mathf.Max(f - 1, 0);
            int next = Mathf.Min(f + 1, frameCount - 1);
            float span = Mathf.Max((next - prev) * dt, 1e-5f);

            Vector3 worldVel = (rootPos[next] - rootPos[prev]) / span;
            Vector2 localVel = space.ToLocalDirection(worldVel);

            int offset = rowOffset + schema.RootVelocityOffset;
            features[offset] = localVel.x;
            features[offset + 1] = localVel.y;
        }

        #endregion
    }
}
