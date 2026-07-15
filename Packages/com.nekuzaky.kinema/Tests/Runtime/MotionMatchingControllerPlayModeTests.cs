using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// First PlayMode coverage (TODO.md: "No PlayMode test coverage"): drives a real
    /// <see cref="MotionMatchingController"/> - live PlayableGraph, real Update loop, real searches -
    /// through scripted intent for a number of frames and asserts the machine holds together:
    /// initialization succeeds, frame/clip mapping stays in range, database switching and the
    /// Mecanim-fade survive ticking, teardown doesn't leak or throw. Everything runs on a synthetic
    /// database wrapped around a procedurally-authored <see cref="AnimationClip"/>, so no rig, mocap
    /// pack or scene asset is needed. What this deliberately does not judge: whether the motion looks
    /// right - that stays a Play Mode eyeball job, per TODO.md.
    /// </summary>
    public sealed class MotionMatchingControllerPlayModeTests
    {
        private const int FrameCount = 30;
        private const int Fps = 10;

        private GameObject _go;
        private MotionMatchingController _controller;
        private MotionMatchingDatabase _db;
        private AnimationClip _clip;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _clip = CreateProceduralClip();
            _db = CreateDatabase(_clip);

            _go = new GameObject("PlayModeTestCharacter");
            _go.AddComponent<Animator>();
            // Schema names a "Foot" bone; give the rig one so the live pose query path runs instead
            // of its bone-missing fallback.
            var foot = new GameObject("Foot");
            foot.transform.SetParent(_go.transform, false);

            _controller = _go.AddComponent<MotionMatchingController>();
            yield return null; // let Awake/OnEnable run (OnEnable warns: no database yet - expected).
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            if (_db != null) Object.Destroy(_db);
            if (_clip != null) Object.Destroy(_clip);
            yield return null;
        }

        /// <summary>A 3-second clip drifting the root forward - real curves, so
        /// <c>AnimationClipPlayable</c> has something genuine to evaluate.</summary>
        private static AnimationClip CreateProceduralClip()
        {
            var clip = new AnimationClip { name = "SyntheticWalk", wrapMode = WrapMode.Loop };
            clip.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 3f, 1f));
            clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Constant(0f, 3f, 0f));
            return clip;
        }

        /// <summary>Same shape as the EditMode TestDatabaseFactory, but with a real AnimationClip in
        /// the entry - the piece playback (<c>AnimationClipPlayable</c>) needs and EditMode can't use.</summary>
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
                // Spread trajectory positions so different queries prefer different frames.
                features[f * dim + schema.TrajectoryPositionOffset] = f * 0.1f;
                features[f * dim + schema.TrajectoryPositionOffset + 1] = 0f;
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
                FeatureWeights.Default, bakeFrameRate: Fps, bakeDateUtc: "playmode-test",
                totalDuration: FrameCount / (float)Fps);
            return db;
        }

        [UnityTest]
        public IEnumerator SwitchDatabase_InitializesAndTicksWithoutErrors()
        {
            Assert.IsTrue(_controller.SwitchDatabase(_db), "SwitchDatabase should accept a valid database");
            Assert.IsTrue(_controller.IsInitialized);

            _controller.DesiredVelocity = new Vector3(0f, 0f, 1.2f);
            for (int i = 0; i < 30; i++) yield return null;

            // Still alive after ~30 real Update ticks (searches included at the default 10 Hz interval).
            Assert.IsTrue(_controller.IsInitialized);
            Assert.AreEqual(0, _controller.CurrentClipIndex, "single-clip database: the active clip can only be 0");
            int frame = _controller.CurrentFrame;
            Assert.GreaterOrEqual(frame, 0);
            Assert.Less(frame, FrameCount, "mapped frame must stay inside the database");
        }

        [UnityTest]
        public IEnumerator ChangingIntent_KeepsFrameMappingInRange()
        {
            _controller.SwitchDatabase(_db);

            // Sweep intent through a few directions, ticking between each - exercises repeated
            // searches and transitions, the loop the EditMode tests can't drive.
            foreach (Vector3 intent in new[]
                     { Vector3.forward, Vector3.right, Vector3.zero, new Vector3(-1f, 0f, -1f) })
            {
                _controller.DesiredVelocity = intent;
                for (int i = 0; i < 15; i++)
                {
                    yield return null;
                    int frame = _controller.CurrentFrame;
                    Assert.GreaterOrEqual(frame, 0, $"frame mapping broke while heading {intent}");
                    Assert.Less(frame, FrameCount, $"frame mapping broke while heading {intent}");
                }
            }
        }

        [UnityTest]
        public IEnumerator SetMatchingActive_FadeOutAndBackIn_SurvivesTicking()
        {
            _controller.SwitchDatabase(_db);
            _controller.DesiredVelocity = Vector3.forward;

            _controller.SetMatchingActive(false, 0.05f);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsFalse(_controller.IsMatchingActive);
            Assert.IsTrue(_controller.IsInitialized, "fading out must not tear the graph down");

            _controller.SetMatchingActive(true, 0.05f);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(_controller.IsMatchingActive);
            Assert.IsTrue(_controller.IsInitialized);
        }

        [UnityTest]
        public IEnumerator DisablingComponent_TearsDownCleanly()
        {
            _controller.SwitchDatabase(_db);
            for (int i = 0; i < 5; i++) yield return null;

            _controller.enabled = false;
            yield return null;
            Assert.IsFalse(_controller.IsInitialized);
            Assert.AreEqual(-1, _controller.CurrentClipIndex);

            // Re-enable: OnEnable re-initializes from the still-assigned database.
            _controller.enabled = true;
            yield return null;
            Assert.IsTrue(_controller.IsInitialized, "re-enabling should rebuild the graph from the assigned database");
        }
    }
}
