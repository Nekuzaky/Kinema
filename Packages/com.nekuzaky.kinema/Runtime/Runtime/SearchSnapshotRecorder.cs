using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// One recorded matching decision, kept for post-hoc inspection. Carries enough playback state
    /// (both slots' clip/time, blend, mirror flag, and the character's transform) to deterministically
    /// reproduce the exact pose that was on screen at that moment - a real visual rewind, not just
    /// the cost numbers.
    /// </summary>
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

        // Enough playback state to visually rewind to this exact moment.
        public Vector3 CharacterPosition;
        public Quaternion CharacterRotation;
        public int ActiveSlot;
        public int Slot0ClipIndex;
        public double Slot0Time;
        public int Slot1ClipIndex;
        public double Slot1Time;
        public float Blend01;
        public bool Mirrored;
    }

    /// <summary>
    /// Fixed-size ring buffer of the last N matching decisions (Kinematica-style snapshot
    /// debugging). Slots are preallocated so recording is allocation-free in steady state; the
    /// editor's Debug tab scrubs through the history in play mode and can preview any recorded
    /// moment by replaying its captured playback state through the live graph.
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
            float[] groupCosts, TrajectorySample[] desired, TrajectorySample[] candidate,
            Vector3 characterPosition, Quaternion characterRotation, int activeSlot,
            int slot0ClipIndex, double slot0Time, int slot1ClipIndex, double slot1Time,
            float blend01, bool mirrored)
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

            s.CharacterPosition = characterPosition;
            s.CharacterRotation = characterRotation;
            s.ActiveSlot = activeSlot;
            s.Slot0ClipIndex = slot0ClipIndex;
            s.Slot0Time = slot0Time;
            s.Slot1ClipIndex = slot1ClipIndex;
            s.Slot1Time = slot1Time;
            s.Blend01 = blend01;
            s.Mirrored = mirrored;
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
