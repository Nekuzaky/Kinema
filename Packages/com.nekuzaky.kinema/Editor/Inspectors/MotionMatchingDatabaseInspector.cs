using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Database inspector. The asset stores large flat arrays that would be useless (and slow) in a
    /// default inspector, so this draws a curated read-only summary instead and points at the window
    /// for anything deeper.
    /// </summary>
    [CustomEditor(typeof(MotionMatchingDatabase))]
    public sealed class MotionMatchingDatabaseInspector : UnityEditor.Editor
    {
        #region Unity API

        public override void OnInspectorGUI()
        {
            var db = (MotionMatchingDatabase)target;

            if (!db.IsValid)
            {
                MotionMatchingStyles.HelpRow("This database has not been baked yet, or its data is inconsistent.", MessageType.Warning);
                if (GUILayout.Button("Open Motion Matching Window")) MotionMatchingWindow.Open();
                return;
            }

            using (MotionMatchingStyles.BeginSection("Summary"))
            {
                MotionMatchingStyles.KeyValue("Frames", db.FrameCount.ToString("N0"));
                MotionMatchingStyles.KeyValue("Clips", db.ClipCount.ToString());
                MotionMatchingStyles.KeyValue("Dimensions / frame", db.Dimension.ToString());
                MotionMatchingStyles.KeyValue("Bake rate", db.BakeFrameRate + " fps");
                MotionMatchingStyles.KeyValue("Total duration", db.TotalDurationSeconds.ToString("F2") + " s");
                float mb = db.FrameCount * db.Dimension * 4f / (1024f * 1024f);
                MotionMatchingStyles.KeyValue("Feature memory", mb.ToString("F2") + " MB");
                MotionMatchingStyles.KeyValue("Baked", db.BakeDateUtc + " UTC");
            }

            using (MotionMatchingStyles.BeginSection("Clips"))
            {
                for (int i = 0; i < db.ClipCount; i++)
                {
                    MotionClipEntry clip = db.GetClip(i);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"{i:00}  {clip.Name}", GUILayout.Width(190));
                        GUILayout.Label($"{clip.FrameCount} f · {clip.Length:F2}s", MotionMatchingStyles.KeyLabel);
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(clip.Clip == null))
                            if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44)))
                                EditorGUIUtility.PingObject(clip.Clip);
                    }
                }
            }

            if (GUILayout.Button("Open Motion Matching Window")) MotionMatchingWindow.Open();
        }

        #endregion
    }
}
