using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Config inspector: the stock fields plus a computed schema readout and one-click bake, so the
    /// asset is usable straight from the Project window without opening the full window.
    /// </summary>
    [CustomEditor(typeof(MotionMatchingConfig))]
    public sealed class MotionMatchingConfigInspector : UnityEditor.Editor
    {
        #region Unity API

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (MotionMatchingConfig)target;
            FeatureSchema schema = config.Schema;

            EditorGUILayout.Space();
            using (MotionMatchingStyles.BeginSection("Computed Schema"))
            {
                MotionMatchingStyles.KeyValue("Trajectory points", schema.TrajectoryPointCount.ToString());
                MotionMatchingStyles.KeyValue("Bones", schema.BoneCount.ToString());
                MotionMatchingStyles.KeyValue("Feature dimensions", schema.Dimension.ToString());
            }

            bool ready = config.IsReadyToBake(out string reason);
            if (!ready) MotionMatchingStyles.HelpRow(reason, MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!ready))
                {
                    GUI.backgroundColor = MotionMatchingStyles.Accent;
                    if (GUILayout.Button("Bake Database", GUILayout.Height(26)))
                        Bake(config);
                    GUI.backgroundColor = Color.white;
                }
                if (GUILayout.Button("Open Window", GUILayout.Height(26), GUILayout.Width(110)))
                    MotionMatchingWindow.Open();
            }
        }

        #endregion

        #region Tools and Utilities

        private static void Bake(MotionMatchingConfig config)
        {
            // Rebake this config's existing database in place. Passing null instead created a fresh
            // asset on every click: the scene's controllers kept referencing the old one, so the bake
            // appeared to change nothing while "XDatabase 1", "XDatabase 2"... piled up beside it.
            BakeReport report = MotionMatchingBaker.Bake(config, MotionMatchingBaker.FindDatabaseFor(config));
            if (report.Success)
            {
                EditorGUIUtility.PingObject(report.Database);
                Debug.Log($"[MotionMatching] Baked {report.FrameCount:N0} frames → {report.DatabasePath}", report.Database);
            }
            else
            {
                EditorUtility.DisplayDialog("Bake Failed", report.Error, "OK");
            }
        }

        #endregion
    }
}
