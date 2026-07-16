using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// PlayMode coverage for motion events: plays a real event through
    /// <see cref="MotionMatchingController.PlayEvent"/> and asserts the root warp actually delivers
    /// the character to the requested position/yaw by contact time, that the event ends on its own,
    /// and that matching resumes. Manual ticking at a fixed timestep - the warp math is time-based,
    /// and owning the clock keeps the assertions exact without coroutines.
    /// </summary>
    public sealed class MotionEventPlayModeTests
    {
        private PlayModeTestRig _rig;
        private AnimationClip _eventClip;
        private MotionEventDefinition _eventDefinition;

        [SetUp]
        public void SetUp()
        {
            _rig = PlayModeTestRig.Create("EventTestCharacter");

            // Animates the child, never the root: the warp owns the root (see PlayModeTestRig).
            _eventClip = new AnimationClip { name = "SyntheticVault" };
            _eventClip.SetCurve("Foot", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 0.5f));

            _eventDefinition = ScriptableObject.CreateInstance<MotionEventDefinition>();
            SetPrivateField(_eventDefinition, "_clip", _eventClip);
            SetPrivateField(_eventDefinition, "_contactTime", 0.5f);
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
            if (_eventClip != null) Object.DestroyImmediate(_eventClip);
            if (_eventDefinition != null) Object.DestroyImmediate(_eventDefinition);
        }

        /// <summary>MotionEventDefinition is an authoring asset with serialized-only fields; tests
        /// author one through the same fields the inspector writes.</summary>
        private static void SetPrivateField(Object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, $"expected serialized field '{fieldName}' on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        [Test]
        public void PlayEvent_WarpsRootToTargetByContactTime()
        {
            var target = new Vector3(2f, 0f, 3f);
            var targetRotation = Quaternion.Euler(0f, 90f, 0f);

            Assert.IsTrue(_rig.Controller.PlayEvent(_eventDefinition, target, targetRotation));
            Assert.IsTrue(_rig.Controller.IsPlayingEvent);

            _rig.Step(18); // contact at 0.5 s; 18 x 1/30 = 0.6 s, just past it.

            Vector3 horizontalError = _rig.GameObject.transform.position - target;
            horizontalError.y = 0f;
            // The per-frame warp closes the remaining error linearly; with dt = 1/30 over a 0.5 s
            // approach the residual is ~initialError * dt / contactTime, about 0.24 m here.
            Assert.Less(horizontalError.magnitude, 0.3f,
                $"root should land near the event target by contact time (was {horizontalError.magnitude:F3} m off)");

            float yawError = Quaternion.Angle(_rig.GameObject.transform.rotation, targetRotation);
            Assert.Less(yawError, 15f, $"root yaw should converge on the target facing (was {yawError:F1} deg off)");
        }

        [Test]
        public void PlayEvent_EndsAtClipEndAndResumesMatching()
        {
            _rig.Controller.PlayEvent(_eventDefinition, _rig.GameObject.transform.position, _rig.GameObject.transform.rotation);
            Assert.IsTrue(_rig.Controller.IsPlayingEvent);

            _rig.Step(36); // event clip is 1 s; 36 x 1/30 = 1.2 s.

            Assert.IsFalse(_rig.Controller.IsPlayingEvent, "event should end on its own at clip end");

            // Matching resumed: the mapped frame is a valid database frame again after more steps.
            _rig.Controller.DesiredVelocity = Vector3.forward;
            _rig.Step(10);
            int frame = _rig.Controller.CurrentFrame;
            Assert.GreaterOrEqual(frame, 0);
            Assert.Less(frame, PlayModeTestRig.FrameCount);
        }

        [Test]
        public void RootMotion_StaysBoundedUnderConstantIntent()
        {
            // Regression guard on the warp/stride machinery as a whole: with a modest constant
            // intent, the character must not teleport or diverge. The synthetic clip covers 1 m in
            // 3 s, so even with stride warping the plausible travel over 2 s is well under 5 m.
            _rig.Controller.DesiredVelocity = new Vector3(0f, 0f, 1f);
            Vector3 start = _rig.GameObject.transform.position;

            _rig.Step(60); // 2 s

            float travelled = Vector3.Distance(start, _rig.GameObject.transform.position);
            Assert.Less(travelled, 5f, $"unexpected divergence: travelled {travelled:F2} m in 2 s");
            Assert.IsFalse(float.IsNaN(_rig.GameObject.transform.position.x), "position must never go NaN");
        }
    }
}
