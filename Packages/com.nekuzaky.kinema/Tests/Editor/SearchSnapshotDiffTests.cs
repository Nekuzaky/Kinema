using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// <see cref="SearchSnapshotDiff.Compute"/> is the pure half of the snapshot-diffing feature
    /// (TODO.md: "Snapshot debugger has no state diffing") - fully covered here. The editor-window
    /// panel that renders a diff is IMGUI and stays eyeball-verified, like the rest of the window.
    /// </summary>
    public sealed class SearchSnapshotDiffTests
    {
        private static SearchSnapshot MakeSnapshot(
            float time = 0f, int frame = 0, int clip = 0, float totalCost = 1f,
            float[] groupCosts = null, Vector3 position = default, bool jumped = false,
            TrajectorySample[] desired = null)
        {
            return new SearchSnapshot
            {
                Time = time,
                SelectedFrame = frame,
                ClipIndex = clip,
                TotalCost = totalCost,
                GroupCosts = groupCosts ?? new float[] { 0f, 0f, 0f },
                Desired = desired ?? new TrajectorySample[0],
                Candidate = new TrajectorySample[0],
                CharacterPosition = position,
                Jumped = jumped
            };
        }

        [Test]
        public void Compute_NullInput_ReturnsNull()
        {
            Assert.IsNull(SearchSnapshotDiff.Compute(null, MakeSnapshot()));
            Assert.IsNull(SearchSnapshotDiff.Compute(MakeSnapshot(), null));
        }

        [Test]
        public void Compute_DeltasAreBMinusA()
        {
            var a = MakeSnapshot(time: 1f, totalCost: 2f);
            var b = MakeSnapshot(time: 3.5f, totalCost: 1.25f);

            var diff = SearchSnapshotDiff.Compute(a, b);

            Assert.AreEqual(2.5f, diff.TimeDelta, 1e-5f);
            Assert.AreEqual(-0.75f, diff.TotalCostDelta, 1e-5f);
        }

        [Test]
        public void Compute_FlagsFrameAndClipChanges()
        {
            var a = MakeSnapshot(frame: 10, clip: 0);
            var same = MakeSnapshot(frame: 10, clip: 0);
            var moved = MakeSnapshot(frame: 42, clip: 1);

            var noChange = SearchSnapshotDiff.Compute(a, same);
            Assert.IsFalse(noChange.FrameChanged);
            Assert.IsFalse(noChange.ClipChanged);

            var changed = SearchSnapshotDiff.Compute(a, moved);
            Assert.IsTrue(changed.FrameChanged);
            Assert.IsTrue(changed.ClipChanged);
        }

        [Test]
        public void Compute_DominantGroup_IsLargestAbsoluteDelta()
        {
            var a = MakeSnapshot(groupCosts: new float[] { 1f, 5f, 0f });
            var b = MakeSnapshot(groupCosts: new float[] { 1.2f, 2f, 0.5f }); // deltas: +0.2, -3, +0.5

            var diff = SearchSnapshotDiff.Compute(a, b);

            Assert.AreEqual(1, diff.DominantGroup, "group 1 moved by -3, the largest magnitude");
            Assert.AreEqual(-3f, diff.GroupCostDeltas[1], 1e-5f);
        }

        [Test]
        public void Compute_MismatchedGroupCounts_UsesTheSmaller()
        {
            var a = MakeSnapshot(groupCosts: new float[] { 1f, 2f });
            var b = MakeSnapshot(groupCosts: new float[] { 0f, 0f, 9f });

            var diff = SearchSnapshotDiff.Compute(a, b);

            Assert.AreEqual(2, diff.GroupCostDeltas.Length);
        }

        [Test]
        public void Compute_CharacterDistance_IsEuclidean()
        {
            var a = MakeSnapshot(position: Vector3.zero);
            var b = MakeSnapshot(position: new Vector3(3f, 0f, 4f));

            Assert.AreEqual(5f, SearchSnapshotDiff.Compute(a, b).CharacterDistance, 1e-5f);
        }

        [Test]
        public void Compute_DesiredTrajectoryDelta_IsMeanPointDistance()
        {
            var a = MakeSnapshot(desired: new[]
            {
                new TrajectorySample(new Vector2(0f, 0f), Vector2.up),
                new TrajectorySample(new Vector2(0f, 1f), Vector2.up)
            });
            var b = MakeSnapshot(desired: new[]
            {
                new TrajectorySample(new Vector2(1f, 0f), Vector2.up), // 1 away
                new TrajectorySample(new Vector2(0f, 4f), Vector2.up)  // 3 away
            });

            Assert.AreEqual(2f, SearchSnapshotDiff.Compute(a, b).DesiredTrajectoryDelta, 1e-5f);
        }

        [Test]
        public void Compute_EmptyTrajectories_LeaveDeltaZero()
        {
            var diff = SearchSnapshotDiff.Compute(MakeSnapshot(), MakeSnapshot());
            Assert.AreEqual(0f, diff.DesiredTrajectoryDelta);
        }
    }
}
