#if KINEMA_TIMELINE
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using Kinema.MotionMatching.Timeline;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// Builds a real <see cref="PlayableGraph"/> (mixer + clip playables, exactly the shape
    /// <see cref="MotionMatchingTrack"/> produces) and evaluates it directly - no Timeline asset, no
    /// PlayableDirector, no Timeline window needed. This exercises the actual Unity Playables
    /// evaluation path <see cref="MotionMatchingMixerBehaviour.ProcessFrame"/> runs on, which is the
    /// part of the Timeline integration verifiable without opening the Timeline window; whether an
    /// authored clip's ease-in/ease-out feels right in the Timeline UI is not (same caveat as the
    /// rest of the runtime-feel gaps in TODO.md).
    /// </summary>
    public sealed class MotionMatchingTimelineTests
    {
        private GameObject _go;
        private MotionMatchingController _controller;
        private PlayableGraph _graph;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("MotionMatchingTimelineTest");
            _controller = _go.AddComponent<MotionMatchingController>();
            _graph = PlayableGraph.Create("MotionMatchingTimelineTest");
        }

        [TearDown]
        public void TearDown()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private (ScriptPlayable<MotionMatchingMixerBehaviour> mixer, ScriptPlayable<MotionMatchingBehaviour> clip) BuildOneClipMixer(float fadeTime)
        {
            var mixer = ScriptPlayable<MotionMatchingMixerBehaviour>.Create(_graph, 1);
            var clip = ScriptPlayable<MotionMatchingBehaviour>.Create(_graph);
            clip.GetBehaviour().FadeTime = fadeTime;
            _graph.Connect(clip, 0, mixer, 0);
            mixer.SetInputWeight(0, 0f);

            var output = ScriptPlayableOutput.Create(_graph, "test");
            output.SetSourcePlayable(mixer);
            output.SetUserData(_controller);

            return (mixer, clip);
        }

        [Test]
        public void NoClipActive_DoesNotTouchController()
        {
            BuildOneClipMixer(0.2f);
            bool before = _controller.IsMatchingActive;

            _graph.Evaluate();

            Assert.AreEqual(before, _controller.IsMatchingActive);
        }

        [Test]
        public void ClipGainsWeight_ActivatesMatching()
        {
            var (mixer, _) = BuildOneClipMixer(0.2f);

            mixer.SetInputWeight(0, 1f);
            _graph.Evaluate();

            Assert.IsTrue(_controller.IsMatchingActive);
        }

        [Test]
        public void ClipLosesWeight_RestoresPreviousState()
        {
            // Controller defaults to IsMatchingActive == true (_outputTarget starts at 1); force a
            // known starting state rather than relying on that default.
            _controller.SetMatchingActive(false, 0.01f);
            var (mixer, _) = BuildOneClipMixer(0.2f);

            mixer.SetInputWeight(0, 1f);
            _graph.Evaluate();
            Assert.IsTrue(_controller.IsMatchingActive);

            mixer.SetInputWeight(0, 0f);
            _graph.Evaluate();

            Assert.IsFalse(_controller.IsMatchingActive);
        }

        [Test]
        public void SecondControlSpan_RestoresStateFromJustBeforeIt_NotFromBeforeTheFirst()
        {
            _controller.SetMatchingActive(false, 0.01f);
            var (mixer, _) = BuildOneClipMixer(0.2f);

            // Span 1: control on, then off - restores the pre-span-1 state (inactive).
            mixer.SetInputWeight(0, 1f);
            _graph.Evaluate();
            mixer.SetInputWeight(0, 0f);
            _graph.Evaluate();
            Assert.IsFalse(_controller.IsMatchingActive);

            // Between spans, a script activates matching on its own.
            _controller.SetMatchingActive(true, 0.01f);

            // Span 2: control on, then off - must restore the state from just before span 2 (active),
            // not the stale pre-span-1 capture (inactive).
            mixer.SetInputWeight(0, 1f);
            _graph.Evaluate();
            mixer.SetInputWeight(0, 0f);
            _graph.Evaluate();

            Assert.IsTrue(_controller.IsMatchingActive,
                "the second span's restore must honour the script's toggle made between the spans");
        }

        [Test]
        public void TwoOverlappingClips_StayActiveAcrossTheHandoff()
        {
            var mixer = ScriptPlayable<MotionMatchingMixerBehaviour>.Create(_graph, 2);
            var clipA = ScriptPlayable<MotionMatchingBehaviour>.Create(_graph);
            var clipB = ScriptPlayable<MotionMatchingBehaviour>.Create(_graph);
            _graph.Connect(clipA, 0, mixer, 0);
            _graph.Connect(clipB, 0, mixer, 1);

            var output = ScriptPlayableOutput.Create(_graph, "test");
            output.SetSourcePlayable(mixer);
            output.SetUserData(_controller);

            // Clip A active, B not yet.
            mixer.SetInputWeight(0, 1f);
            mixer.SetInputWeight(1, 0f);
            _graph.Evaluate();
            Assert.IsTrue(_controller.IsMatchingActive);

            // Crossfade midpoint: both nonzero.
            mixer.SetInputWeight(0, 0.5f);
            mixer.SetInputWeight(1, 0.5f);
            _graph.Evaluate();
            Assert.IsTrue(_controller.IsMatchingActive);

            // B fully takes over.
            mixer.SetInputWeight(0, 0f);
            mixer.SetInputWeight(1, 1f);
            _graph.Evaluate();
            Assert.IsTrue(_controller.IsMatchingActive, "matching should stay active through a crossfade between two clips on the track");
        }
    }
}
#endif
