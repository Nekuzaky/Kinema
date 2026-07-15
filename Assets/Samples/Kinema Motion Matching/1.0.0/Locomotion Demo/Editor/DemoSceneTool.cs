using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kinema.MotionMatching.Samples.Editor
{
    /// <summary>
    /// Tools > Kinema > Demo Scene: bakes everything available and builds the scene to exercise it.
    ///
    /// One trip. When a mocap pack is installed it is baked from scratch here - all of it, every
    /// clip - rather than requiring a separate setup step first. Failing that, the richest database
    /// already baked in the project is used.
    ///
    /// The scene is built to provoke the subsystems flat ground never touches: slopes and steps for
    /// ground adaptation, a low ledge for the vault event, a long lane for stride warping. It also
    /// carries the tools for judging the data rather than just feeling it - an animation browser that
    /// can force any clip, and recording that sends ghosts out to redo your trajectory.
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

        /// <summary>Builds the scene from an already-completed pack bake, so setup runs only once.</summary>
        internal static void BuildSceneFrom(OpsivePackSetup.PackBake bake)
        {
            BuildScene(bake.RigPath, bake.DatabasePath, bake.VaultEventPath);
        }

        #endregion

        #region Tools and Utilities — Build

        private static bool Build(out string error)
        {
            error = null;

            // A mocap pack is the best data available, so bake all of it here rather than leaving the
            // user to discover a separate setup step.
            if (OpsivePackSetup.PackAvailable)
            {
                if (!OpsivePackSetup.TryBake(out OpsivePackSetup.PackBake bake))
                {
                    error = "The mocap pack is installed but could not be baked. See the Console for the reason.";
                    return false;
                }
                BuildSceneFrom(bake);
                return true;
            }

            MotionMatchingConfig config = FindRichestConfig(out MotionMatchingDatabase database, out string databasePath);
            if (config == null)
            {
                error = "No mocap pack and no baked database found. Run Tools > Kinema > Setup > Demo From FBX first, " +
                        "then build the demo scene.";
                return false;
            }
            if (config.RigPrefab == null)
            {
                error = $"'{config.name}' has no rig prefab assigned, so there is nothing to animate.";
                return false;
            }

            BuildScene(AssetDatabase.GetAssetPath(config.RigPrefab), databasePath, DemoPaths.SampleRoot + "/VaultEvent.asset");
            return true;
        }

        /// <summary>
        /// Takes paths rather than objects on purpose. Creating the scene unloads unreferenced assets
        /// and destroys their instances, so anything loaded beforehand comes back as a Unity
        /// fake-null: the C# reference is alive, `== null` is true, and the wiring silently drops.
        /// Everything is therefore loaded from disk after the scene exists.
        /// </summary>
        private static void BuildScene(string rigPath, string databasePath, string vaultEventPath)
        {
            DemoSceneBuilder.EnsureFolders();
            DemoSceneBuilder.NewDemoScene();

            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
            var database = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(databasePath);
            var vaultEvent = AssetDatabase.LoadAssetAtPath<MotionEventDefinition>(vaultEventPath);

            Debug.Log($"[Kinema] Demo scene from '{rig.name}': {database.ClipCount} clips, {database.FrameCount:N0} frames, " +
                      $"{(database.HasTags ? database.TagNames.Length : 0)} tags.");

            // A skinless rig animates correctly and renders nothing, which looks exactly like the
            // scene failing to build. Say so rather than handing over an empty-looking viewport.
            if (rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
                Debug.LogWarning($"[Kinema] '{rig.name}' has no skinned mesh, so the character will be invisible. " +
                                 "It still animates - rebake against a rig that carries a skin.");

            (Material ground, Material obstacle) = DemoSceneBuilder.CreateMaterials();
            DemoSceneBuilder.BuildEnvironment(ground, obstacle);
            BuildTestTerrain(obstacle);

            GameObject character = BuildCharacter(rig, dbRef: database, vaultEvent: vaultEvent);
            DemoSceneBuilder.WireCamera(character.transform);
            DemoSceneBuilder.ConfigureSun();
            Selection.activeGameObject = character;

            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[Kinema] Demo scene saved → {ScenePath}. Play, then: Tab browser, WASD move, " +
                      "Space vault at a low ledge, C crouch, R record, G ghost, K clear ghosts.");
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

            // Low ledge for the vault event, at the height the trigger expects.
            DemoSceneBuilder.CreateBox(root.transform, new Vector3(0f, 0.45f, -9f), new Vector3(6f, 0.9f, 0.5f), Quaternion.identity, material);

            // Long open lane: room to accelerate through the full speed range and watch stride warp.
            DemoSceneBuilder.CreateBox(root.transform, new Vector3(0f, 0.01f, 20f), new Vector3(3f, 0.02f, 26f), Quaternion.identity, material);
        }

        private static GameObject BuildCharacter(GameObject rig, MotionMatchingDatabase dbRef, MotionEventDefinition vaultEvent)
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
            character.AddComponent<PoseRecorder>();
            character.AddComponent<CharacterMotor>();
            character.AddComponent<LocomotionInputProvider>();
            character.AddComponent<AnimationBrowser>();
            character.AddComponent<GhostReplayDirector>();

            // Only meaningful on a tagged set; on an untagged one it would warn every run.
            if (dbRef.HasTags) character.AddComponent<StanceTagController>();

            if (vaultEvent != null)
            {
                var vault = character.AddComponent<VaultTrigger>();
                DemoSceneBuilder.SetObjectReference(vault, "_vaultEvent", vaultEvent);
            }
            else
            {
                Debug.LogWarning("[Kinema] No vault event available, so Space will do nothing in this scene.");
            }

            DemoSceneBuilder.SetObjectReference(controller, "_database", dbRef);
            DemoSceneBuilder.SetObjectReference(controller, "_animator", animator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

            return character;
        }

        #endregion

        #region Tools and Utilities — Discovery

        /// <summary>
        /// Picks the baked config with the most frames: "all the animations" means the richest set
        /// available, and it keeps the tool argument-free as packs are added.
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
