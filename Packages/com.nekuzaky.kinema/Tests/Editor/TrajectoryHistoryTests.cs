using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// The past half of the trajectory feature is only as correct as this ring buffer's
    /// interpolation - a bug here would quietly feed the matcher a wrong "where we came from".
    /// </summary>
    public class TrajectoryHistoryTests
    {
        [Test]
        public void HasData_IsFalseBeforeAnyRecord()
        {
            var history = new TrajectoryHistory(8);
            Assert.IsFalse(history.HasData);
        }

        [Test]
        public void Sample_InterpolatesBetweenTwoRecordedPoints()
        {
            var history = new TrajectoryHistory(8);
            history.Record(0f, new Vector3(0f, 0f, 0f), Vector3.forward);
            history.Record(1f, new Vector3(10f, 0f, 0f), Vector3.forward);

            history.Sample(0.5f, out Vector3 pos, out _);

            Assert.AreEqual(5f, pos.x, 1e-4f, "midpoint in time should be the midpoint in space");
        }

        [Test]
        public void Sample_ClampsToTheOldestRecordWhenAskedForEarlierTime()
        {
            var history = new TrajectoryHistory(8);
            history.Record(5f, new Vector3(1f, 0f, 0f), Vector3.forward);
            history.Record(6f, new Vector3(2f, 0f, 0f), Vector3.forward);

            history.Sample(0f, out Vector3 pos, out _); // long before anything recorded

            Assert.AreEqual(1f, pos.x, 1e-4f, "should clamp to the oldest sample rather than extrapolate");
        }

        [Test]
        public void Record_WrapsAroundWithoutExceedingCapacity()
        {
            var history = new TrajectoryHistory(4);
            for (int i = 0; i < 10; i++)
                history.Record(i, new Vector3(i, 0f, 0f), Vector3.forward);

            // Only the last 4 records should remain; sampling near "now" must reflect recent data.
            history.Sample(9f, out Vector3 pos, out _);
            Assert.AreEqual(9f, pos.x, 1e-4f);
        }

        [Test]
        public void Clear_ResetsHasData()
        {
            var history = new TrajectoryHistory(4);
            history.Record(0f, Vector3.zero, Vector3.forward);
            Assert.IsTrue(history.HasData);

            history.Clear();
            Assert.IsFalse(history.HasData);
        }
    }
}
