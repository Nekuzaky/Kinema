using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kinema.MotionMatching.Samples.Editor
{
    /// <summary>
    /// Tools > Kinema > Demo Scene: the one and only demo generator.
    ///
    /// It picks its own source, best first - an installed mocap pack, otherwise an FBX dropped in the
    /// sample's Character folder (using its clips, or generating a procedural set if it is just a
    /// skin) - bakes it, and builds the scene. Every source funnels through the same bake contract
    /// and the same scene builder, so "the demo" means one thing rather than one thing per source.
    ///
    /// The scene is built to provoke the subsystems flat ground never touches: slopes and steps for
    /// ground adaptation, a low ledge for the vault event, a long lane for stride warping. It also
    /// carries recording that sends ghosts out to redo your trajectory; clip playback, tags and
    /// takes are driven from the window's Director tab.
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

        /// <summary>What every bake source hands back: paths to the rig, the database and the vault event.</summary>
        internal struct DemoBake
        {
            public string RigPath;
            public string DatabasePath;
            public string VaultEventPath;
            public string JumpMovingEventPath;
            public string JumpIdleEventPath;
        }

        #endregion

        #region Tools and Utilities — Build

        /// <summary>
        /// Source order is quality order: real mocap beats a hand-dropped FBX, which beats nothing.
        /// Each source bakes from scratch, so the scene always matches the data on disk.
        /// </summary>
        private static bool Build(out string error)
        {
            error = null;
            DemoBake bake;

            if (OpsivePackSetup.PackAvailable)
            {
                if (!OpsivePackSetup.TryBake(out bake))
                {
                    error = "The mocap pack is installed but could not be baked. See the Console for the reason.";
                    return false;
                }
            }
            else if (DemoSetup.FbxAvailable)
            {
                if (!DemoSetup.TryBake(out bake))
                {
                    error = "An FBX was found but could not be baked. See the Console for the reason.";
                    return false;
                }
            }
            else
            {
                error = $"Nothing to build from. Install a mocap pack, or drop a character FBX in " +
                        $"{DemoPaths.Character} and run this again.";
                return false;
            }

            BuildScene(bake);
            return true;
        }

        /// <summary>
        /// Takes paths rather than objects on purpose. Creating the scene unloads unreferenced assets
        /// and destroys their instances, so anything loaded beforehand comes back as a Unity
        /// fake-null: the C# reference is alive, `== null` is true, and the wiring silently drops.
        /// Everything is therefore loaded from disk after the scene exists.
        /// </summary>
        private static void BuildScene(DemoBake bake)
        {
            DemoSceneBuilder.EnsureFolders();
            DemoSceneBuilder.NewDemoScene();

            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(bake.RigPath);
            var database = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(bake.DatabasePath);
            var vaultEvent = AssetDatabase.LoadAssetAtPath<MotionEventDefinition>(bake.VaultEventPath);
            var jumpMoving = AssetDatabase.LoadAssetAtPath<MotionEventDefinition>(bake.JumpMovingEventPath);
            var jumpIdle = AssetDatabase.LoadAssetAtPath<MotionEventDefinition>(bake.JumpIdleEventPath);

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

            GameObject character = BuildCharacter(rig, database, vaultEvent, jumpMoving, jumpIdle);
            DemoSceneBuilder.WireCamera(character.transform);
            DemoSceneBuilder.ConfigureSun();
            Selection.activeGameObject = character;

            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[Kinema] Demo scene saved → {ScenePath}. Play, then: WASD move, " +
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

            BuildTraversalCourse(material);
        }

        /// <summary>
        /// A traversal course down one lane: vault walls inside the trigger's height window, gaps
        /// sized to the run-jump's root motion, and rising platforms. Everything the event system
        /// claims to do, laid out so a single run exercises all of it in order.
        /// </summary>
        private static void BuildTraversalCourse(Material material)
        {
            var root = new GameObject("Traversal Course");
            float x = -24f;

            // Runway into the first wall.
            DemoSceneBuilder.CreateBox(root.transform, new Vector3(x, 0.05f, -20f), new Vector3(4f, 0.1f, 10f), Quaternion.identity, material);

            // Three vault walls, spaced for a stride between each, heights across the window.
            foreach (float height in new[] { 0.5f, 0.75f, 1.0f })
            {
                DemoSceneBuilder.CreateBox(root.transform, new Vector3(x, height * 0.5f, -16f + height * 8f),
                    new Vector3(3.5f, height, 0.4f), Quaternion.identity, material);
            }

            // Gapped platforms: jumpable with the run-jump's root motion (~1.5-2 m of travel).
            float z = -2f;
            foreach (float gap in new[] { 1.2f, 1.6f, 2.0f })
            {
                DemoSceneBuilder.CreateBox(root.transform, new Vector3(x, 0.3f, z), new Vector3(3.5f, 0.6f, 3f), Quaternion.identity, material);
                z += 3f + gap;
            }
            DemoSceneBuilder.CreateBox(root.transform, new Vector3(x, 0.3f, z), new Vector3(3.5f, 0.6f, 3f), Quaternion.identity, material);

            // Rising platforms: each step within the vault window from the previous top.
            z += 4f;
            for (int i = 0; i < 4; i++)
            {
                float height = 0.6f + i * 0.55f;
                DemoSceneBuilder.CreateBox(root.transform, new Vector3(x, height * 0.5f, z + i * 2.6f),
                    new Vector3(3.5f, height, 2.2f), Quaternion.identity, material);
            }
        }

        private static GameObject BuildCharacter(GameObject rig, MotionMatchingDatabase dbRef,
            MotionEventDefinition vaultEvent, MotionEventDefinition jumpMoving, MotionEventDefinition jumpIdle)
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

            // Unity's defaults are tuned for the default 0.5 radius: left alone on a 0.3-radius
            // capsule the skin width is 27% of the radius, which its own docs call out as a cause of
            // jitter. The motor also pushes down constantly to stay grounded, so the body ends up
            // oscillating inside that skin - visible as a character that never quite settles, while
            // ghosts (no motor, root motion straight onto the transform) stay smooth.
            cc.skinWidth = cc.radius * 0.1f;
            cc.minMoveDistance = 0f; // "keep at 0 to avoid jittering" - CharacterController docs.

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
            character.AddComponent<GhostReplayDirector>();

            // Only meaningful on a tagged set; on an untagged one it would warn every run.
            if (dbRef.HasTags) character.AddComponent<StanceTagController>();

            if (vaultEvent != null || jumpMoving != null || jumpIdle != null)
            {
                var vault = character.AddComponent<VaultTrigger>();
                DemoSceneBuilder.SetObjectReference(vault, "_vaultEvent", vaultEvent);
                DemoSceneBuilder.SetObjectReference(vault, "_jumpMovingEvent", jumpMoving);
                DemoSceneBuilder.SetObjectReference(vault, "_jumpIdleEvent", jumpIdle);
            }
            else
            {
                Debug.LogWarning("[Kinema] No vault event available, so Space will do nothing in this scene.");
            }

            DemoSceneBuilder.SetObjectReference(controller, "_database", dbRef);
            DemoSceneBuilder.SetObjectReference(controller, "_animator", animator);
            // The mocap set is thin at higher speeds, so an unpenalised search hops between clips
            // several times a second. Each hop blends, and a blend drags the planted foot far enough
            // to break the foot lock. A firmer clip-change cost trades a little responsiveness for
            // feet that stay put - which is what reads as real.
            DemoSceneBuilder.SetFloat(controller, "_clipChangeCost", 0.25f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

            return character;
        }

        #endregion

    }
}
