using System;
using System.Diagnostics;
using Unity.Burst;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Measures what a search actually costs, so "is it fast enough" stops being an opinion.
    ///
    /// The number that matters is not the average: a frame that overruns its budget is the one the
    /// player sees, so this reports the 99th percentile alongside the mean, and converts both into
    /// the question a game actually asks - how many characters can search every tick inside a 16.7 ms
    /// frame, at the configured search rate.
    ///
    /// Queries are drawn from the database itself with noise added, rather than being random noise:
    /// the branch-and-bound early-out means a plausible query and a nonsense one take very different
    /// paths, and only the plausible one is worth timing. Synthetic databases scale beyond whatever
    /// is baked locally, so the growth curve can be read to the sizes a shipping game would hold.
    /// </summary>
    public static class SearchBenchmark
    {
        #region Main API

        [MenuItem("Tools/Kinema/Benchmark Search", priority = 62)]
        public static void RunMenu() => Run();

        /// <summary>Headless entry point (Unity -executeMethod).</summary>
        public static void RunFromCommandLine() => Run();

        public static void Run()
        {
            var report = new StringBuilder();
            report.AppendLine("[Kinema] Search benchmark");

            // Block on Burst compilation rather than racing it; restored before returning.
            bool previousSync = BurstCompiler.Options.EnableBurstCompileSynchronously;
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            try
            {
                Measure(report);
            }
            finally
            {
                BurstCompiler.Options.EnableBurstCompileSynchronously = previousSync;
                Debug.Log(report.ToString());
            }
        }

        private static void Measure(StringBuilder report)
        {
            // Burst compiles the search job; with Burst off in the editor these numbers are
            // several times worse than a build and mean nothing. Say which one this is.
            report.AppendLine($"[Kinema]   worker threads: {Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount + 1}, " +
                              $"Burst: {(EditorPrefs.GetBool("BurstCompiler.IsEnabled", true) ? "enabled" : "DISABLED - numbers are meaningless")}");

            WarmUpBurst(report);

            MotionMatchingDatabase real = FindRichestDatabase();
            if (real != null)
                Measure(report, real, $"baked: {real.name}", SearchAcceleration.BurstLinear);

            // Synthetic scaling: same dimensionality as the real set, frame counts a shipping game
            // would reach. 44 dims is what the demo schema produces.
            foreach (int frames in new[] { 5_000, 25_000, 100_000, 400_000 })
            {
                MotionMatchingDatabase synthetic = CreateSynthetic(frames, dimension: 44);
                Measure(report, synthetic, $"synthetic {frames:N0} frames", SearchAcceleration.BurstLinear);
                if (frames >= 100_000)
                    Measure(report, synthetic, $"synthetic {frames:N0} frames", SearchAcceleration.KdTree);
                UnityEngine.Object.DestroyImmediate(synthetic);
            }

            // Same database, measured last: if this disagrees with the first line, the numbers are
            // still order-dependent and none of them can be trusted.
            if (real != null)
                Measure(report, real, $"baked (re-measured last)", SearchAcceleration.BurstLinear);
        }

        #endregion

        #region Tools and Utilities

        /// <summary>
        /// Burst compiles asynchronously in the editor: until the background compile lands, the job
        /// runs as managed IL an order of magnitude slower. Whichever database was measured first
        /// absorbed that penalty and reported a fictional number - reordering the runs moved the slow
        /// result with the order, which is how this was caught, and a batch-count warmup could not
        /// fix it because no number of batches makes an async compiler finish. Forcing synchronous
        /// compilation does: the first search blocks until the job is compiled, and every timing
        /// after it describes compiled code.
        /// </summary>
        private static void WarmUpBurst(StringBuilder report)
        {
            MotionMatchingDatabase db = CreateSynthetic(4000, 44);
            try
            {
                using var matcher = new MotionMatcher(db, FeatureWeights.Default);
                var query = new MotionMatchingQuery(db.Schema);
                float[] features = db.Features;
                for (int i = 0; i < db.Dimension; i++) query.Values[i] = features[i] + 0.05f;

                // The first search blocks on the compile; timing it would report the compiler, not
                // the search, so it is paid before the stopwatch starts.
                var watch = new Stopwatch();
                watch.Restart();
                matcher.Search(query);
                double compileMs = watch.Elapsed.TotalMilliseconds;

                watch.Restart();
                for (int i = 0; i < 400; i++) matcher.Search(query);
                watch.Stop();
                report.AppendLine($"[Kinema]   warmup: Burst compiled in {compileMs:F0} ms, then " +
                                  $"{watch.Elapsed.TotalMilliseconds / 400d * 1000d:F1} us/search");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(db);
            }
        }

        private static void Measure(StringBuilder report, MotionMatchingDatabase db, string label, SearchAcceleration acceleration)
        {
            const int Warmup = 200;
            const int Samples = 2000;

            using var matcher = new MotionMatcher(db, FeatureWeights.Default) { Acceleration = acceleration };
            var query = new MotionMatchingQuery(db.Schema);
            var random = new System.Random(12345);

            // Plausible queries: real rows nudged off their exact values. Random noise would take a
            // different branch-and-bound path and time something the runtime never does.
            void NextQuery()
            {
                int row = random.Next(db.FrameCount) * db.Dimension;
                float[] features = db.Features;
                for (int i = 0; i < db.Dimension; i++)
                    query.Values[i] = features[row + i] + (float)(random.NextDouble() - 0.5) * 0.2f;
            }

            for (int i = 0; i < Warmup; i++) { NextQuery(); matcher.Search(query); }

            var timings = new double[Samples];
            var watch = new Stopwatch();
            for (int i = 0; i < Samples; i++)
            {
                NextQuery();
                watch.Restart();
                matcher.Search(query);
                watch.Stop();
                timings[i] = watch.Elapsed.TotalMilliseconds;
            }

            Array.Sort(timings);
            double mean = 0d;
            for (int i = 0; i < Samples; i++) mean += timings[i];
            mean /= Samples;
            double median = timings[Samples / 2];
            double p99 = timings[(int)(Samples * 0.99)];

            // Characters that can each search every tick inside a 60 Hz frame, at 10 Hz searching.
            const float FrameBudgetMs = 16.67f;
            const float SearchesPerSecond = 10f;
            double perCharacterMsPerFrame = p99 * (SearchesPerSecond / 60f);
            int characters = perCharacterMsPerFrame > 0d ? (int)(FrameBudgetMs / perCharacterMsPerFrame) : 0;

            report.AppendLine(
                $"[Kinema]   {label,-34} {acceleration,-11} " +
                $"mean {mean * 1000d,7:F1} us | median {median * 1000d,7:F1} us | p99 {p99 * 1000d,8:F1} us " +
                $"| ~{characters:N0} characters @10Hz in a 60fps frame");
        }

        /// <summary>
        /// A synthetic database of clustered frames. Clusters matter: real motion data is locally
        /// similar, which is exactly what the early-out exploits, so uniform noise would flatter the
        /// linear scan.
        /// </summary>
        private static MotionMatchingDatabase CreateSynthetic(int frameCount, int dimension)
        {
            int boneCount = (dimension - 2) / 6 - 1;
            boneCount = Mathf.Max(1, boneCount);
            var schema = new FeatureSchema
            {
                TrajectoryTimes = new[] { -0.2f, 0.2f, 0.4f, 0.6f },
                BoneNames = new string[3],
                BoneWeights = new[] { 1f, 1f, 1f }
            };
            for (int b = 0; b < 3; b++) schema.BoneNames[b] = "Bone" + b;

            int dim = schema.Dimension;
            var random = new System.Random(4242);
            var features = new float[frameCount * dim];

            const int ClusterCount = 64;
            var centres = new float[ClusterCount * dim];
            for (int i = 0; i < centres.Length; i++) centres[i] = (float)(random.NextDouble() * 4d - 2d);

            for (int f = 0; f < frameCount; f++)
            {
                int cluster = (f * ClusterCount / frameCount) % ClusterCount;
                for (int i = 0; i < dim; i++)
                    features[f * dim + i] = centres[cluster * dim + i] + (float)(random.NextDouble() - 0.5) * 0.3f;
            }

            var mean = new float[dim];
            var std = new float[dim];
            for (int i = 0; i < dim; i++) std[i] = 1f;

            var frames = new MotionFrameInfo[frameCount];
            for (int f = 0; f < frameCount; f++) frames[f] = new MotionFrameInfo(0, f / 30f);
            var clips = new[]
            {
                new MotionClipEntry { Name = "Synthetic", StartFrame = 0, FrameCount = frameCount, Length = frameCount / 30f, IsLooping = true }
            };

            var db = ScriptableObject.CreateInstance<MotionMatchingDatabase>();
            db.SetBakedData(schema, features, mean, std, frames, clips, FeatureWeights.Default,
                bakeFrameRate: 30, bakeDateUtc: "benchmark", totalDuration: frameCount / 30f);
            return db;
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

        #endregion
    }
}
