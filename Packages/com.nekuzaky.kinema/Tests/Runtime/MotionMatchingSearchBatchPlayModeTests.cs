using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// PlayMode coverage for <see cref="MotionMatchingSearchBatch"/>: several real controllers route
    /// their periodic searches through the batch (schedule in Update, complete in LateUpdate) and
    /// must behave exactly as soundly as the synchronous path - valid frame mapping under ticking
    /// intent, and a clean fallback to synchronous searching when the batch is disabled mid-run.
    /// What these tests cannot judge is the performance win itself - that's the benchmark's job
    /// (measured ~1.7x); this verifies the wiring is correct, not that it is fast.
    /// </summary>
    public sealed class MotionMatchingSearchBatchPlayModeTests
    {
        private const int Fps = 10;
        private const int FrameCount = 30;
        private const int CharacterCount = 3;

        private GameObject _batchGo;
        private MotionMatchingSearchBatch _batch;
        private GameObject[] _characters;
        private MotionMatchingController[] _controllers;
        private MotionMatchingDatabase _db;
        private AnimationClip _clip;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.captureDeltaTime = 1f / 30f;

            _clip = new AnimationClip { name = "SyntheticWalk", wrapMode = WrapMode.Loop };
            _clip.SetCurve("Foot", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 3f, 1f));
            _db = CreateDatabase(_clip);

            _characters = new GameObject[CharacterCount];
            _controllers = new MotionMatchingController[CharacterCount];
            for (int i = 0; i < CharacterCount; i++)
            {
                _characters[i] = new GameObject($"BatchCharacter{i}");
                _characters[i].AddComponent<Animator>();
                var foot = new GameObject("Foot");
                foot.transform.SetParent(_characters[i].transform, false);
                _controllers[i] = _characters[i].AddComponent<MotionMatchingController>();
            }
            yield return null;

            foreach (MotionMatchingController controller in _controllers)
                controller.SwitchDatabase(_db);

            // Batch created after the controllers so its empty-list auto-collect finds them all.
            _batchGo = new GameObject("SearchBatch");
            _batch = _batchGo.AddComponent<MotionMatchingSearchBatch>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (_batchGo != null) Object.Destroy(_batchGo);
            if (_characters != null)
                foreach (GameObject go in _characters)
                    if (go != null) Object.Destroy(go);
            if (_db != null) Object.Destroy(_db);
            if (_clip != null) Object.Destroy(_clip);
            yield return null;
        }

        private static MotionMatchingDatabase CreateDatabase(AnimationClip clip)
        {
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

            var db = ScriptableObject.CreateInstance<MotionMatchingDatabase>();
            db.SetBakedData(schema, features, mean, std, frames, clips,
                FeatureWeights.Default, bakeFrameRate: Fps, bakeDateUtc: "batch-test",
                totalDuration: FrameCount / (float)Fps);
            return db;
        }

        [UnityTest]
        public IEnumerator Batch_RoutesAllControllersAndKeepsThemSound()
        {
            foreach (MotionMatchingController controller in _controllers)
                Assert.IsTrue(ReferenceEquals(controller.SearchScheduler, _batch),
                    "auto-collect should have routed every controller through the batch");

            for (int i = 0; i < CharacterCount; i++)
                _controllers[i].DesiredVelocity = new Vector3(0f, 0f, 0.5f + i * 0.5f);

            // 1.5 s at 30 fps fixed step - several 10 Hz search cycles per controller, all batched.
            for (int i = 0; i < 45; i++)
            {
                yield return null;
                foreach (MotionMatchingController controller in _controllers)
                {
                    int frame = controller.CurrentFrame;
                    Assert.GreaterOrEqual(frame, 0, "batched searching broke frame mapping");
                    Assert.Less(frame, FrameCount, "batched searching broke frame mapping");
                }
            }
        }

        [UnityTest]
        public IEnumerator DisablingBatch_RestoresSynchronousSearching()
        {
            foreach (MotionMatchingController controller in _controllers)
                controller.DesiredVelocity = Vector3.forward;
            for (int i = 0; i < 15; i++) yield return null;

            _batch.enabled = false;
            yield return null;

            foreach (MotionMatchingController controller in _controllers)
                Assert.IsNull(controller.SearchScheduler, "disabling the batch must hand back the synchronous path");

            // Controllers keep working on their own afterwards.
            for (int i = 0; i < 15; i++) yield return null;
            foreach (MotionMatchingController controller in _controllers)
            {
                int frame = controller.CurrentFrame;
                Assert.GreaterOrEqual(frame, 0);
                Assert.Less(frame, FrameCount);
            }
        }
    }
}
