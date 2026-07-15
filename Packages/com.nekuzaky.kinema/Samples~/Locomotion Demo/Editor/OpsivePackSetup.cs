using System.Collections.Generic;
using System.Linq;
using Kinema.MotionMatching.Editor;
using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Samples.Editor
{
    /// <summary>
    /// Bakes a database from an Opsive OmniAnimation locomotion pack: a real mocap set with the
    /// coverage motion matching actually needs - walk/run/sprint in every direction, diagonals,
    /// strafes, turn-in-place, and (rarely available elsewhere) proper starts and stops.
    ///
    /// The pack ships a bare skeleton with no mesh, so the demo's skinned character is used for
    /// display and baking instead, with Humanoid retargeting mapping the mocap onto it. Baking on
    /// the rig that is actually rendered is what keeps the features honest: they describe the body
    /// on screen, not a proxy of it.
    ///
    /// Clips are auto-tagged from their file names (Crouch, Strafe, Backward, Sprint, Start, Stop...),
    /// which turns 74 clips into a tag-filterable database without painting a single range by hand.
    /// </summary>
    public static class OpsivePackSetup
    {
        #region Main API

        private const string PackRoot = "Assets/Opsive/OmniAnimation/Packs";
        private const string RigPath = PackRoot + "/Shared/Animators/OmniAnimationRig.fbx";
        private const string ClipFolder = PackRoot + "/CoreLocomotion/Animations/Original";

        private static string OutputFolder => DemoPaths.SampleRoot + "/Opsive";
        private static string ConfigPath => OutputFolder + "/KinemaOpsiveConfig.asset";
        private static string DatabasePath => OutputFolder + "/KinemaOpsiveConfigDatabase.asset";

        /// <summary>Tag vocabulary derived from the pack's naming convention.</summary>
        private static readonly (string Tag, string Keyword)[] TagRules =
        {
            ("Crouch",   "Crouch"),
            ("Strafe",   "Strafe"),
            ("Backward", "Backward"),
            ("Diagonal", "Diagonal"),
            ("Idle",     "Idle"),
            ("Walk",     "Walk"),
            ("Run",      "Run"),
            ("Sprint",   "Sprint"),
            ("Turn",     "Turn"),
            ("Start",    "Start"),
            ("Stop",     "Stop"),
            ("Jump",     "Jump")
        };

        #endregion

        #region Tools and Utilities

        /// <summary>True when the pack is present in the project at the expected path.</summary>
        internal static bool PackAvailable => AssetDatabase.IsValidFolder(ClipFolder);

        /// <summary>
        /// Resolves the rig, bakes every clip in the pack, and authors the vault event. Split out from
        /// scene building so the Demo Scene tool can run the whole setup itself rather than requiring
        /// a separate menu trip.
        /// </summary>
        internal static bool TryBake(out DemoSceneTool.DemoBake bake)
        {
            bake = default;

            GameObject rig = ResolveDisplayRig();
            if (rig == null) return false;

            AnimationClip[] clips = LoadPackClips();
            if (clips.Length == 0)
            {
                Debug.LogError($"[Kinema] No clips found under {ClipFolder}.");
                return false;
            }
            Debug.Log($"[Kinema] Opsive pack: {clips.Length} mocap clips found.");

            string[] boneNames = ResolveHumanoidBones(rig);
            if (boneNames.Length == 0)
            {
                Debug.LogError($"[Kinema] Could not resolve Humanoid bones on '{rig.name}'.");
                return false;
            }
            Debug.Log($"[Kinema] Bones: [{string.Join(", ", boneNames)}]");

            EnsureFolder();
            MotionMatchingConfig config = CreateOrLoadConfig();
            ConfigureAsset(config, rig, clips, boneNames);

            var existingDb = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(DatabasePath);
            BakeReport report = MotionMatchingBaker.Bake(config, existingDb);
            if (!report.Success)
            {
                Debug.LogError("[Kinema] Bake failed: " + report.Error);
                return false;
            }
            Debug.Log($"[Kinema] Baked {report.FrameCount:N0} frames from {report.ClipCount} clip(s) → {report.DatabasePath}");
            foreach (string w in report.Warnings) Debug.LogWarning("[Kinema] " + w);

            // Paths, not objects: creating the demo scene unloads unreferenced assets and destroys
            // these instances, leaving Unity fake-nulls behind. The scene builder reloads from disk.
            bake = new DemoSceneTool.DemoBake
            {
                RigPath = AssetDatabase.GetAssetPath(rig),
                DatabasePath = DatabasePath,
                VaultEventPath = CreateVaultEvent(clips)
            };
            return true;
        }

        /// <summary>
        /// Authors the vault event from the pack's running jump. The pack has no vault capture, but a
        /// run-jump is the closest thing in it: it leaves the ground travelling forward, which is what
        /// root warping needs to land the character on the obstacle's far edge.
        /// </summary>
        private static string CreateVaultEvent(AnimationClip[] clips)
        {
            AnimationClip jump = FindClip(clips, "RunJumpLeft") ?? FindClip(clips, "RunJump") ?? FindClip(clips, "Jump");
            if (jump == null)
            {
                Debug.LogWarning("[Kinema] No jump clip in the pack, so the vault event was skipped. Space will do nothing.");
                return null;
            }

            string path = OutputFolder + "/OpsiveVaultEvent.asset";
            var def = AssetDatabase.LoadAssetAtPath<MotionEventDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MotionEventDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }

            var so = new SerializedObject(def);
            so.FindProperty("_clip").objectReferenceValue = jump;
            // Contact just past the take-off, where the body is over the obstacle.
            so.FindProperty("_contactTime").floatValue = Mathf.Clamp(jump.length * 0.45f, 0.1f, jump.length - 0.05f);
            so.FindProperty("_blendIn").floatValue = 0.12f;
            so.FindProperty("_warpPosition").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Kinema] Vault event built from '{jump.name}' ({jump.length:F2}s). Space near a low obstacle.");
            return path;
        }

        private static AnimationClip FindClip(AnimationClip[] clips, string nameFragment)
        {
            foreach (AnimationClip clip in clips)
                if (clip.name.IndexOf(nameFragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return clip;
            return null;
        }

        private static AnimationClip[] LoadPackClips()
        {
            var clips = new List<AnimationClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { ClipFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;

                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__") && clip.length >= 0.15f)
                        clips.Add(clip);
                }
            }
            return clips.Distinct().OrderBy(c => c.name).ToArray();
        }

        /// <summary>
        /// Picks the rig to bake and display on.
        ///
        /// The pack's own rig is a bare skeleton: 68 joints, valid Humanoid avatar, and no mesh at
        /// all. Baking on it works and the character animates, but there is nothing on screen. So
        /// when the pack rig carries no skin, fall back to the demo's skinned character and let
        /// Humanoid retargeting map the mocap onto it - both rigs are Humanoid, so the clips go
        /// through muscle space and land on whatever proportions the display rig has. Baking on that
        /// same rig keeps the features consistent with what is actually rendered.
        /// </summary>
        private static GameObject ResolveDisplayRig()
        {
            var packRig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if (packRig == null)
            {
                Debug.LogError($"[Kinema] Opsive rig not found at {RigPath}. Import the OmniAnimation pack first.");
                return null;
            }
            if (HasSkinnedMesh(packRig)) return packRig;

            string skinPath = FindSkinnedModel();
            if (skinPath == null)
            {
                Debug.LogError($"[Kinema] The Opsive rig at {RigPath} is a skeleton with no mesh, and no skinned " +
                               $"model was found in {DemoPaths.Character} to display instead. Drop a skinned " +
                               "Humanoid FBX there and re-run.");
                return null;
            }

            // Retargeting only exists in muscle space, so the display rig has to be Humanoid too.
            DemoSetup.EnsureHumanoid(skinPath);
            var skin = AssetDatabase.LoadAssetAtPath<GameObject>(skinPath);
            if (!IsHumanoid(skin))
            {
                Debug.LogError($"[Kinema] '{skinPath}' could not be imported as Humanoid, so the mocap cannot be " +
                               "retargeted onto it. Check the model's rig in the import settings.");
                return null;
            }

            Debug.Log($"[Kinema] Pack rig carries no mesh (skeleton only). Displaying and baking on " +
                      $"'{System.IO.Path.GetFileName(skinPath)}' instead; Humanoid retargeting maps the mocap onto it.");
            return skin;
        }

        private static bool HasSkinnedMesh(GameObject model)
        {
            return model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;
        }

        private static bool IsHumanoid(GameObject model)
        {
            if (model == null) return false;
            GameObject instance = Object.Instantiate(model);
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                return animator != null && animator.isHuman && animator.avatar != null && animator.avatar.isValid;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>First skinned model in the demo's character folder.</summary>
        private static string FindSkinnedModel()
        {
            if (!AssetDatabase.IsValidFolder(DemoPaths.Character)) return null;

            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { DemoPaths.Character }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model != null && HasSkinnedMesh(model)) return path;
            }
            return null;
        }

        /// <summary>Hips + both feet, read off the rig's Humanoid avatar so the names are exact.</summary>
        private static string[] ResolveHumanoidBones(GameObject rig)
        {
            GameObject instance = Object.Instantiate(rig);
            var names = new List<string>();
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || !animator.isHuman) return names.ToArray();

                foreach (HumanBodyBones bone in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    Transform t = animator.GetBoneTransform(bone);
                    if (t != null) names.Add(t.name);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
            return names.ToArray();
        }

        private static void EnsureFolder()
        {
            DemoSceneBuilder.CreateFolderIfMissing(DemoPaths.SampleRoot, "Opsive");
        }

        private static MotionMatchingConfig CreateOrLoadConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<MotionMatchingConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<MotionMatchingConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            return config;
        }

        private static void ConfigureAsset(MotionMatchingConfig config, GameObject rig, AnimationClip[] clips, string[] boneNames)
        {
            var so = new SerializedObject(config);
            so.FindProperty("_rigPrefab").objectReferenceValue = rig;
            so.FindProperty("_bakeFrameRate").intValue = 30;
            // Core-quality options: the phase term needs a nonzero weight, and 66% of this pack is
            // near-idle, so pruning debiases the search besides shrinking it.
            so.FindProperty("_defaultWeights.FootPhase").floatValue = 0.5f;
            so.FindProperty("_pruneIdleDuplicates").boolValue = true;

            SetArray(so.FindProperty("_clips"), clips.Length, (p, i) => p.objectReferenceValue = clips[i]);

            SerializedProperty schema = so.FindProperty("_schema");
            SetArray(schema.FindPropertyRelative("BoneNames"), boneNames.Length, (p, i) => p.stringValue = boneNames[i]);
            // Feet carry the footing read; the hips mostly follow. Weight them accordingly.
            var boneWeights = new float[boneNames.Length];
            for (int i = 0; i < boneWeights.Length; i++)
                boneWeights[i] = boneNames[i].ToLowerInvariant().Contains("foot") ? 1.4f : 1f;
            SetArray(schema.FindPropertyRelative("BoneWeights"), boneWeights.Length, (p, i) => p.floatValue = boneWeights[i]);

            // Tag vocabulary + one whole-clip range per matching keyword.
            var tagNames = TagRules.Select(r => r.Tag).ToArray();
            SetArray(so.FindProperty("_tagNames"), tagNames.Length, (p, i) => p.stringValue = tagNames[i]);
            BuildTagTracks(so.FindProperty("_tagTracks"), clips);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Kinema] Auto-tagged {clips.Length} clips across {tagNames.Length} tags from the pack's naming convention.");
        }

        /// <summary>One track per clip; each keyword found in the name tags the whole clip.</summary>
        private static void BuildTagTracks(SerializedProperty tracks, AnimationClip[] clips)
        {
            tracks.ClearArray();
            int trackIndex = 0;

            foreach (AnimationClip clip in clips)
            {
                var matched = new List<int>();
                for (int t = 0; t < TagRules.Length; t++)
                    if (clip.name.IndexOf(TagRules[t].Keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        matched.Add(t);

                if (matched.Count == 0) continue;

                tracks.InsertArrayElementAtIndex(trackIndex);
                SerializedProperty track = tracks.GetArrayElementAtIndex(trackIndex++);
                track.FindPropertyRelative("Clip").objectReferenceValue = clip;

                SerializedProperty ranges = track.FindPropertyRelative("Ranges");
                SetArray(ranges, matched.Count, (p, i) =>
                {
                    p.FindPropertyRelative("TagIndex").intValue = matched[i];
                    p.FindPropertyRelative("Start").floatValue = 0f;
                    p.FindPropertyRelative("End").floatValue = clip.length;
                });
            }
        }

        private static void SetArray(SerializedProperty array, int size, System.Action<SerializedProperty, int> assign)
        {
            array.ClearArray();
            for (int i = 0; i < size; i++)
            {
                array.InsertArrayElementAtIndex(i);
                assign(array.GetArrayElementAtIndex(i), i);
            }
        }

        #endregion
    }
}
