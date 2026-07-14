using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// A short ring buffer of recent root world transforms, so the query can include the character's
    /// actual <em>past</em> trajectory (where it came from) alongside the predicted future. Past
    /// trajectory is what lets the matcher pick poses that flow correctly out of turns instead of
    /// only reacting to the target.
    /// </summary>
    public sealed class TrajectoryHistory
    {
        #region Private and Protected

        private readonly int _capacity;
        private readonly float[] _time;
        private readonly Vector3[] _position;
        private readonly Vector3[] _forward;
        private int _head = -1;
        private int _count;

        #endregion

        #region Public

        public TrajectoryHistory(int capacity)
        {
            _capacity = Mathf.Max(4, capacity);
            _time = new float[_capacity];
            _position = new Vector3[_capacity];
            _forward = new Vector3[_capacity];
        }

        public bool HasData => _count > 0;

        #endregion

        #region Main API

        public void Record(float time, Vector3 position, Vector3 forward)
        {
            _head = (_head + 1) % _capacity;
            _time[_head] = time;
            _position[_head] = position;
            _forward[_head] = forward;
            if (_count < _capacity) _count++;
        }

        /// <summary>Interpolates the recorded root transform at a past absolute time (clamped to the buffer span).</summary>
        public void Sample(float atTime, out Vector3 position, out Vector3 forward)
        {
            position = _position[_head];
            forward = _forward[_head];
            if (_count == 0) return;

            int newer = _head;
            for (int k = 1; k < _count; k++)
            {
                int older = (_head - k + _capacity) % _capacity;
                if (_time[older] <= atTime)
                {
                    float span = _time[newer] - _time[older];
                    float u = span > 1e-6f ? (atTime - _time[older]) / span : 0f;
                    position = Vector3.Lerp(_position[older], _position[newer], u);
                    forward = Vector3.Slerp(_forward[older], _forward[newer], u);
                    return;
                }
                newer = older;
            }

            // Older than everything we kept -> clamp to the oldest sample.
            int oldest = (_head - (_count - 1) + _capacity) % _capacity;
            position = _position[oldest];
            forward = _forward[oldest];
        }

        public void Clear()
        {
            _head = -1;
            _count = 0;
        }

        #endregion
    }
}
