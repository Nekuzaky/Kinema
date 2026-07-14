using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>One recorded matching decision, kept for post-hoc inspection.</summary>
    public sealed class SearchSnapshot
    {
        public float Time;
        public int SelectedFrame;
        public int ClipIndex;
        public float ClipTime;
        public float TotalCost;
        public float ContinuationCost;
        public bool Jumped;
        public float[] GroupCosts;
        public TrajectorySample[] Desired;
        public TrajectorySample[] Candidate;
    }

    /// <summary>
    /// Fixed-size ring buffer of the last N matching decisions (Kinematica-style snapshot
    /// debugging, without the re-execution). Slots are preallocated so recording is allocation-free
    /// in steady state; the editor's Debug tab scrubs through the history in play mode.
    /// </summary>
    public sealed class SearchSnapshotRecorder
    {
        #region Private and Protected

        private readonly SearchSnapshot[] _ring;
        private int _head = -1;
        private int _count;

        #endregion

        #region Public

        public SearchSnapshotRecorder(int capacity, int groupCount, int trajectoryPoints)
        {
            _ring = new SearchSnapshot[Mathf.Max(8, capacity)];
            for (int i = 0; i < _ring.Length; i++)
            {
                _ring[i] = new SearchSnapshot
                {
                    GroupCosts = new float[groupCount],
                    Desired = new TrajectorySample[trajectoryPoints],
                    Candidate = new TrajectorySample[trajectoryPoints]
                };
            }
        }

        public int Count => _count;
        public int Capacity => _ring.Length;

        #endregion

        #region Main API

        /// <summary>Records one decision. Arrays are copied into the preallocated slot.</summary>
        public void Record(
            float time, int frame, int clipIndex, float clipTime,
            float totalCost, float continuationCost, bool jumped,
            float[] groupCosts, TrajectorySample[] desired, TrajectorySample[] candidate)
        {
            _head = (_head + 1) % _ring.Length;
            if (_count < _ring.Length) _count++;

            SearchSnapshot s = _ring[_head];
            s.Time = time;
            s.SelectedFrame = frame;
            s.ClipIndex = clipIndex;
            s.ClipTime = clipTime;
            s.TotalCost = totalCost;
            s.ContinuationCost = continuationCost;
            s.Jumped = jumped;

            for (int i = 0; i < s.GroupCosts.Length && i < groupCosts.Length; i++) s.GroupCosts[i] = groupCosts[i];
            for (int i = 0; i < s.Desired.Length && i < desired.Length; i++) s.Desired[i] = desired[i];
            for (int i = 0; i < s.Candidate.Length && i < candidate.Length; i++) s.Candidate[i] = candidate[i];
        }

        /// <summary>Snapshot by age: 0 = most recent, Count-1 = oldest.</summary>
        public SearchSnapshot GetByAge(int age)
        {
            if (age < 0 || age >= _count) return null;
            int index = (_head - age + _ring.Length) % _ring.Length;
            return _ring[index];
        }

        public void Clear()
        {
            _head = -1;
            _count = 0;
        }

        #endregion
    }
}
