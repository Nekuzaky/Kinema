using System.Collections.Generic;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Classifies baked frames into gait categories from the motion itself - root speed and
    /// direction change read straight off the database's denormalized root velocity - instead of
    /// relying on clip naming conventions (TODO.md: "Speed/turn/idle detection from motion itself").
    /// Produces consolidated per-clip ranges suitable for tagging. Pure analysis over
    /// <see cref="MotionMatchingDatabase"/> data - nothing here writes tags; a config stays the
    /// author's to edit, so callers surface these as *suggestions*.
    /// </summary>
    public static class GaitClassifier
    {
        public enum Gait { Idle, Walk, Run }

        /// <summary>One consolidated run of same-gait frames inside a single clip. Times are local
        /// clip seconds, end-exclusive. <see cref="Turning"/> is orthogonal to the gait: a walking
        /// turn keeps Gait == Walk with Turning == true.</summary>
        public struct Range
        {
            public int ClipIndex;
            public float StartTime;
            public float EndTime;
            public Gait Gait;
            public bool Turning;
        }

        [System.Serializable]
        public struct Settings
        {
            [Tooltip("Speeds (m/s) at or below this are idle.")]
            public float IdleMaxSpeed;
            [Tooltip("Speeds (m/s) above this are running; between idle and this, walking.")]
            public float WalkMaxSpeed;
            [Tooltip("Root-velocity direction change (deg/s) above this flags the frame as turning. Only meaningful while moving - idle frames are never 'turning'.")]
            public float TurnMinDegreesPerSecond;
            [Tooltip("Runs shorter than this many frames are merged into their neighbours - absorbs single-frame classification flicker at gait boundaries.")]
            public int MinRangeFrames;

            public static Settings Default => new Settings
            {
                IdleMaxSpeed = 0.15f,
                WalkMaxSpeed = 2.5f,
                TurnMinDegreesPerSecond = 45f,
                MinRangeFrames = 4
            };
        }

        /// <summary>Classifies every frame of every clip in <paramref name="database"/> and returns
        /// consolidated ranges, clip by clip in frame order.</summary>
        public static List<Range> Classify(MotionMatchingDatabase database, Settings settings)
        {
            var ranges = new List<Range>();
            float dt = 1f / Mathf.Max(1, database.BakeFrameRate);

            for (int c = 0; c < database.ClipCount; c++)
            {
                MotionClipEntry clip = database.GetClip(c);
                int start = clip.StartFrame;
                int count = clip.FrameCount;
                if (count <= 0) continue;

                var gaits = new Gait[count];
                var turning = new bool[count];
                for (int f = 0; f < count; f++)
                {
                    Vector2 velocity = database.GetRootVelocity(start + f);
                    gaits[f] = ClassifySpeed(velocity.magnitude, settings);

                    if (f > 0 && gaits[f] != Gait.Idle)
                    {
                        Vector2 previous = database.GetRootVelocity(start + f - 1);
                        if (previous.sqrMagnitude > 1e-6f && velocity.sqrMagnitude > 1e-6f)
                        {
                            float degreesPerSecond = Vector2.Angle(previous, velocity) / dt;
                            turning[f] = degreesPerSecond >= settings.TurnMinDegreesPerSecond;
                        }
                    }
                }

                SmoothShortRuns(gaits, turning, settings.MinRangeFrames);
                AppendRuns(ranges, c, gaits, turning, dt);
            }

            return ranges;
        }

        public static Gait ClassifySpeed(float speed, Settings settings)
        {
            if (speed <= settings.IdleMaxSpeed) return Gait.Idle;
            return speed <= settings.WalkMaxSpeed ? Gait.Walk : Gait.Run;
        }

        /// <summary>Absorbs runs shorter than <paramref name="minFrames"/> into the preceding run's
        /// labels (the first run is exempt - there is nothing before it to merge into). One forward
        /// pass; a boundary flicker A-B-A collapses to all-A.</summary>
        private static void SmoothShortRuns(Gait[] gaits, bool[] turning, int minFrames)
        {
            if (minFrames <= 1) return;

            int runStart = 0;
            for (int f = 1; f <= gaits.Length; f++)
            {
                bool boundary = f == gaits.Length || gaits[f] != gaits[runStart] || turning[f] != turning[runStart];
                if (!boundary) continue;

                if (runStart > 0 && f - runStart < minFrames)
                {
                    for (int i = runStart; i < f; i++)
                    {
                        gaits[i] = gaits[runStart - 1];
                        turning[i] = turning[runStart - 1];
                    }
                    // The run merged backwards; keep scanning as part of that earlier run so a
                    // following short run still measures from the true label boundary.
                    for (runStart = f - 1; runStart > 0 && gaits[runStart - 1] == gaits[f - 1] && turning[runStart - 1] == turning[f - 1]; runStart--) { }
                }
                else
                {
                    runStart = f;
                }
            }
        }

        private static void AppendRuns(List<Range> ranges, int clipIndex, Gait[] gaits, bool[] turning, float dt)
        {
            int runStart = 0;
            for (int f = 1; f <= gaits.Length; f++)
            {
                if (f < gaits.Length && gaits[f] == gaits[runStart] && turning[f] == turning[runStart]) continue;

                ranges.Add(new Range
                {
                    ClipIndex = clipIndex,
                    StartTime = runStart * dt,
                    EndTime = f * dt,
                    Gait = gaits[runStart],
                    Turning = turning[runStart]
                });
                runStart = f;
            }
        }
    }
}
