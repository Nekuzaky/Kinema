using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// PlayMode coverage for the controller's live machinery (TODO.md: "No PlayMode test coverage"):
    /// a real <see cref="MotionMatchingController"/> - live PlayableGraph, real searches - driven
    /// through scripted intent, asserting the machine holds together: initialization succeeds,
    /// frame/clip mapping stays in range, the Mecanim fade survives ticking, teardown and re-enable
    /// are clean. Runs on a synthetic database wrapped around a procedural clip, so no rig, mocap
    /// pack or scene asset is needed.
    ///
    /// The controller ticks in Manual mode and these tests call Step at a fixed timestep - plain
    /// [Test], no coroutines, no frame-pacing dependency. What this deliberately does not judge is
    /// whether the motion looks right; that stays a Play Mode eyeball job, per TODO.md.
    /// </summary>
    public sealed class MotionMatchingControllerPlayModeTests
    {
        private PlayModeTestRig _rig;

        [SetUp]
        public void SetUp() => _rig = PlayModeTestRig.Create();

        [TearDown]
        public void TearDown() => _rig.Dispose();

        [Test]
        public void SwitchDatabase_InitializesAndTicksWithoutErrors()
        {
            Assert.IsTrue(_rig.Controller.IsInitialized, "SwitchDatabase should have initialized the controller");

            _rig.Controller.DesiredVelocity = new Vector3(0f, 0f, 1.2f);
            _rig.Step(30);

            Assert.IsTrue(_rig.Controller.IsInitialized);
            Assert.AreEqual(0, _rig.Controller.CurrentClipIndex, "single-clip database: the active clip can only be 0");
            int frame = _rig.Controller.CurrentFrame;
            Assert.GreaterOrEqual(frame, 0);
            Assert.Less(frame, PlayModeTestRig.FrameCount, "mapped frame must stay inside the database");
        }

        [Test]
        public void ChangingIntent_KeepsFrameMappingInRange()
        {
            // Sweep intent through a few directions, stepping between each - exercises repeated
            // searches and transitions, the loop the EditMode tests can't drive.
            foreach (Vector3 intent in new[]
                     { Vector3.forward, Vector3.right, Vector3.zero, new Vector3(-1f, 0f, -1f) })
            {
                _rig.Controller.DesiredVelocity = intent;
                for (int i = 0; i < 15; i++)
                {
                    _rig.Step();
                    int frame = _rig.Controller.CurrentFrame;
                    Assert.GreaterOrEqual(frame, 0, $"frame mapping broke while heading {intent}");
                    Assert.Less(frame, PlayModeTestRig.FrameCount, $"frame mapping broke while heading {intent}");
                }
            }
        }

        [Test]
        public void SetMatchingActive_FadeOutAndBackIn_SurvivesTicking()
        {
            _rig.Controller.DesiredVelocity = Vector3.forward;

            _rig.Controller.SetMatchingActive(false, 0.05f);
            _rig.Step(10);
            Assert.IsFalse(_rig.Controller.IsMatchingActive);
            Assert.IsTrue(_rig.Controller.IsInitialized, "fading out must not tear the graph down");

            _rig.Controller.SetMatchingActive(true, 0.05f);
            _rig.Step(10);
            Assert.IsTrue(_rig.Controller.IsMatchingActive);
            Assert.IsTrue(_rig.Controller.IsInitialized);
        }

        [Test]
        public void DisablingComponent_TearsDownCleanly()
        {
            _rig.Step(5);

            _rig.Controller.enabled = false;
            Assert.IsFalse(_rig.Controller.IsInitialized);
            Assert.AreEqual(-1, _rig.Controller.CurrentClipIndex);

            // Re-enable: OnEnable re-initializes from the still-assigned database.
            _rig.Controller.enabled = true;
            Assert.IsTrue(_rig.Controller.IsInitialized, "re-enabling should rebuild the graph from the assigned database");
        }

        [Test]
        public void Step_InAutomaticMode_IsIgnored()
        {
            _rig.Controller.Ticking = MotionMatchingController.TickMode.Automatic;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Step ignored"));

            float before = _rig.Controller.CurrentClipTime;
            _rig.Controller.Step(1f);

            Assert.AreEqual(before, _rig.Controller.CurrentClipTime,
                "Step must not advance the clock in Automatic mode - Update owns it there");
        }
    }
}
