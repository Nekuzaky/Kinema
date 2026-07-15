using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kinema.MotionMatching.Samples.Editor
{
    /// <summary>
    /// Tools > Kinema > Demo Scene: builds a scene for exercising a whole database.
    ///
    /// The regular demo answers "does locomotion feel right". This one answers "is my data any good"
    /// - a different question that the regular demo cannot address, because the matcher only ever
    /// shows you frames that fit what you happened to be doing. Clips that are never selected are
    /// never seen, so this scene ships an <see cref="AnimationBrowser"/> that can force any clip, and
    /// terrain built to provoke every subsystem: slopes and steps for ground adaptation, a low ledge
    /// for the vault event, open ground for stride warping across the speed range.
    ///
    /// The richest baked config in the project wins, so importing a bigger mocap pack and rerunning
    /// this picks it up with no arguments.
    /// </summary>
    public static class DemoSceneTool
    {
        #region Main API

        private static string ScenePath => DemoPaths.SampleRoot + "/KinemaTestScene.unity";

        [MenuItem("Tools/Kinema/Demo Scene", priority = 0)]
        public static void BuildMenu()
        {
            if (!Build(out string error))
            {
                EditorUtility.DisplayDialog("Kinema", error, "OK");
                return;
            }
            EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>Headless entry point (Unity -executeMethod).</summary>
        public static void BuildFromCommandLine()
        {
            if (!Build(out string error)) Debug.LogError("[Kinema] " + error);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Kinema/Demo Scene", validate = true)]
        private static bool ValidateBuild() => FindRichestConfig(out _, out _) != null;

        #endregion

        #region Tools and Utilities — Build

        private static bool Build(out string error)
        {
            error = null;

            MotionMatchingConfig config = FindRichestConfig(out MotionMatchingDatabase database, out string databasePath);
            if (config == null)
            {
                error = "No baked database found. Run Kinema > Motion Matching > Setup Full Demo From FBX " +
                        "(or Setup Demo From Opsive Pack) first, then build the test scene.";
                return false;
            }
            if (config.RigPrefab == null)
            {
                error = $"'{config.name}' has no rig prefab assigned, so there is nothing to animate.";
                return false;
            }

            Debug.Log($"[Kinema] Test scene using '{config.name}': {database.ClipCount} clips, " +
                      $"{database.FrameCount:N0} frames, {(database.HasTags ? database.TagNames.Length : 0)} tags.");

            // A skinless rig animates correctly and renders nothing, which looks exactly like the
            // scene failing to build. Say so rather than handing over an empty-looking viewport.
            if (config.RigPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
                Debug.LogWarning($"[Kinema] '{config.RigPrefab.name}' has no skinned mesh, so the character will be " +
                                 "invisible in the scene. It still animates - select it and watch the Transforms, or " +
                                 "rebake against a rig that carries a skin.");

            DemoSceneBuilder.EnsureFolders();
            DemoSceneBuilder.NewDemoScene();

            (Material ground, Material obstacle) = DemoSceneBuilder.CreateMaterials();
            DemoSceneBuilder.BuildEnvironment(ground, obstacle);
            BuildTestTerrain(obstacle);

            GameObject character = BuildCharacter(config.RigPrefab, databasePath, database);
            DemoSceneBuilder.WireCamera(character.transform);
            DemoSceneBuilder.ConfigureSun();
            Selection.activeGameObject = character;

            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Kinema] Test scene saved → {ScenePath}");
            return true;
        }

        /// <summary>
        /// Terrain that provokes the subsystems the flat demo never touches: without a slope or a
        /// step, ground adaptation has nothing to correct and looks identical to it being broken.
        /// </summary>
        private static void BuildTestTerrain(Material material)
        {
            var root = new GameObject("Test Terrain");

            // Ramps: baked clips were authored flat, so this is where foot conforming shows.
            DemoSceneBuilder.CreateBox(root.transform, new Vector3(-12f, 0.6f, 0f), new Vector3(5f, 0.4f, 9f), Quaternion.Euler(0f, 0f, 14f), material);
            DemoSceneBuilder.CreateBox(root.transform, new Vector3(-12f, 0.6f, 11f), new Vector3(5f, 0.4f, 9f), Quaternion.Euler(9f, 0f, 0f), material);

            // Staircase: each tread forces a discrete pelvis drop and foot replant.
            for (int i = 0; i < 6; i++)
            {
                float height = 0.16f * (i + 1);
                DemoSceneBuilder.CreateBox(root.transform, new Vector3(12f, height * 0.5f, -4f + i * 0.45f),
                    new Vector3(4f, height, 0.45f), Quaternion.identity, material);
            }

            // Low ledge for the vault event, at the height the demo trigger expects.
            DemoSceneBuilder.CreateBox(root.transform, new Vector3(0f, 0.45f, -9f), new Vector3(6f, 0.9f, 0.5f), Quaternion.identity, material);

            // Long open lane: room to accelerate through the full speed range and watch stride warp.
            DemoSceneBuilder.CreateBox(root.transform, new Vector3(0f, 0.01f, 20f), new Vector3(3f, 0.02f, 26f), Quaternion.identity, material);
        }

        private static GameObject BuildCharacter(GameObject rig, string databasePath, MotionMatchingDatabase database)
        {
            var character = (GameObject)PrefabUtility.InstantiatePrefab(rig);
            character.name = "Character";
            character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

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
            character.AddComponent<AnimationBrowser>();

            // Only meaningful on a tagged set; on an untagged one it would warn every run.
            if (database.HasTags) character.AddComponent<StanceTagController>();

            // Asset references must point at the on-disk asset, not an in-memory instance.
            var dbRef = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(databasePath);
            DemoSceneBuilder.SetObjectReference(controller, "_database", dbRef);
            DemoSceneBuilder.SetObjectReference(controller, "_animator", animator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

            return character;
        }

        #endregion

        #region Tools and Utilities — Discovery

        /// <summary>
        /// Picks the baked config with the most frames: "test all the animations" means the richest
        /// set available, and it keeps the tool argument-free as packs are added.
        /// </summary>
        private static MotionMatchingConfig FindRichestConfig(out MotionMatchingDatabase database, out string databasePath)
        {
            database = null;
            databasePath = null;
            MotionMatchingConfig best = null;

            foreach (string guid in AssetDatabase.FindAssets("t:MotionMatchingConfig"))
            {
                string configPath = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<MotionMatchingConfig>(configPath);
                if (config == null || config.RigPrefab == null) continue;

                string candidatePath = configPath.Substring(0, configPath.Length - ".asset".Length) + "Database.asset";
                var candidate = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(candidatePath);
                if (candidate == null || !candidate.IsValid) continue;

                if (database != null && candidate.FrameCount <= database.FrameCount) continue;
                best = config;
                database = candidate;
                databasePath = candidatePath;
            }
            return best;
        }

        #endregion
    }
}
