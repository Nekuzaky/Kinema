using System.Collections.Generic;
using System.Linq;
using Kinema.MotionMatching.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kinema.MotionMatching.Samples.Editor
{
    /// <summary>
    /// End-to-end demo bootstrap from a single FBX: forces a Humanoid import, resolves the real bone
    /// names off the avatar, authors a config, bakes the database, and builds a fully wired scene
    /// (rig + controller + collision motor + input + follow camera). One click from "raw FBX" to
    /// "press Play".
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

        [MenuItem("Kinema/Motion Matching/Inspect Imported FBX", priority = 40)]
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

        [MenuItem("Kinema/Motion Matching/Setup Full Demo From FBX", priority = 41)]
        public static void SetupFullDemo() => BuildFullDemo();

        /// <summary>Headless entry point (Unity -executeMethod).</summary>
        public static void BuildFullDemoFromCommandLine()
        {
            BuildFullDemo();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        #endregion

        #region Tools and Utilities

        private static void BuildFullDemo()
        {
            string fbxPath = FbxPath;
            if (string.IsNullOrEmpty(fbxPath) || AssetImporter.GetAtPath(fbxPath) is not ModelImporter)
            {
                Debug.LogError($"[Kinema] No FBX found in {CharacterFolder}. Drop a Humanoid rig there and re-run.");
                return;
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
                return;
            }

            string[] boneNames = ResolveBoneNames(rig);
            Debug.Log($"[Kinema] Bones: [{string.Join(", ", boneNames)}]");

            // Vault motion event: procedural clip + event asset (Generic rig only; the demo default).
            MotionEventDefinition vaultEvent = null;
            if (realClips.Length == 0)
            {
                AnimationClip vaultClip = ProceduralLocomotionAuthor.GenerateVault(rig, EnsureGeneratedFolder());
                vaultEvent = CreateOrUpdateVaultEvent(vaultClip);
                Debug.Log("[Kinema] Vault event ready (Space / gamepad South near a low obstacle).");
            }

            MotionMatchingConfig config = CreateOrLoadConfig();
            ConfigureAsset(config, rig, clips, boneNames);

            MotionMatchingDatabase database = null;
            if (clips.Length > 0)
            {
                var existingDb = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(DatabasePath);
                BakeReport report = MotionMatchingBaker.Bake(config, existingDb);
                if (report.Success)
                {
                    database = report.Database;
                    Debug.Log($"[Kinema] Baked {report.FrameCount:N0} frames from {report.ClipCount} clip(s) → {report.DatabasePath}");
                    foreach (string w in report.Warnings) Debug.LogWarning("[Kinema] " + w);
                }
                else
                {
                    Debug.LogError("[Kinema] Bake failed: " + report.Error);
                }
            }

            BuildScene(rig, database, vaultEvent);
        }

        /// <summary>The vault event asset: clip, contact at the hands-plant moment, horizontal warp only.</summary>
        private static MotionEventDefinition CreateOrUpdateVaultEvent(AnimationClip vaultClip)
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
            return def;
        }

        /// <summary>Forces a model to import as Humanoid (idempotent). Returns false if it isn't a model.</summary>
        private static bool EnsureHumanoid(string path) => EnsureAnimationType(path, ModelImporterAnimationType.Human);

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

        private static void BuildScene(GameObject rig, MotionMatchingDatabase database, MotionEventDefinition vaultEvent = null)
        {
            DemoSceneBuilder.EnsureFolders();
            DemoSceneBuilder.NewDemoScene();

            (Material ground, Material obstacle) = DemoSceneBuilder.CreateMaterials();
            DemoSceneBuilder.BuildEnvironment(ground, obstacle);

            var character = (GameObject)PrefabUtility.InstantiatePrefab(rig);
            character.name = "Character";
            character.transform.position = Vector3.zero;
            character.transform.rotation = Quaternion.identity;

            var cc = character.GetComponent<CharacterController>();
            if (cc == null) cc = character.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.radius = 0.3f;
            cc.height = 1.8f;

            var animator = character.GetComponent<Animator>();
            if (animator == null) animator = character.AddComponent<Animator>();

            var controller = character.AddComponent<MotionMatchingController>();
            character.AddComponent<FootLockIK>();
            character.AddComponent<CharacterMotor>();
            character.AddComponent<LocomotionInputProvider>();

            // The bake's AssetDatabase.Refresh reimports freshly created assets and kills their
            // in-memory instances (Unity fake-null), so the disk is the source of truth here.
            var vaultRef = AssetDatabase.LoadAssetAtPath<MotionEventDefinition>(DemoPaths.SampleRoot + "/VaultEvent.asset");
            if (vaultRef != null)
            {
                var vault = character.AddComponent<VaultTrigger>();
                DemoSceneBuilder.SetObjectReference(vault, "_vaultEvent", vaultRef);
            }

            // Reload from disk so the reference is a persisted asset (guid-resolvable) at save time.
            MotionMatchingDatabase dbRef = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(DatabasePath) ?? database;
            Debug.Log($"[Kinema] Assigning database ref: {(dbRef != null ? dbRef.name : "NULL")}");
            DemoSceneBuilder.SetObjectReference(controller, "_database", dbRef);
            DemoSceneBuilder.SetObjectReference(controller, "_animator", animator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

            DemoSceneBuilder.WireCamera(character.transform);
            DemoSceneBuilder.ConfigureSun();
            Selection.activeGameObject = character;

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, DemoSceneBuilder.ScenePath);
            Debug.Log($"[Kinema] Full demo scene saved → {DemoSceneBuilder.ScenePath}");
        }

        #endregion
    }
}
