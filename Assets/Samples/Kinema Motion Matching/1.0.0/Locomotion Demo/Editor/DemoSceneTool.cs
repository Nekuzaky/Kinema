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

        /// <summary>Which scene to generate. All share one bake; only the environment and who drives the characters differ.</summary>
        private enum DemoScene { Test, Parkour, Sandbox }

        private static string ScenePath(DemoScene flavor) => DemoPaths.SampleRoot + flavor switch
        {
            DemoScene.Parkour => "/KinemaParkourScene.unity",
            DemoScene.Sandbox => "/KinemaSandboxScene.unity",
            _ => "/KinemaTestScene.unity"
        };

        [MenuItem("Tools/Kinema/Demo Scene", priority = 0)]
        public static void BuildTestMenu() => BuildMenu(DemoScene.Test);

        [MenuItem("Tools/Kinema/Scenes/Parkour", priority = 1)]
        public static void BuildParkourMenu() => BuildMenu(DemoScene.Parkour);

        [MenuItem("Tools/Kinema/Scenes/Sandbox", priority = 2)]
        public static void BuildSandboxMenu() => BuildMenu(DemoScene.Sandbox);

        private static void BuildMenu(DemoScene flavor)
        {
            if (!Build(flavor, out string error))
            {
                EditorUtility.DisplayDialog("Kinema", error, "OK");
                return;
            }
            EditorSceneManager.OpenScene(ScenePath(flavor));
        }

        /// <summary>Headless entry point (Unity -executeMethod). Builds the test scene.</summary>
        public static void BuildFromCommandLine()
        {
            if (!Build(DemoScene.Test, out string error)) Debug.LogError("[Kinema] " + error);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>Headless: build every scene flavor in one run (used to verify them all).</summary>
        public static void BuildAllFromCommandLine()
        {
            foreach (DemoScene flavor in new[] { DemoScene.Test, DemoScene.Parkour, DemoScene.Sandbox })
                if (!Build(flavor, out string error)) Debug.LogError($"[Kinema] {flavor}: {error}");
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
        private static bool Build(DemoScene flavor, out string error)
        {
            error = null;
            DemoBake bake;

            // The Opsive path is gated behind KINEMA_OPSIVE and off by default. The published package
            // integrates no third-party product a buyer may not own; someone who has the OmniAnimation
            // pack adds the define (Project Settings > Player > Scripting Define Symbols) to switch it
            // on. Nothing about it ships enabled.
#if KINEMA_OPSIVE
            if (OpsivePackSetup.PackAvailable)
            {
                if (!OpsivePackSetup.TryBake(out bake))
                {
                    error = "The mocap pack is installed but could not be baked. See the Console for the reason.";
                    return false;
                }
                BuildScene(bake, flavor);
                return true;
            }
#endif

            if (DemoSetup.FbxAvailable)
            {
                if (!DemoSetup.TryBake(out bake))
                {
                    error = "An FBX was found but could not be baked. See the Console for the reason.";
                    return false;
                }
            }
            else
            {
                error = $"Nothing to build from. Drop a Humanoid character FBX in {DemoPaths.Character} " +
                        "and run this again - the demo bakes its clips, or generates a locomotion set " +
                        "if it is just a skin.";
                return false;
            }

            BuildScene(bake, flavor);
            return true;
        }

        /// <summary>
        /// Takes paths rather than objects on purpose. Creating the scene unloads unreferenced assets
        /// and destroys their instances, so anything loaded beforehand comes back as a Unity
        /// fake-null: the C# reference is alive, `== null` is true, and the wiring silently drops.
        /// Everything is therefore loaded from disk after the scene exists.
        /// </summary>
        private static void BuildScene(DemoBake bake, DemoScene flavor)
        {
            DemoSceneBuilder.EnsureFolders();
            DemoSceneBuilder.NewDemoScene();

            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(bake.RigPath);
            var database = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(bake.DatabasePath);
            var vaultEvent = AssetDatabase.LoadAssetAtPath<MotionEventDefinition>(bake.VaultEventPath);
            var jumpMoving = AssetDatabase.LoadAssetAtPath<MotionEventDefinition>(bake.JumpMovingEventPath);
            var jumpIdle = AssetDatabase.LoadAssetAtPath<MotionEventDefinition>(bake.JumpIdleEventPath);

            Debug.Log($"[Kinema] {flavor} scene from '{rig.name}': {database.ClipCount} clips, {database.FrameCount:N0} frames, " +
                      $"{(database.HasTags ? database.TagNames.Length : 0)} tags.");

            if (rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
                Debug.LogWarning($"[Kinema] '{rig.name}' has no skinned mesh, so the character will be invisible. " +
                                 "It still animates - rebake against a rig that carries a skin.");

            (Material ground, Material obstacle) = DemoPresentation.CreateMaterials();
            DemoSceneBuilder.BuildEnvironment(ground, obstacle);

            switch (flavor)
            {
                case DemoScene.Parkour: BuildParkourCourse(obstacle); break;
                case DemoScene.Sandbox: BuildSandboxArena(obstacle); break;
                default: BuildTestTerrain(obstacle); break;
            }

            // The player: the full stack plus input, recording and ghost director.
            (GameObject player, _) = BuildBody(rig, database, vaultEvent, jumpMoving, jumpIdle, "Character", Vector3.zero, autoVault: false);
            AddPlayerDrivers(player, database);
            DemoSceneBuilder.WireCamera(player.transform);

            // AI: the same motion matching stack driven by an AI provider instead of input - the
            // point being that the controller is input-agnostic, so an NPC and the player run
            // identical locomotion. Each keeps its collision motor (unlike ghosts) and auto-vaults.
            switch (flavor)
            {
                case DemoScene.Parkour:
                    // One chaser running the course behind the player.
                    BuildFollowerAI(rig, database, vaultEvent, jumpMoving, jumpIdle, new Vector3(-24f, 0f, -26f), player.transform);
                    break;
                case DemoScene.Sandbox:
                    // A crowd of wanderers: many matched characters on screen at once.
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = i / 6f * Mathf.PI * 2f;
                        var pos = new Vector3(Mathf.Cos(angle) * 6f, 0f, Mathf.Sin(angle) * 6f);
                        BuildWandererAI(rig, database, vaultEvent, jumpMoving, jumpIdle, pos);
                    }
                    break;
            }

            // Every AI character is a full matcher, so a crowd is where the scaling work has to show
            // up or it may as well not exist. The batch overlaps their searches on Burst's worker
            // threads instead of letting each block on its own; it auto-collects every controller in
            // the scene when it enables, so it needs no wiring per character.
            if (flavor != DemoScene.Test) DemoSceneBuilder.BuildSearchBatch();

            DemoPresentation.Apply(player, Camera.main);
            Selection.activeGameObject = player;

            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath(flavor));

            string extra = flavor switch
            {
                DemoScene.Parkour => "Run the course; an AI chases and auto-vaults behind you.",
                DemoScene.Sandbox => "Six AI wanderers share the arena - watch the matcher run on all of them.",
                _ => "R record, G ghost, K clear ghosts."
            };
            Debug.Log($"[Kinema] {flavor} scene saved → {ScenePath(flavor)}. Play: WASD move, Space vault/jump, C crouch. {extra}");
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

        /// <summary>
        /// Parkour: a continuous circuit of everything the event system does - a run of vault walls,
        /// a gap-jump gauntlet, an ascending stair of platforms, then a long return straight. Laid
        /// out as a loop so a chasing AI and the player both keep moving through it.
        /// </summary>
        private static void BuildParkourCourse(Material material)
        {
            var root = new GameObject("Parkour Course");
            float x = -24f;

            DemoSceneBuilder.CreateBox(root.transform, new Vector3(x, 0.05f, -26f), new Vector3(4f, 0.1f, 6f), Quaternion.identity, material);

            // Vault run: four walls across the trigger's height window, a stride apart.
            for (int i = 0; i < 4; i++)
                DemoSceneBuilder.CreateBox(root.transform, new Vector3(x, 0.35f + i * 0.03f, -20f + i * 3f),
                    new Vector3(3.5f, 0.7f + i * 0.06f, 0.4f), Quaternion.identity, material);

            // Gap-jump gauntlet: five islands, gaps within the run-jump's reach.
            float z = -4f;
            for (int i = 0; i < 5; i++)
            {
                DemoSceneBuilder.CreateBox(root.transform, new Vector3(x, 0.3f, z), new Vector3(3f, 0.6f, 2.6f), Quaternion.identity, material);
                z += 2.6f + 1.5f;
            }

            // Ascending platforms, each a vaultable step from the last.
            z += 3f;
            for (int i = 0; i < 5; i++)
            {
                float h = 0.6f + i * 0.5f;
                DemoSceneBuilder.CreateBox(root.transform, new Vector3(x, h * 0.5f, z + i * 2.4f), new Vector3(3.5f, h, 2f), Quaternion.identity, material);
            }

            // Return straight along the far side, so the circuit closes.
            DemoSceneBuilder.CreateBox(root.transform, new Vector3(x + 9f, 0.02f, 4f), new Vector3(3f, 0.04f, 44f), Quaternion.identity, material);
        }

        /// <summary>
        /// Sandbox: an open arena with a scatter of low, vaultable blocks and a soft perimeter, room
        /// for a crowd of wandering AIs and the player to move without a scripted route - the "just
        /// let it run and watch the matcher" scene.
        /// </summary>
        private static void BuildSandboxArena(Material material)
        {
            var root = new GameObject("Sandbox");

            // A ring of low blocks: obstacles to route around and auto-vault, not a course.
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                float r = 10f + (i % 3) * 2f;
                var pos = new Vector3(Mathf.Cos(a) * r, 0.35f, Mathf.Sin(a) * r);
                DemoSceneBuilder.CreateBox(root.transform, pos, new Vector3(2f, 0.7f, 2f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), material);
            }

            // A couple of ramps for the ground-adaptation IK to work against.
            DemoSceneBuilder.CreateBox(root.transform, new Vector3(-14f, 0.5f, 0f), new Vector3(5f, 0.3f, 8f), Quaternion.Euler(0f, 0f, 12f), material);
            DemoSceneBuilder.CreateBox(root.transform, new Vector3(14f, 0.5f, 0f), new Vector3(5f, 0.3f, 8f), Quaternion.Euler(0f, 0f, -12f), material);
        }

        /// <summary>
        /// The shared body every character has - player or AI: the motion matching stack, collision
        /// motor, IK, tuned weights and the vault trigger. What drives it (input vs AI) is added on
        /// top by the caller, which is the whole point: the controller does not know or care.
        /// </summary>
        private static (GameObject body, MotionMatchingController controller) BuildBody(
            GameObject rig, MotionMatchingDatabase dbRef, MotionEventDefinition vaultEvent,
            MotionEventDefinition jumpMoving, MotionEventDefinition jumpIdle, string name, Vector3 position, bool autoVault)
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(rig);
            body.name = name;
            body.transform.SetPositionAndRotation(position, Quaternion.identity);

            // Explicit null checks: GetComponent returns a fake-null Unity object, which ?? does not catch.
            var cc = body.GetComponent<CharacterController>();
            if (cc == null) cc = body.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.radius = 0.3f;
            cc.height = 1.8f;
            // Unity's skin width default (0.08) is tuned for the 0.5 default radius; on this 0.3
            // capsule it is 27% of the radius, which the docs name as a jitter cause. 10% + zero
            // min-move is the documented anti-jitter setup.
            cc.skinWidth = cc.radius * 0.1f;
            cc.minMoveDistance = 0f;

            var animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            animator.applyRootMotion = true;

            var controller = body.AddComponent<MotionMatchingController>();
            body.AddComponent<FootLockIK>();
            body.AddComponent<GroundAdaptationIK>();
            body.AddComponent<CharacterMotor>();

            // Before the vault trigger, which reads it: one sensor per character, shared by everything
            // that needs to know what is ahead, sensing on a timer instead of per frame per consumer.
            body.AddComponent<ObstacleSensor>();

            if (vaultEvent != null || jumpMoving != null || jumpIdle != null)
            {
                var vault = body.AddComponent<VaultTrigger>();
                DemoSceneBuilder.SetObjectReference(vault, "_vaultEvent", vaultEvent);
                DemoSceneBuilder.SetObjectReference(vault, "_jumpMovingEvent", jumpMoving);
                DemoSceneBuilder.SetObjectReference(vault, "_jumpIdleEvent", jumpIdle);
                if (autoVault) DemoSceneBuilder.SetBool(vault, "_autoVault", true);
            }

            DemoSceneBuilder.SetObjectReference(controller, "_database", dbRef);
            DemoSceneBuilder.SetObjectReference(controller, "_animator", animator);
            DemoSceneBuilder.SetFloat(controller, "_clipChangeCost", 0.25f);
            // Trajectory-dominant weights so intent beats a momentarily-idle pose (the idle/walk stutter).
            DemoSceneBuilder.SetFloat(controller, "_weights.TrajectoryPosition", 1.6f);
            DemoSceneBuilder.SetFloat(controller, "_weights.TrajectoryDirection", 1.3f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

            return (body, controller);
        }

        /// <summary>Player-only drivers: input, recording, ghosts, quality probe, stance filter.</summary>
        private static void AddPlayerDrivers(GameObject player, MotionMatchingDatabase dbRef)
        {
            player.AddComponent<MotionQualityProbe>();
            player.AddComponent<SessionRecorder>();
            player.AddComponent<PoseRecorder>();
            player.AddComponent<LocomotionInputProvider>();
            player.AddComponent<GhostReplayDirector>();
            if (dbRef.HasTags) player.AddComponent<StanceTagController>();
        }

        private static void BuildFollowerAI(GameObject rig, MotionMatchingDatabase db,
            MotionEventDefinition vault, MotionEventDefinition jumpM, MotionEventDefinition jumpI, Vector3 position, Transform target)
        {
            (GameObject ai, _) = BuildBody(rig, db, vault, jumpM, jumpI, "AI Follower", position, autoVault: true);
            // The brain-driven stack: a scripted brain issuing Follow goals, translated to locomotion
            // by the command provider. Swap the brain for an LLMAIBrain and the same agent is
            // model-directed with no other change - and it shows up in the window's AI tab either way.
            var brain = ai.AddComponent<ScriptedAIBrain>();
            DemoSceneBuilder.SetEnum(brain, "_behaviour", 2); // FollowPlayer
            var provider = ai.AddComponent<AICommandProvider>();
            AddSearchLOD(ai);
            AddTagFilter(ai, db);
            // This one auto-vaults, so anything inside the vault window must not read as an obstacle:
            // 1.2 clears the trigger's 1.15 ceiling. Otherwise avoidance steers it around every wall
            // it was built to vault, and the chase never shows a vault at all.
            DemoSceneBuilder.SetFloat(provider, "_passableHeight", 1.2f);
            Tint(ai, new Color(1f, 0.55f, 0.4f)); // warm, to read apart from the player
        }

        private static void BuildWandererAI(GameObject rig, MotionMatchingDatabase db,
            MotionEventDefinition vault, MotionEventDefinition jumpM, MotionEventDefinition jumpI, Vector3 position)
        {
            (GameObject ai, _) = BuildBody(rig, db, vault, jumpM, jumpI, "AI Wanderer", position, autoVault: false);
            ai.AddComponent<ScriptedAIBrain>(); // Wander by default
            ai.AddComponent<AICommandProvider>();
            AddSearchLOD(ai);
            AddTagFilter(ai, db);
            Tint(ai, new Color(0.5f, 0.8f, 0.55f)); // green, distinct from player and follower
        }

        /// <summary>
        /// Narrows what an AI may pick to what it can plausibly be doing. The player gets this from
        /// its StanceTagController, which has to exist anyway because crouching is a thing the player
        /// asks for; an AI has no such component and so searched the entire database - crouch clips
        /// and jump clips included, both of which are perfectly good matches for walking as far as
        /// the cost function is concerned. That is where "the AI does random things" came from: it
        /// was crouch-walking and running on jump clips because nobody had ruled them out.
        /// </summary>
        private static void AddTagFilter(GameObject agent, MotionMatchingDatabase database)
        {
            if (!database.HasTags) return; // nothing to filter on; the component would only warn.
            agent.AddComponent<LocomotionTagFilter>();
        }

        /// <summary>
        /// Search LOD on an AI character, keyed to the camera. Only AI gets this: the player is
        /// always the thing you are looking at, so degrading its search cadence would be visible
        /// immediately, while a wanderer across the arena can search a quarter as often and no one
        /// can tell. This only stretches the search interval - playback and IK are untouched, so a
        /// distant agent still moves smoothly, it just re-decides less often.
        /// </summary>
        private static void AddSearchLOD(GameObject agent)
        {
            var lod = agent.AddComponent<MotionMatchingLOD>();
            DemoSceneBuilder.SetObjectReference(lod, "_controller", agent.GetComponent<MotionMatchingController>());
            if (Camera.main != null)
                DemoSceneBuilder.SetObjectReference(lod, "_referencePoint", Camera.main.transform);
        }

        /// <summary>Tints every renderer with a shared instance of a material coloured for this character type.</summary>
        private static void Tint(GameObject character, Color color)
        {
            var renderers = character.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0 || renderers[0].sharedMaterial == null) return;
            var mat = new Material(renderers[0].sharedMaterial);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            foreach (var r in renderers) r.sharedMaterial = mat;
        }

        #endregion

    }
}
