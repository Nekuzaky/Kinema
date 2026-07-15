using System.Collections.Generic;
using System.Linq;
using Kinema.MotionMatching.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kinema.MotionMatching.Samples.Editor
{
    /// <summary>
    /// Builds the demo from an Opsive OmniAnimation locomotion pack: a real mocap set with the
    /// coverage motion matching actually needs - walk/run/sprint in every direction, diagonals,
    /// strafes, turn-in-place, and (rarely available elsewhere) proper starts and stops.
    ///
    /// The pack's own rig is used as the demo character: the capture was authored on that skeleton,
    /// so features, contact bone names and playback all stay on one consistent skeleton with no
    /// retargeting guesswork.
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
        private static string ScenePath => OutputFolder + "/KinemaOpsiveDemo.unity";

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

        [MenuItem("Kinema/Motion Matching/Setup Demo From Opsive Pack", priority = 42)]
        public static void SetupMenu() => Build();

        /// <summary>Headless entry point (Unity -executeMethod).</summary>
        public static void BuildFromCommandLine()
        {
            Build();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        #endregion

        #region Tools and Utilities

        private static void Build()
        {
            GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if (rig == null)
            {
                Debug.LogError($"[Kinema] Opsive rig not found at {RigPath}. Import the OmniAnimation pack first.");
                return;
            }

            AnimationClip[] clips = LoadPackClips();
            if (clips.Length == 0)
            {
                Debug.LogError($"[Kinema] No clips found under {ClipFolder}.");
                return;
            }
            Debug.Log($"[Kinema] Opsive pack: {clips.Length} mocap clips found.");

            string[] boneNames = ResolveHumanoidBones(rig);
            if (boneNames.Length == 0)
            {
                Debug.LogError("[Kinema] Could not resolve Humanoid bones on the Opsive rig.");
                return;
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
                return;
            }
            Debug.Log($"[Kinema] Baked {report.FrameCount:N0} frames from {report.ClipCount} clip(s) → {report.DatabasePath}");
            foreach (string w in report.Warnings) Debug.LogWarning("[Kinema] " + w);

            BuildScene(rig);
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

        private static void BuildScene(GameObject rig)
        {
            DemoSceneBuilder.EnsureFolders();
            DemoSceneBuilder.NewDemoScene();

            (Material ground, Material obstacle) = DemoSceneBuilder.CreateMaterials();
            DemoSceneBuilder.BuildEnvironment(ground, obstacle);

            var character = (GameObject)PrefabUtility.InstantiatePrefab(rig);
            character.name = "Character";
            character.transform.position = Vector3.zero;
            character.transform.rotation = Quaternion.identity;

            // Explicit null checks: GetComponent returns a fake-null Unity object, which ?? does not catch.
            var cc = character.GetComponent<CharacterController>();
            if (cc == null) cc = character.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.radius = 0.3f;
            cc.height = 1.8f;

            var animator = character.GetComponent<Animator>();
            if (animator == null) animator = character.AddComponent<Animator>();
            animator.applyRootMotion = true;

            var controller = character.AddComponent<MotionMatchingController>();
            character.AddComponent<FootLockIK>();
            character.AddComponent<GroundAdaptationIK>();
            character.AddComponent<MotionQualityProbe>();
            character.AddComponent<SessionRecorder>();
            character.AddComponent<CharacterMotor>();
            character.AddComponent<LocomotionInputProvider>();
            // Crouch is 44% of this pack. Without a stance filter the search answers a standing
            // query with a crouched frame whose feet happen to line up.
            character.AddComponent<StanceTagController>();

            var dbRef = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(DatabasePath);
            DemoSceneBuilder.SetObjectReference(controller, "_database", dbRef);
            DemoSceneBuilder.SetObjectReference(controller, "_animator", animator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

            DemoSceneBuilder.WireCamera(character.transform);
            DemoSceneBuilder.ConfigureSun();
            Selection.activeGameObject = character;

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Kinema] Opsive demo scene saved → {ScenePath}");
        }

        #endregion
    }
}
