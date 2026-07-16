using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// PlayMode coverage for <see cref="MotionMatchingSearchBatch"/>: several real controllers route
    /// their searches through the batch (schedule on tick, complete together) and must behave as
    /// soundly as the synchronous path - valid frame mapping under ticking, a clean fallback when
    /// the batch is disabled, and no disposal under a running job when a controller tears down with
    /// a search in flight.
    ///
    /// Manual ticking makes the schedule/complete window explicit: Step schedules, then the test
    /// calls CompletePendingSearches - the same order LateUpdate produces, but without coroutines
    /// and without depending on frame pacing. What these tests cannot judge is the performance win
    /// itself - that's the benchmark's job (measured ~1.7x); this verifies the wiring.
    /// </summary>
    public sealed class MotionMatchingSearchBatchPlayModeTests
    {
        private const int CharacterCount = 3;

        private GameObject _batchGo;
        private MotionMatchingSearchBatch _batch;
        private MotionMatchingController[] _controllers;
        private MotionMatchingDatabase _db;
        private AnimationClip _clip;

        [SetUp]
        public void SetUp()
        {
            _clip = PlayModeTestRig.CreateLocomotionClip();
            _db = PlayModeTestRig.CreateDatabase(_clip);

            _controllers = new MotionMatchingController[CharacterCount];
            for (int i = 0; i < CharacterCount; i++)
                _controllers[i] = PlayModeTestRig.CreateCharacter($"BatchCharacter{i}", _db);

            // Batch created after the controllers so its empty-list auto-collect finds them all.
            _batchGo = new GameObject("SearchBatch");
            _batch = _batchGo.AddComponent<MotionMatchingSearchBatch>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_batchGo != null) Object.DestroyImmediate(_batchGo);
            if (_controllers != null)
                foreach (MotionMatchingController controller in _controllers)
                    if (controller != null) Object.DestroyImmediate(controller.gameObject);
            if (_db != null) Object.DestroyImmediate(_db);
            if (_clip != null) Object.DestroyImmediate(_clip);
        }

        /// <summary>One batched frame: every controller schedules, then all jobs complete together -
        /// exactly what Update-then-LateUpdate does in a running scene.</summary>
        private void StepAllBatched(int steps = 1)
        {
            for (int s = 0; s < steps; s++)
            {
                foreach (MotionMatchingController controller in _controllers)
                    if (controller != null && controller.isActiveAndEnabled) controller.Step(PlayModeTestRig.Dt);
                _batch.CompletePendingSearches();
            }
        }

        [Test]
        public void Batch_RoutesAllControllersAndKeepsThemSound()
        {
            foreach (MotionMatchingController controller in _controllers)
                Assert.IsTrue(ReferenceEquals(controller.SearchScheduler, _batch),
                    "auto-collect should have routed every controller through the batch");

            for (int i = 0; i < CharacterCount; i++)
                _controllers[i].DesiredVelocity = new Vector3(0f, 0f, 0.5f + i * 0.5f);

            for (int s = 0; s < 45; s++) // 1.5 s of batched searching
            {
                StepAllBatched();
                foreach (MotionMatchingController controller in _controllers)
                {
                    int frame = controller.CurrentFrame;
                    Assert.GreaterOrEqual(frame, 0, "batched searching broke frame mapping");
                    Assert.Less(frame, PlayModeTestRig.FrameCount, "batched searching broke frame mapping");
                }
            }
        }

        [Test]
        public void DisablingControllerWithSearchInFlight_DoesNotDisposeUnderTheJob()
        {
            // Regression: a controller torn down between scheduling its batched search and the
            // batch completing it used to dispose the matcher's NativeArrays while the Burst job
            // still read them - a safety-system error in the editor. Teardown now completes the
            // pending handle first. Manual ticking reproduces the window exactly: Step schedules
            // (the search timer starts at 0, so the very first tick after SwitchDatabase searches),
            // then the controller is disabled before CompletePendingSearches runs.
            _controllers[0].DesiredVelocity = Vector3.forward;
            _controllers[0].Step(PlayModeTestRig.Dt); // scheduled, not completed.

            _controllers[0].enabled = false;          // Teardown with the job in flight.
            _batch.CompletePendingSearches();         // stale handle must be completed and discarded.

            _controllers[0].enabled = true;
            Assert.IsTrue(_controllers[0].IsInitialized, "controller must re-initialize cleanly after the mid-flight teardown");

            _controllers[0].Ticking = MotionMatchingController.TickMode.Manual;
            for (int i = 0; i < 10; i++)
            {
                _controllers[0].Step(PlayModeTestRig.Dt);
                _batch.CompletePendingSearches();
            }
            int frame = _controllers[0].CurrentFrame;
            Assert.GreaterOrEqual(frame, 0);
            Assert.Less(frame, PlayModeTestRig.FrameCount);
        }

        [Test]
        public void DisablingBatch_RestoresSynchronousSearching()
        {
            foreach (MotionMatchingController controller in _controllers)
                controller.DesiredVelocity = Vector3.forward;
            StepAllBatched(15);

            _batch.enabled = false;

            foreach (MotionMatchingController controller in _controllers)
                Assert.IsNull(controller.SearchScheduler, "disabling the batch must hand back the synchronous path");

            // Controllers keep working on their own afterwards (searches complete inline now).
            foreach (MotionMatchingController controller in _controllers)
                for (int i = 0; i < 15; i++) controller.Step(PlayModeTestRig.Dt);

            foreach (MotionMatchingController controller in _controllers)
            {
                int frame = controller.CurrentFrame;
                Assert.GreaterOrEqual(frame, 0);
                Assert.Less(frame, PlayModeTestRig.FrameCount);
            }
        }
    }
}
