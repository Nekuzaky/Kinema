using System.Collections.Generic;
using System.Linq;
using Kinema.MotionMatching.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kinema.MotionMatching.Samples.Editor
{
    /// <summary>
    /// Bakes a database from a single FBX dropped in the sample's Character folder: forces the right
    /// import type, resolves real bone names off the avatar, authors a config and bakes.
    ///
    /// This is the fallback source when no mocap pack is installed. If the FBX carries locomotion
    /// clips they are used; if it is just a skin, a procedural locomotion set is generated so the
    /// demo works with nothing but a character. Scene building belongs to
    /// <see cref="DemoSceneTool"/> - there is one definition of the demo scene, not one per source.
    /// </summary>
    public static class DemoSetup
    {
        #region Main API

        private static string CharacterFolder => DemoPaths.Character;
        private static string AnimationsFolder => DemoPaths.Animations;
        private static string ConfigPath => DemoPaths.ConfigPath;
        private static string DatabasePath => DemoPaths.DatabasePath;

        // Below this length a clip is a bind/T-pose, not locomotion — skip it so the bake stays clean.
        private const float MinClipLength = 0.15f;

        /// <summary>First FBX found under the sample's Character folder (null if none present).</summary>
        private static string FbxPath
        {
            get
            {
                if (!AssetDatabase.IsValidFolder(CharacterFolder)) return null;
                foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { CharacterFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return path;
                }
                return null;
            }
        }

        [MenuItem("Tools/Kinema/Inspect Imported FBX", priority = 60)]
        public static void InspectFbx()
        {
            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[Kinema] No model importer at {FbxPath}");
                return;
            }

            Debug.Log($"[Kinema] FBX '{FbxPath}' — animationType={importer.animationType}");

            AnimationClip[] clips = LoadClips();
            Debug.Log($"[Kinema] Clips found: {clips.Length}");
            foreach (AnimationClip c in clips)
                Debug.Log($"   • {c.name} — {c.length:F2}s, looping={c.isLooping}, humanMotion={c.humanMotion}");

            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            GameObject instance = Object.Instantiate(go);
            var animator = instance.GetComponentInChildren<Animator>();
            Debug.Log($"[Kinema] Animator isHuman={(animator != null && animator.isHuman)}");
            if (animator != null && animator.isHuman)
            {
                foreach (HumanBodyBones bone in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    Transform t = animator.GetBoneTransform(bone);
                    Debug.Log($"   {bone} -> {(t != null ? t.name : "MISSING")}");
                }
            }
            Object.DestroyImmediate(instance);
        }

        /// <summary>True when an FBX is present to bake from.</summary>
        internal static bool FbxAvailable => !string.IsNullOrEmpty(FbxPath);

        #endregion

        #region Tools and Utilities

        internal static bool TryBake(out DemoSceneTool.DemoBake bake)
        {
            bake = default;

            string fbxPath = FbxPath;
            if (string.IsNullOrEmpty(fbxPath) || AssetImporter.GetAtPath(fbxPath) is not ModelImporter)
            {
                Debug.LogError($"[Kinema] No FBX found in {CharacterFolder}. Drop a Humanoid rig there and re-run.");
                return false;
            }

            // Prefer real locomotion clips if the user supplied any; otherwise generate a demo set so the
            // scene works with zero manual steps.
            AnimationClip[] realClips = LoadClips();
            GameObject rig;
            AnimationClip[] clips;

            if (realClips.Length > 0)
            {
                EnsureHumanoid(fbxPath);
                rig = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                clips = realClips;
                Debug.Log($"[Kinema] Using {clips.Length} supplied locomotion clip(s) (Humanoid).");
            }
            else
            {
                EnsureGeneric(fbxPath);
                rig = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                clips = ProceduralLocomotionAuthor.Generate(rig, EnsureGeneratedFolder());
                Debug.Log($"[Kinema] No clips supplied — generated {clips.Length} procedural locomotion clip(s) (Generic).");
            }

            if (rig == null)
            {
                Debug.LogError("[Kinema] Could not load the rig.");
                return false;
            }

            string[] boneNames = ResolveBoneNames(rig);
            Debug.Log($"[Kinema] Bones: [{string.Join(", ", boneNames)}]");

            // Vault motion event: procedural clip + event asset (Generic rig only; the demo default).
            string vaultEventPath = null;
            if (realClips.Length == 0)
            {
                AnimationClip vaultClip = ProceduralLocomotionAuthor.GenerateVault(rig, EnsureGeneratedFolder());
                vaultEventPath = CreateOrUpdateVaultEvent(vaultClip);
                Debug.Log("[Kinema] Vault event ready (Space / gamepad South near a low obstacle).");
            }

            MotionMatchingConfig config = CreateOrLoadConfig();
            ConfigureAsset(config, rig, clips, boneNames);

            if (clips.Length == 0)
            {
                Debug.LogError("[Kinema] No clips to bake.");
                return false;
            }

            var existingDb = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(DatabasePath);
            BakeReport report = MotionMatchingBaker.Bake(config, existingDb);
            if (!report.Success)
            {
                Debug.LogError("[Kinema] Bake failed: " + report.Error);
                return false;
            }
            Debug.Log($"[Kinema] Baked {report.FrameCount:N0} frames from {report.ClipCount} clip(s) → {report.DatabasePath}");
            foreach (string w in report.Warnings) Debug.LogWarning("[Kinema] " + w);

            // Paths, not objects: creating the demo scene destroys these instances and leaves Unity
            // fake-nulls behind. The scene builder reloads from disk.
            bake = new DemoSceneTool.DemoBake
            {
                RigPath = fbxPath,
                DatabasePath = DatabasePath,
                VaultEventPath = vaultEventPath
            };
            return true;
        }

        /// <summary>The vault event asset: clip, contact at the hands-plant moment, horizontal warp only.</summary>
        private static string CreateOrUpdateVaultEvent(AnimationClip vaultClip)
        {
            string path = DemoPaths.SampleRoot + "/VaultEvent.asset";
            var def = AssetDatabase.LoadAssetAtPath<MotionEventDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MotionEventDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }

            var so = new SerializedObject(def);
            so.FindProperty("_clip").objectReferenceValue = vaultClip;
            so.FindProperty("_contactTime").floatValue = 0.35f; // hands plant on the edge
            so.FindProperty("_blendIn").floatValue = 0.12f;
            so.FindProperty("_warpPosition").boolValue = true;
            so.FindProperty("_warpRotation").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            return path;
        }

        /// <summary>Forces a model to import as Humanoid (idempotent). Returns false if it isn't a model.</summary>
        internal static bool EnsureHumanoid(string path) => EnsureAnimationType(path, ModelImporterAnimationType.Human);

        /// <summary>Forces a model to import as Generic — required for procedural transform-curve clips.</summary>
        private static bool EnsureGeneric(string path) => EnsureAnimationType(path, ModelImporterAnimationType.Generic);

        private static bool EnsureAnimationType(string path, ModelImporterAnimationType type)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return false;

            if (importer.animationType != type)
            {
                importer.animationType = type;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }
            return true;
        }

        private static string EnsureGeneratedFolder()
        {
            DemoSceneBuilder.CreateFolderIfMissing(CharacterFolder, "Animations");
            DemoSceneBuilder.CreateFolderIfMissing(AnimationsFolder, "Generated");
            return DemoPaths.Generated;
        }

        private static string[] ResolveBoneNames(GameObject rig)
        {
            GameObject instance = Object.Instantiate(rig);
            var animator = instance.GetComponentInChildren<Animator>();
            var names = new List<string>();

            if (animator != null && animator.isHuman)
            {
                foreach (HumanBodyBones bone in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    Transform t = animator.GetBoneTransform(bone);
                    if (t != null) names.Add(t.name);
                }
            }

            if (names.Count == 0) // Fallback: substring search over the hierarchy.
            {
                foreach (Transform t in instance.GetComponentsInChildren<Transform>())
                {
                    string n = t.name.ToLowerInvariant();
                    if (n.EndsWith("hips") || n.EndsWith("leftfoot") || n.EndsWith("rightfoot"))
                        names.Add(t.name);
                }
            }

            Object.DestroyImmediate(instance);
            return names.Distinct().ToArray();
        }

        private static AnimationClip[] LoadClips()
        {
            var clips = new List<AnimationClip>();
            CollectClips(FbxPath, clips);

            // Also pull in any locomotion FBXs dropped into the Animations folder (auto-Humanoid'd).
            if (AssetDatabase.IsValidFolder(AnimationsFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { AnimationsFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                    EnsureHumanoid(path);
                    CollectClips(path, clips);
                }
            }

            // Drop bind/T-poses; keep real motion only.
            return clips.Where(c => c.length >= MinClipLength).Distinct().ToArray();
        }

        private static void CollectClips(string path, List<AnimationClip> into)
        {
            into.AddRange(AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")));
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

            SerializedProperty clipsProp = so.FindProperty("_clips");
            clipsProp.ClearArray();
            for (int i = 0; i < clips.Length; i++)
            {
                clipsProp.InsertArrayElementAtIndex(i);
                clipsProp.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            }

            SerializedProperty bonesProp = so.FindProperty("_schema").FindPropertyRelative("BoneNames");
            bonesProp.ClearArray();
            for (int i = 0; i < boneNames.Length; i++)
            {
                bonesProp.InsertArrayElementAtIndex(i);
                bonesProp.GetArrayElementAtIndex(i).stringValue = boneNames[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        #endregion
    }
}
