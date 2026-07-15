using System;

namespace Kinema.MotionMatching
{
    /// <summary>How much of one clip the matcher actually reaches for.</summary>
    public struct ClipCoverage
    {
        public int ClipIndex;
        public string Name;
        public int FrameCount;
        public int UsedFrames;
        public int Selections;

        /// <summary>Share of this clip's frames that were selected at least once.</summary>
        public float UsedFraction => FrameCount > 0 ? (float)UsedFrames / FrameCount : 0f;

        /// <summary>True when the matcher never once picked this clip: pure dead weight.</summary>
        public bool IsDead => Selections == 0;
    }

    /// <summary>
    /// Which parts of the database the matcher actually uses, built from per-frame selection counts.
    ///
    /// This answers a question motion matching normally leaves unanswered: <b>is my data any good?</b>
    /// Dead clips are mocap budget and memory spent on motion the matcher never chooses. Low coverage
    /// means the database is bigger than the gameplay needs. Rising cost with high coverage means the
    /// opposite - the data is exhausted and something is missing. Pure logic, no Unity dependency
    /// beyond the database read, so it is unit-testable.
    /// </summary>
    public sealed class CoverageReport
    {
        #region Public

        public int FrameCount { get; private set; }
        public int UsedFrames { get; private set; }
        public int DeadFrames => FrameCount - UsedFrames;
        public int TotalSelections { get; private set; }
        public ClipCoverage[] Clips { get; private set; } = Array.Empty<ClipCoverage>();

        /// <summary>Share of the database that was selected at least once.</summary>
        public float CoverageFraction => FrameCount > 0 ? (float)UsedFrames / FrameCount : 0f;

        /// <summary>Clips the matcher never picked at all.</summary>
        public int DeadClipCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Clips.Length; i++) if (Clips[i].IsDead) n++;
                return n;
            }
        }

        #endregion

        #region Main API

        /// <summary>Builds a report from a database and its per-frame selection counts.</summary>
        public static CoverageReport Build(MotionMatchingDatabase database, int[] usage)
        {
            var report = new CoverageReport();
            if (database == null || usage == null || usage.Length == 0) return report;

            int frames = Math.Min(database.FrameCount, usage.Length);
            report.FrameCount = frames;

            var clips = new ClipCoverage[database.ClipCount];
            for (int c = 0; c < database.ClipCount; c++)
            {
                MotionClipEntry entry = database.GetClip(c);
                var coverage = new ClipCoverage
                {
                    ClipIndex = c,
                    Name = entry.Name,
                    FrameCount = entry.FrameCount
                };

                int end = Math.Min(entry.EndFrameExclusive, frames);
                for (int f = entry.StartFrame; f < end; f++)
                {
                    if (usage[f] <= 0) continue;
                    coverage.UsedFrames++;
                    coverage.Selections += usage[f];
                }

                clips[c] = coverage;
                report.UsedFrames += coverage.UsedFrames;
                report.TotalSelections += coverage.Selections;
            }

            report.Clips = clips;
            return report;
        }

        #endregion
    }
}
