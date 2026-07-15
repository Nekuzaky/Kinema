using UnityEngine;

namespace Kinema.MotionMatching.SmokeTest
{
    /// <summary>
    /// Standalone-player smoke test (TODO.md: "Never built as a standalone player"). Placed in a
    /// scene by the build script, it assembles the same synthetic setup the PlayMode tests use -
    /// procedural clip, database via <see cref="MotionMatchingDatabase.SetBakedData"/>, a
    /// <see cref="MotionMatchingController"/> - ticks it for a fixed number of frames in the built
    /// player (real Burst-compiled searches, real PlayableGraph, outside the Editor), then prints
    /// one machine-greppable verdict line and quits. Run the player with
    /// <c>-batchmode -nographics -logFile</c> and grep the log for <c>[KinemaSmoke]</c>.
    /// </summary>
    public sealed class StandaloneSmokeTest : MonoBehaviour
    {
        private const int FrameCount = 30;
        private const int Fps = 10;
        private const int FramesToRun = 60;

        private MotionMatchingController _controller;
        private MotionMatchingDatabase _db;
        private int _framesTicked;
        private bool _failed;

        private void Start()
        {
            var clip = new AnimationClip { name = "SyntheticWalk", wrapMode = WrapMode.Loop };
            clip.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 3f, 1f));

            var schema = new FeatureSchema
            {
                TrajectoryTimes = new[] { 0.2f },
                BoneNames = new[] { "Foot" },
                BoneWeights = new[] { 1f }
            };
            int dim = schema.Dimension;
            var mean = new float[dim];
            var std = new float[dim];
            for (int i = 0; i < dim; i++) std[i] = 1f;

            var features = new float[FrameCount * dim];
            var frames = new MotionFrameInfo[FrameCount];
            for (int f = 0; f < FrameCount; f++)
            {
                frames[f] = new MotionFrameInfo(0, f / (float)Fps);
                features[f * dim + schema.TrajectoryPositionOffset] = f * 0.1f;
            }
            var clips = new[]
            {
                new MotionClipEntry
                {
                    Clip = clip, Name = clip.name, StartFrame = 0,
                    FrameCount = FrameCount, Length = clip.length, IsLooping = true
                }
            };

            _db = ScriptableObject.CreateInstance<MotionMatchingDatabase>();
            _db.SetBakedData(schema, features, mean, std, frames, clips,
                FeatureWeights.Default, bakeFrameRate: Fps, bakeDateUtc: "smoke",
                totalDuration: FrameCount / (float)Fps);

            var character = new GameObject("SmokeCharacter");
            character.AddComponent<Animator>();
            var foot = new GameObject("Foot");
            foot.transform.SetParent(character.transform, false);

            _controller = character.AddComponent<MotionMatchingController>();
            if (!_controller.SwitchDatabase(_db) || !_controller.IsInitialized)
            {
                Fail("controller failed to initialize");
                return;
            }
            _controller.DesiredVelocity = new Vector3(0f, 0f, 1.2f);
        }

        private void Update()
        {
            if (_failed || _controller == null) return;

            int frame = _controller.CurrentFrame;
            if (!_controller.IsInitialized || _controller.CurrentClipIndex != 0 || frame < 0 || frame >= FrameCount)
            {
                Fail($"state broke at tick {_framesTicked}: initialized={_controller.IsInitialized} clip={_controller.CurrentClipIndex} frame={frame}");
                return;
            }

            if (++_framesTicked >= FramesToRun)
            {
                BenchmarkSearch();
                Debug.Log($"[KinemaSmoke] PASS - {_framesTicked} frames ticked, final frame {frame}, clip {_controller.CurrentClipIndex}");
                Quit(0);
            }
        }

        /// <summary>
        /// In-player search benchmark (TODO.md: standalone-build numbers were never measured -
        /// everything so far ran in-editor with Burst forced synchronous). Same shape as the editor's
        /// Benchmark Search: plausible queries (real rows plus noise), warmup absorbing the Burst
        /// compile, then mean/median/p99 over 2000 samples on a 44-dim clustered synthetic set,
        /// logged as one greppable line.
        /// </summary>
        private void BenchmarkSearch()
        {
            const int Frames = 5000;
            const int Warmup = 200;
            const int Samples = 2000;

            MotionMatchingDatabase db = CreateSyntheticBenchmarkDatabase(Frames);
            try
            {
                using var matcher = new MotionMatcher(db, FeatureWeights.Default);
                var query = new MotionMatchingQuery(db.Schema);
                var random = new System.Random(12345);

                void NextQuery()
                {
                    int row = random.Next(db.FrameCount) * db.Dimension;
                    float[] features = db.Features;
                    for (int i = 0; i < db.Dimension; i++)
                        query.Values[i] = features[row + i] + (float)(random.NextDouble() - 0.5) * 0.2f;
                }

                for (int i = 0; i < Warmup; i++) { NextQuery(); matcher.Search(query); }

                var timings = new double[Samples];
                var watch = new System.Diagnostics.Stopwatch();
                for (int i = 0; i < Samples; i++)
                {
                    NextQuery();
                    watch.Restart();
                    matcher.Search(query);
                    watch.Stop();
                    timings[i] = watch.Elapsed.TotalMilliseconds;
                }

                System.Array.Sort(timings);
                double mean = 0d;
                for (int i = 0; i < Samples; i++) mean += timings[i];
                mean /= Samples;

                Debug.Log($"[KinemaSmoke] BENCH standalone, synthetic {Frames:N0} frames x 44 dims: " +
                          $"mean {mean * 1000d:F1} us | median {timings[Samples / 2] * 1000d:F1} us | " +
                          $"p99 {timings[(int)(Samples * 0.99)] * 1000d:F1} us");
            }
            finally
            {
                Destroy(db);
            }
        }

        /// <summary>Clustered synthetic set, same construction as the editor benchmark's - clusters
        /// matter because the search's early-out exploits local similarity.</summary>
        private static MotionMatchingDatabase CreateSyntheticBenchmarkDatabase(int frameCount)
        {
            var schema = new FeatureSchema
            {
                TrajectoryTimes = new[] { -0.2f, 0.2f, 0.4f, 0.6f },
                BoneNames = new[] { "Bone0", "Bone1", "Bone2" },
                BoneWeights = new[] { 1f, 1f, 1f }
            };
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
                bakeFrameRate: 30, bakeDateUtc: "smoke-bench", totalDuration: frameCount / 30f);
            return db;
        }

        private void Fail(string reason)
        {
            _failed = true;
            Debug.Log($"[KinemaSmoke] FAIL - {reason}");
            Quit(1);
        }

        private static void Quit(int code)
        {
            Application.Quit(code);
        }
    }
}
