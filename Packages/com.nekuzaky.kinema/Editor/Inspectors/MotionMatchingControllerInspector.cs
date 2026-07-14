using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Controller inspector: stock fields, quick actions, an at-a-glance live readout in play mode,
    /// and scene-view handles that draw the desired vs candidate trajectory with a cost label.
    /// </summary>
    [CustomEditor(typeof(MotionMatchingController))]
    public sealed class MotionMatchingControllerInspector : UnityEditor.Editor
    {
        #region Unity API

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var controller = (MotionMatchingController)target;

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Debug Window")) MotionMatchingWindow.Open();
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    if (GUILayout.Button("Reset Weights to DB Default"))
                        controller.ResetWeightsToDatabaseDefault();
            }

            if (Application.isPlaying && controller.IsInitialized)
                DrawLiveReadout(controller.LastDebug);
        }

        private void OnSceneGUI()
        {
            var controller = (MotionMatchingController)target;
            if (!Application.isPlaying || !controller.IsInitialized) return;

            MotionMatchingDebugData debug = controller.LastDebug;
            if (debug == null || !debug.HasData) return;

            var space = new CharacterSpace(controller.transform.position, controller.transform.forward);
            DrawTrajectoryHandles(space, debug.DesiredTrajectory, MotionMatchingStyles.TrajectoryDesired);
            DrawTrajectoryHandles(space, debug.CandidateTrajectory, MotionMatchingStyles.TrajectoryCandidate);

            Handles.color = Color.white;
            Handles.Label(controller.transform.position + Vector3.up * 2.1f,
                $"{debug.SelectedClipName}\nframe {debug.SelectedFrame} · cost {debug.TotalCost:F2}");
        }

        #endregion

        #region Tools and Utilities

        private static void DrawLiveReadout(MotionMatchingDebugData debug)
        {
            if (debug == null || !debug.HasData) return;
            using (MotionMatchingStyles.BeginSection("Live"))
            {
                MotionMatchingStyles.KeyValue("Clip", debug.SelectedClipName ?? "—");
                MotionMatchingStyles.KeyValue("Frame", debug.SelectedFrame.ToString());
                MotionMatchingStyles.KeyValue("Total cost", debug.TotalCost.ToString("F3"));
                MotionMatchingStyles.KeyValue("Trajectory / Pose", $"{debug.TrajectoryCost:F3} / {debug.PoseCost:F3}");
            }
        }

        private static void DrawTrajectoryHandles(CharacterSpace space, TrajectorySample[] samples, Color color)
        {
            if (samples == null || samples.Length == 0) return;

            Handles.color = color;
            var points = new Vector3[samples.Length + 1];
            points[0] = space.Origin;
            for (int i = 0; i < samples.Length; i++)
                points[i + 1] = space.ToWorldPoint(samples[i].Position);

            Handles.DrawAAPolyLine(4f, points);
            for (int i = 0; i < samples.Length; i++)
            {
                Vector3 p = points[i + 1];
                Vector3 dir = space.ToWorldDirection(samples[i].Direction);
                Handles.DrawSolidDisc(p, Vector3.up, 0.05f);
                Handles.DrawAAPolyLine(2f, p, p + dir * 0.3f);
            }
        }

        #endregion
    }
}
