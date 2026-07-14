using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kinema.MotionMatching.Samples.Editor
{
    /// <summary>
    /// Builds the demo environment (ground, obstacles, camera, sun) and a placeholder character.
    /// The environment helpers are reused by <see cref="DemoSetup"/> when a real rig is available, so
    /// there is exactly one definition of "what the demo scene looks like".
    /// </summary>
    public static class DemoSceneBuilder
    {
        #region Main API

        internal static string DemoFolder => DemoPaths.SampleRoot;
        internal static string ScenePath => DemoPaths.ScenePath;

        [MenuItem("Kinema/Motion Matching/Build Demo Scene (placeholder)", priority = 21)]
        public static void BuildMenu()
        {
            string path = BuildPlaceholder();
            EditorSceneManager.OpenScene(path);
            Debug.Log($"[Kinema] Placeholder demo built → {path}. Use 'Setup Full Demo From FBX' to wire a real rig.");
        }

        /// <summary>Entry point for headless generation (Unity -executeMethod).</summary>
        public static void BuildFromCommandLine()
        {
            string path = BuildPlaceholder();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Kinema] Placeholder demo built (batch) → {path}");
        }

        #endregion

        #region Tools and Utilities — Reusable scene pieces

        internal static Scene NewDemoScene() => EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        internal static (Material ground, Material obstacle) CreateMaterials()
        {
            Material ground = CreateLit(DemoFolder + "/Materials/Ground.mat", new Color(0.22f, 0.23f, 0.26f));
            Material obstacle = CreateLit(DemoFolder + "/Materials/Obstacle.mat", new Color(0.30f, 0.72f, 1.00f));
            return (ground, obstacle);
        }

        internal static void BuildEnvironment(Material groundMat, Material obstacleMat)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(6f, 1f, 6f); // 60 x 60 m
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            var obstacles = new GameObject("Obstacles");
            CreateBox(obstacles.transform, new Vector3(4f, 0.5f, 3f), new Vector3(2f, 1f, 2f), Quaternion.identity, obstacleMat);
            CreateBox(obstacles.transform, new Vector3(-5f, 1f, 5f), new Vector3(3f, 2f, 1f), Quaternion.Euler(0f, 25f, 0f), obstacleMat);
            CreateBox(obstacles.transform, new Vector3(-3f, 0.25f, -4f), new Vector3(4f, 0.5f, 4f), Quaternion.identity, obstacleMat);
            CreateBox(obstacles.transform, new Vector3(7f, 0.4f, -3f), new Vector3(6f, 0.8f, 1.2f), Quaternion.Euler(0f, -15f, 0f), obstacleMat);
            CreateBox(obstacles.transform, new Vector3(0f, 0.5f, 8f), new Vector3(4f, 0.4f, 5f), Quaternion.Euler(-14f, 0f, 0f), obstacleMat);
        }

        internal static void WireCamera(Transform target)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
            }
            cam.transform.position = new Vector3(0f, 3f, -6f);
            cam.transform.rotation = Quaternion.Euler(18f, 0f, 0f);

            var follow = cam.GetComponent<FollowCamera>();
            if (follow == null) follow = cam.gameObject.AddComponent<FollowCamera>();
            SetObjectReference(follow, "_target", target);
        }

        internal static void ConfigureSun()
        {
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                light.intensity = 1.1f;
            }
        }

        internal static void CreateBox(Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(parent, false);
            box.transform.SetPositionAndRotation(pos, rot);
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = mat;
        }

        internal static Material CreateLit(string path, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            // Idempotent: reuse the material at this path so repeated builds don't spawn "Ground 1.mat".
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static void SetObjectReference(Object component, string property, Object value)
        {
            var so = new SerializedObject(component);
            SerializedProperty prop = so.FindProperty(property);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        internal static void EnsureFolders()
        {
            CreateFolderIfMissing(DemoFolder, "Materials");
        }

        internal static void CreateFolderIfMissing(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
                AssetDatabase.CreateFolder(parent, child);
        }

        #endregion

        #region Tools and Utilities — Placeholder build

        private static string BuildPlaceholder()
        {
            EnsureFolders();
            Scene scene = NewDemoScene();

            (Material ground, Material obstacle) = CreateMaterials();
            BuildEnvironment(ground, obstacle);

            GameObject character = BuildPlaceholderCharacter();
            WireCamera(character.transform);
            ConfigureSun();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            return ScenePath;
        }

        private static GameObject BuildPlaceholderCharacter()
        {
            var character = new GameObject("Character");

            var cc = character.AddComponent<CharacterController>();
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.radius = 0.3f;
            cc.height = 1.8f;

            character.AddComponent<Animator>();
            character.AddComponent<MotionMatchingController>();
            character.AddComponent<CharacterMotor>();
            character.AddComponent<LocomotionInputProvider>();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Placeholder Visual (replace with rig)";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(character.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            visual.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);

            Selection.activeGameObject = character;
            return character;
        }

        #endregion
    }
}
