using System;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// One recorded frame of a play session: the locomotion intent that drove it plus what the
    /// matcher did with it. Intent is what gets replayed; the rest is captured so a replay can be
    /// diffed against the original run.
    /// </summary>
    [Serializable]
    public struct SessionFrame
    {
        public float DeltaTime;
        public Vector3 DesiredVelocity;
        public Vector3 DesiredFacing;

        // Outcome of that frame, for verification and analysis.
        public Vector3 Position;
        public Quaternion Rotation;
        public int SelectedFrame;
        public float Cost;
        public bool Jumped;
    }

    /// <summary>
    /// A recorded play session: the full stream of locomotion intent, timestep included.
    /// Replaying it feeds the exact same intent back through the controller, so two tuning setups
    /// can be compared on identical input instead of on two different human performances - the
    /// difference between measuring a change and guessing at it.
    /// </summary>
    [PreferBinarySerialization]
    public sealed class SessionRecording : ScriptableObject
    {
        #region Public

        [SerializeField] private SessionFrame[] _frames = Array.Empty<SessionFrame>();
        [SerializeField] private string _databaseName;
        [SerializeField] private int _databaseFrameCount;
        [SerializeField] private string _recordedUtc;
        [SerializeField] private float _duration;

        public SessionFrame[] Frames => _frames;
        public int FrameCount => _frames.Length;
        public string DatabaseName => _databaseName;
        public int DatabaseFrameCount => _databaseFrameCount;
        public string RecordedUtc => _recordedUtc;
        public float Duration => _duration;

        public bool IsValid => _frames.Length > 0;

        #endregion

        #region Main API

        public void SetRecording(SessionFrame[] frames, string databaseName, int databaseFrameCount, string recordedUtc)
        {
            _frames = frames ?? Array.Empty<SessionFrame>();
            _databaseName = databaseName;
            _databaseFrameCount = databaseFrameCount;
            _recordedUtc = recordedUtc;

            _duration = 0f;
            for (int i = 0; i < _frames.Length; i++) _duration += _frames[i].DeltaTime;
        }

        /// <summary>Average matching cost across the recorded session.</summary>
        public float AverageCost()
        {
            if (_frames.Length == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < _frames.Length; i++) sum += _frames[i].Cost;
            return sum / _frames.Length;
        }

        /// <summary>Clip switches per second - the flicker metric.</summary>
        public float JumpsPerSecond()
        {
            if (_duration <= 1e-4f) return 0f;
            int jumps = 0;
            for (int i = 0; i < _frames.Length; i++) if (_frames[i].Jumped) jumps++;
            return jumps / _duration;
        }

        /// <summary>Total distance travelled by the root, in meters.</summary>
        public float DistanceTravelled()
        {
            float d = 0f;
            for (int i = 1; i < _frames.Length; i++)
                d += Vector3.Distance(_frames[i].Position, _frames[i - 1].Position);
            return d;
        }

        #endregion
    }
}
