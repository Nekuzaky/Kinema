using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Delta between two recorded matching decisions, so the debug window can answer "what changed
    /// between this decision and that one" (TODO.md: snapshot debugger had no state diffing) instead
    /// of forcing the reader to eyeball two absolute cost readouts. Pure data + pure math - lives in
    /// the runtime assembly next to <see cref="SearchSnapshot"/> so it is unit-testable, but nothing
    /// at runtime computes one; only the editor window does, on demand.
    /// </summary>
    public sealed class SearchSnapshotDiff
    {
        public float TimeDelta;
        public bool FrameChanged;
        public bool ClipChanged;
        public float TotalCostDelta;
        /// <summary>Per-group cost deltas, B minus A. Same indexing as <see cref="SearchSnapshot.GroupCosts"/>.</summary>
        public float[] GroupCostDeltas;
        /// <summary>Index of the group with the largest absolute cost delta - the headline "what moved".</summary>
        public int DominantGroup;
        public float CharacterDistance;
        /// <summary>Mean point-to-point distance between the two desired trajectories - how much the
        /// *intent* differed. Large intent delta + large cost delta usually means the cost moved
        /// because the request moved, not because matching quality changed.</summary>
        public float DesiredTrajectoryDelta;
        public bool JumpedA;
        public bool JumpedB;

        /// <summary>Diffs <paramref name="b"/> against <paramref name="a"/> (deltas are B minus A).
        /// Snapshots come from the recorder's preallocated ring, so both may be live objects - all
        /// data is copied out, nothing references the inputs afterwards. Returns null if either
        /// input is null.</summary>
        public static SearchSnapshotDiff Compute(SearchSnapshot a, SearchSnapshot b)
        {
            if (a == null || b == null) return null;

            int groupCount = Mathf.Min(a.GroupCosts?.Length ?? 0, b.GroupCosts?.Length ?? 0);
            var diff = new SearchSnapshotDiff
            {
                TimeDelta = b.Time - a.Time,
                FrameChanged = a.SelectedFrame != b.SelectedFrame,
                ClipChanged = a.ClipIndex != b.ClipIndex,
                TotalCostDelta = b.TotalCost - a.TotalCost,
                GroupCostDeltas = new float[groupCount],
                CharacterDistance = Vector3.Distance(a.CharacterPosition, b.CharacterPosition),
                JumpedA = a.Jumped,
                JumpedB = b.Jumped
            };

            float dominantAbs = -1f;
            for (int i = 0; i < groupCount; i++)
            {
                float d = b.GroupCosts[i] - a.GroupCosts[i];
                diff.GroupCostDeltas[i] = d;
                if (Mathf.Abs(d) > dominantAbs)
                {
                    dominantAbs = Mathf.Abs(d);
                    diff.DominantGroup = i;
                }
            }

            int points = Mathf.Min(a.Desired?.Length ?? 0, b.Desired?.Length ?? 0);
            if (points > 0)
            {
                float sum = 0f;
                for (int i = 0; i < points; i++)
                    sum += Vector2.Distance(a.Desired[i].Position, b.Desired[i].Position);
                diff.DesiredTrajectoryDelta = sum / points;
            }

            return diff;
        }
    }
}
