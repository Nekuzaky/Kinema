using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// Covers the parts of <see cref="MotionMatchingController"/> that don't require a baked
    /// database, a running PlayableGraph or Play Mode - currently just the public
    /// <see cref="MotionMatchingController.SearchInterval"/> accessor used by
    /// <see cref="MotionMatchingLOD"/> to push cadence outside the inspector slider's 0.02-0.5 range.
    /// </summary>
    public sealed class MotionMatchingControllerTests
    {
        private GameObject _go;
        private MotionMatchingController _controller;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("MotionMatchingControllerTest");
            _controller = _go.AddComponent<MotionMatchingController>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void SearchInterval_ClampsBelowMinimum()
        {
            _controller.SearchInterval = -1f;
            Assert.AreEqual(0.02f, _controller.SearchInterval, 1e-5f);
        }

        [Test]
        public void SearchInterval_ClampsAboveMaximum()
        {
            _controller.SearchInterval = 100f;
            Assert.AreEqual(2f, _controller.SearchInterval, 1e-5f);
        }

        [Test]
        public void SearchInterval_PassesThroughInRangeValues()
        {
            _controller.SearchInterval = 0.4f;
            Assert.AreEqual(0.4f, _controller.SearchInterval, 1e-5f);
        }
    }
}
