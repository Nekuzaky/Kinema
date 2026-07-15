using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kinema.MotionMatching.SmokeTest.Editor
{
    /// <summary>
    /// Builds the standalone smoke-test player (see <see cref="StandaloneSmokeTest"/>): creates a
    /// scene containing only the smoke-test bootstrap, builds a Windows64 player around it, and exits
    /// with a non-zero code on any build error so a wrapper script can rely on the exit code.
    /// Headless: <c>Unity -batchmode -executeMethod Kinema.MotionMatching.SmokeTest.Editor.BuildSmokeTest.Build
    /// -smokeOutput &lt;dir&gt;</c>. The scene is created in memory and saved to a temp asset that is
    /// deleted after the build - nothing is left in Assets.
    /// </summary>
    public static class BuildSmokeTest
    {
        private const string TempScenePath = "Assets/StandaloneSmokeTest/SmokeScene_Temp.unity";

        public static void Build()
        {
            string output = ReadArg("-smokeOutput") ?? "Build/SmokeTest";

            // URP's build preprocessor refuses to build while the GlobalSettings asset is pending
            // migration to this Editor version (happens whenever the project was last saved by a
            // different 6000.3.x patch). The migration itself runs when the asset loads; it just
            // never gets persisted in batchmode unless something saves it - so save it.
            var urpSettings = AssetDatabase.LoadMainAssetAtPath("Assets/Settings/UniversalRenderPipelineGlobalSettings.asset");
            if (urpSettings != null)
            {
                EditorUtility.SetDirty(urpSettings);
                AssetDatabase.SaveAssets();
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("SmokeTest", typeof(StandaloneSmokeTest));
            if (!EditorSceneManager.SaveScene(scene, TempScenePath))
            {
                Debug.Log("[KinemaSmokeBuild] FAIL - could not save temp scene");
                EditorApplication.Exit(1);
                return;
            }

            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { TempScenePath },
                    locationPathName = output + "/KinemaSmoke.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                };

                var report = BuildPipeline.BuildPlayer(options);
                bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
                Debug.Log($"[KinemaSmokeBuild] {(ok ? "PASS" : "FAIL")} - result {report.summary.result}, " +
                          $"errors {report.summary.totalErrors}, size {report.summary.totalSize / (1024 * 1024)} MB, " +
                          $"time {report.summary.totalTime.TotalSeconds:F0}s");
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.Log($"[KinemaSmokeBuild] FAIL - {e.Message}");
                EditorApplication.Exit(1);
            }
            finally
            {
                AssetDatabase.DeleteAsset(TempScenePath);
            }
        }

        private static string ReadArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
