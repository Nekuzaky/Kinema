using System;
using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// The Analysis tab: measure the system instead of eyeballing it.
    ///
    /// Coverage answers "is my data any good" - which clips the matcher actually reaches for, and
    /// which are dead weight. Quality answers "does it read as real" - foot sliding, flicker, cost.
    /// Recording closes the loop: capture a session, replay the identical intent after a tuning
    /// change, and compare the numbers instead of two different human performances.
    /// </summary>
    public sealed class AnalysisTabDrawer
    {
        #region Private and Protected

        private Vector2 _coverageScroll;
        private CoverageReport _report;
        private int _reportSearches = -1;
        private string _saveMessage;

        #endregion

        #region Main API

        public void Draw(MotionMatchingController controller)
        {
            if (controller == null)
            {
                MotionMatchingStyles.HelpRow("Select a MotionMatchingController to analyse. Coverage and quality accumulate while playing.", MessageType.Info);
                return;
            }
            if (!Application.isPlaying || !controller.IsInitialized)
            {
                MotionMatchingStyles.HelpRow("Enter Play mode and move around: coverage, quality metrics and recording become available here.", MessageType.Info);
                return;
            }

            DrawQuality(controller);
            DrawCoverage(controller);
            DrawRecording(controller);
        }

        #endregion

        #region Tools and Utilities — Quality

        private static void DrawQuality(MotionMatchingController controller)
        {
            var probe = controller.GetComponent<MotionQualityProbe>();

            using (MotionMatchingStyles.BeginSection("Quality"))
            {
                if (probe == null)
                {
                    MotionMatchingStyles.HelpRow("Add a Motion Quality Probe to this character to measure foot sliding, flicker and cost.", MessageType.Info);
                    if (GUILayout.Button("Add Motion Quality Probe"))
                        Undo.AddComponent<MotionQualityProbe>(controller.gameObject);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    // Foot slide is the headline: a planted foot that travels is the thing players read as fake.
                    Color slideColor = probe.FootSlideRate < 0.05f ? MotionMatchingStyles.Ok
                        : probe.FootSlideRate < 0.15f ? MotionMatchingStyles.Warning : MotionMatchingStyles.Error;
                    MotionMatchingStyles.StatCard(probe.FootSlideRate.ToString("F3"), "Foot slide m/s", slideColor);
                    MotionMatchingStyles.StatCard(probe.JumpsPerSecond.ToString("F1"), "Jumps / s", MotionMatchingStyles.Accent);
                    MotionMatchingStyles.StatCard(probe.AverageCost.ToString("F2"), "Avg cost", MotionMatchingStyles.Accent);
                    MotionMatchingStyles.StatCard(probe.PeakCost.ToString("F2"), "Peak cost", MotionMatchingStyles.Accent);
                }

                MotionMatchingStyles.KeyValue("Slide budget", $"{probe.SlideMetres:F2} m over {probe.GroundedSeconds:F1}s grounded");
                MotionMatchingStyles.KeyValue("Peak slide", probe.PeakFootSlideRate.ToString("F3") + " m/s");

                MotionMatchingStyles.HelpRow(
                    probe.FootSlideRate < 0.05f
                        ? "Planted feet hold: under 0.05 m/s reads as real contact."
                        : probe.FootSlideRate < 0.15f
                            ? "Some sliding. Raise the bone weights on the feet, or enable Foot Lock IK."
                            : "Heavy sliding. The database likely lacks motion at this speed, or foot contacts are mis-detected.",
                    probe.FootSlideRate < 0.05f ? MessageType.Info : MessageType.Warning);

                if (GUILayout.Button("Reset Metrics")) probe.ResetMetrics();
            }
        }

        #endregion

        #region Tools and Utilities — Coverage

        private void DrawCoverage(MotionMatchingController controller)
        {
            // Rebuild only when new searches happened: the report walks the whole database.
            if (_report == null || controller.TotalSearches != _reportSearches)
            {
                _report = CoverageReport.Build(controller.Database, controller.FrameUsage);
                _reportSearches = controller.TotalSearches;
            }

            using (MotionMatchingStyles.BeginSection($"Coverage — {_report.TotalSelections:N0} selections"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Color coverageColor = _report.CoverageFraction > 0.5f ? MotionMatchingStyles.Ok
                        : _report.CoverageFraction > 0.2f ? MotionMatchingStyles.Warning : MotionMatchingStyles.Error;
                    MotionMatchingStyles.StatCard((_report.CoverageFraction * 100f).ToString("F0") + "%", "Database used", coverageColor);
                    MotionMatchingStyles.StatCard(_report.DeadFrames.ToString("N0"), "Dead frames",
                        _report.DeadFrames > 0 ? MotionMatchingStyles.Warning : MotionMatchingStyles.Ok);
                    MotionMatchingStyles.StatCard(_report.DeadClipCount.ToString(), "Dead clips",
                        _report.DeadClipCount > 0 ? MotionMatchingStyles.Error : MotionMatchingStyles.Ok);
                    MotionMatchingStyles.StatCard(controller.TotalJumps.ToString("N0"), "Jumps", MotionMatchingStyles.Accent);
                }

                _coverageScroll = EditorGUILayout.BeginScrollView(_coverageScroll, GUILayout.MaxHeight(190));
                int maxSelections = 1;
                foreach (ClipCoverage c in _report.Clips) maxSelections = Mathf.Max(maxSelections, c.Selections);

                foreach (ClipCoverage clip in _report.Clips)
                {
                    Color color = clip.IsDead ? MotionMatchingStyles.Error
                        : clip.UsedFraction < 0.25f ? MotionMatchingStyles.Warning : MotionMatchingStyles.Ok;
                    MotionMatchingStyles.ProportionBar(
                        $"{clip.ClipIndex:00}  {clip.Name}",
                        clip.Selections / (float)maxSelections,
                        clip.IsDead ? "never used" : $"{clip.Selections:N0} · {clip.UsedFraction * 100f:F0}% frames",
                        color);
                }
                EditorGUILayout.EndScrollView();

                if (_report.DeadClipCount > 0)
                    MotionMatchingStyles.HelpRow(
                        $"{_report.DeadClipCount} clip(s) were never selected. Either the gameplay never asks for that motion, or its features are too far from anything the player does - both mean wasted memory and mocap budget.",
                        MessageType.Warning);
                else if (_report.CoverageFraction < 0.2f && _report.TotalSelections > 200)
                    MotionMatchingStyles.HelpRow(
                        "The matcher keeps landing on a small slice of the database. Usually the trajectory weights dominate, or the data lacks variety at the speeds being played.",
                        MessageType.Warning);

                if (GUILayout.Button("Reset Coverage")) controller.ResetTelemetry();
            }
        }

        #endregion

        #region Tools and Utilities — Recording

        private void DrawRecording(MotionMatchingController controller)
        {
            var recorder = controller.GetComponent<SessionRecorder>();

            using (MotionMatchingStyles.BeginSection("Session Recording"))
            {
                if (recorder == null)
                {
                    MotionMatchingStyles.HelpRow("Add a Session Recorder to capture this session's locomotion intent. Replaying it later feeds identical input back through the controller, so tuning changes can be compared on the same performance.", MessageType.Info);
                    if (GUILayout.Button("Add Session Recorder"))
                        Undo.AddComponent<SessionRecorder>(controller.gameObject);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    MotionMatchingStyles.StatCard(recorder.RecordedFrameCount.ToString("N0"), "Frames", MotionMatchingStyles.Accent);
                    MotionMatchingStyles.StatCard(recorder.RecordedDuration.ToString("F1") + "s", "Duration", MotionMatchingStyles.Accent);
                    if (recorder.IsRecording) MotionMatchingStyles.StatCard("REC", "Capturing", MotionMatchingStyles.Error);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!recorder.IsRecording)
                    {
                        if (GUILayout.Button("Start Recording")) recorder.StartRecording();
                    }
                    else if (GUILayout.Button("Stop Recording")) recorder.StopRecording();

                    using (new EditorGUI.DisabledScope(recorder.IsRecording || recorder.RecordedFrameCount == 0))
                        if (GUILayout.Button("Save Recording Asset")) SaveRecording(recorder);
                }

                if (!string.IsNullOrEmpty(_saveMessage))
                    MotionMatchingStyles.HelpRow(_saveMessage, MessageType.Info);

                var replay = controller.GetComponent<ReplayLocomotionProvider>();
                if (replay != null && replay.IsReplaying)
                    MotionMatchingStyles.KeyValue("Replaying", $"{replay.Progress01 * 100f:F0}% (frame {replay.FrameIndex})");
            }
        }

        private void SaveRecording(SessionRecorder recorder)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Session Recording", "SessionRecording", "asset",
                "Save the captured locomotion intent for replay and A/B comparison.");
            if (string.IsNullOrEmpty(path)) return;

            var recording = AssetDatabase.LoadAssetAtPath<SessionRecording>(path);
            if (recording == null)
            {
                recording = ScriptableObject.CreateInstance<SessionRecording>();
                AssetDatabase.CreateAsset(recording, path);
            }

            recorder.WriteTo(recording, DateTime.UtcNow.ToString("u"));
            EditorUtility.SetDirty(recording);
            AssetDatabase.SaveAssets();

            _saveMessage = $"Saved {recording.FrameCount:N0} frames ({recording.Duration:F1}s, {recording.DistanceTravelled():F1} m travelled) to {path}. " +
                           "Add a Replay Locomotion Provider and assign it to replay this exact input.";
            EditorGUIUtility.PingObject(recording);
        }

        #endregion
    }
}
