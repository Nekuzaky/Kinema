using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// PlayMode coverage for motion events (TODO.md follow-up: "event root-warping lands on its
    /// target"): plays a real event through <see cref="MotionMatchingController.PlayEvent"/> and
    /// asserts the warp actually delivers the root to the requested position/yaw by contact time,
    /// that the event ends on its own, and that matching resumes. Uses
    /// <see cref="Time.captureDeltaTime"/> for a fixed, deterministic timestep - batchmode frame
    /// times are otherwise sub-millisecond and the warp math is time-based.
    /// </summary>
    public sealed class MotionEventPlayModeTests
    {
        private const int Fps = 10;
        private const int FrameCount = 30;
        private const float FixedDt = 1f / 30f;

        private GameObject _go;
        private MotionMatchingController _controller;
        private MotionMatchingDatabase _db;
        private AnimationClip _locomotionClip;
        private AnimationClip _eventClip;
        private MotionEventDefinition _eventDefinition;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.captureDeltaTime = FixedDt;

            // Neither clip may animate the ROOT transform: any root curve on any clip connected to
            // the two-slot mixer keeps that property graph-owned even at weight 0, and every
            // Evaluate then stomps the event warp's transform writes back to the curve value.
            // Real locomotion clips carry root motion through the Animator, not root curves.
            _locomotionClip = new AnimationClip { name = "SyntheticWalk", wrapMode = WrapMode.Loop };
            _locomotionClip.SetCurve("Foot", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 3f, 1f));
            _db = CreateDatabase(_locomotionClip);

            // The event clip must NOT animate the root transform: the graph re-evaluates the clip
            // every frame and would overwrite the warp's transform writes (found the hard way -
            // a root-position curve here left the character exactly at its start). Real event clips
            // animate bones; this one animates the child, and the warp owns the root.
            _eventClip = new AnimationClip { name = "SyntheticVault" };
            _eventClip.SetCurve("Foot", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 0.5f));

            _eventDefinition = ScriptableObject.CreateInstance<MotionEventDefinition>();
            SetPrivateField(_eventDefinition, "_clip", _eventClip);
            SetPrivateField(_eventDefinition, "_contactTime", 0.5f);

            _go = new GameObject("EventTestCharacter");
            _go.AddComponent<Animator>();
            var foot = new GameObject("Foot");
            foot.transform.SetParent(_go.transform, false);
            _controller = _go.AddComponent<MotionMatchingController>();
            yield return null;

            _controller.SwitchDatabase(_db);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (_go != null) Object.Destroy(_go);
            if (_db != null) Object.Destroy(_db);
            if (_locomotionClip != null) Object.Destroy(_locomotionClip);
            if (_eventClip != null) Object.Destroy(_eventClip);
            if (_eventDefinition != null) Object.Destroy(_eventDefinition);
            yield return null;
        }

        /// <summary>MotionEventDefinition is an authoring asset with serialized-only fields; tests
        /// author one through the same serialization path the inspector uses.</summary>
        private static void SetPrivateField(Object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, $"expected serialized field '{fieldName}' on {target.GetType().Name}");
            field.SetValue(target, value);
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
                FeatureWeights.Default, bakeFrameRate: Fps, bakeDateUtc: "event-test",
                totalDuration: FrameCount / (float)Fps);
            return db;
        }

        [UnityTest]
        public IEnumerator PlayEvent_WarpsRootToTargetByContactTime()
        {
            var target = new Vector3(2f, 0f, 3f);
            var targetRotation = Quaternion.Euler(0f, 90f, 0f);

            Assert.IsTrue(_controller.PlayEvent(_eventDefinition, target, targetRotation));
            Assert.IsTrue(_controller.IsPlayingEvent);

            // Contact is at 0.5 s; run to just past it (0.6 s at the fixed 1/30 step).
            for (int i = 0; i < 18; i++) yield return null;

            Vector3 horizontalError = _go.transform.position - target;
            horizontalError.y = 0f;
            // The per-frame warp closes the remaining error linearly; with dt = 1/30 over a 0.5 s
            // approach the residual is ~initialError * dt / contactTime ~ 0.24 m here.
            Assert.Less(horizontalError.magnitude, 0.3f,
                $"root should land near the event target by contact time (was {horizontalError.magnitude:F3} m off)");

            float yawError = Quaternion.Angle(_go.transform.rotation, targetRotation);
            Assert.Less(yawError, 15f, $"root yaw should converge on the target facing (was {yawError:F1} deg off)");
        }

        [UnityTest]
        public IEnumerator PlayEvent_EndsAtClipEndAndResumesMatching()
        {
            _controller.PlayEvent(_eventDefinition, _go.transform.position, _go.transform.rotation);
            Assert.IsTrue(_controller.IsPlayingEvent);

            // Event clip is 1 s; run 1.2 s.
            for (int i = 0; i < 36; i++) yield return null;

            Assert.IsFalse(_controller.IsPlayingEvent, "event should end on its own at clip end");

            // Matching resumed: the mapped frame is a valid database frame again after more ticks.
            _controller.DesiredVelocity = Vector3.forward;
            for (int i = 0; i < 10; i++) yield return null;
            int frame = _controller.CurrentFrame;
            Assert.GreaterOrEqual(frame, 0);
            Assert.Less(frame, FrameCount);
        }

        [UnityTest]
        public IEnumerator RootMotion_StaysBoundedUnderConstantIntent()
        {
            // Regression guard on the warp/stride machinery as a whole: with a modest constant
            // intent, the character must not teleport or diverge. The synthetic clip covers 1 m in
            // 3 s, so even with stride warping the plausible travel over 2 s is well under 5 m.
            _controller.DesiredVelocity = new Vector3(0f, 0f, 1f);
            Vector3 start = _go.transform.position;

            for (int i = 0; i < 60; i++) yield return null; // 2 s at the fixed step

            float travelled = Vector3.Distance(start, _go.transform.position);
            Assert.Less(travelled, 5f, $"unexpected divergence: travelled {travelled:F2} m in 2 s");
            Assert.IsFalse(float.IsNaN(_go.transform.position.x), "position must never go NaN");
        }
    }
}
