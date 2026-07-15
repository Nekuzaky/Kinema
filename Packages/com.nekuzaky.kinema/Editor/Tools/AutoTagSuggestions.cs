using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Runs <see cref="GaitClassifier"/> over the project's richest baked database and logs the
    /// proposed idle/walk/run/turn ranges per clip. Suggestions only - nothing is written to any
    /// config: tagging stays an authoring decision made in the Tags tab, this just does the
    /// looking. Headless-runnable (<c>-executeMethod ... AutoTagSuggestions.RunFromCommandLine</c>),
    /// same pattern as the search benchmark.
    /// </summary>
    public static class AutoTagSuggestions
    {
        [MenuItem("Tools/Kinema/Log Auto-Tag Suggestions", priority = 63)]
        public static void RunMenu() => Run();

        /// <summary>Headless entry point (Unity -executeMethod).</summary>
        public static void RunFromCommandLine() => Run();

        public static void Run()
        {
            MotionMatchingDatabase database = FindRichestDatabase();
            if (database == null)
            {
                Debug.Log("[KinemaAutoTag] No baked database found in the project.");
                return;
            }

            var settings = GaitClassifier.Settings.Default;
            List<GaitClassifier.Range> ranges = GaitClassifier.Classify(database, settings);

            var report = new StringBuilder();
            report.AppendLine($"[KinemaAutoTag] {database.name}: {ranges.Count} suggested ranges " +
                              $"(idle <= {settings.IdleMaxSpeed} m/s, run > {settings.WalkMaxSpeed} m/s, " +
                              $"turn >= {settings.TurnMinDegreesPerSecond} deg/s)");
            foreach (GaitClassifier.Range range in ranges)
            {
                string clipName = database.GetClip(range.ClipIndex).Name;
                string label = range.Gait.ToString() + (range.Turning ? "+Turn" : "");
                report.AppendLine($"[KinemaAutoTag]   {clipName,-40} {range.StartTime,6:F2}-{range.EndTime,-6:F2}s  {label}");
            }
            Debug.Log(report.ToString());
        }

        private static MotionMatchingDatabase FindRichestDatabase()
        {
            MotionMatchingDatabase best = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(MotionMatchingDatabase)))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate == null || !candidate.IsValid) continue;
                if (best == null || candidate.FrameCount > best.FrameCount) best = candidate;
            }
            return best;
        }
    }
}
