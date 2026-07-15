using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// Coverage analysis is pure logic over a database plus selection counts, so it is fully
    /// testable without a scene: the cases below pin the dead-data detection that the Analysis tab
    /// reports on.
    /// </summary>
    public sealed class CoverageReportTests
    {
        private MotionMatchingDatabase _database;

        [SetUp]
        public void SetUp()
        {
            // Two clips of 4 frames each; feature content is irrelevant to coverage.
            _database = TestDatabaseFactory.Create(frameCount: 8, clipFrameCounts: new[] { 4, 4 });
        }

        [TearDown]
        public void TearDown()
        {
            if (_database != null) Object.DestroyImmediate(_database);
        }

        [Test]
        public void Build_WithNullUsage_ReturnsEmptyReport()
        {
            CoverageReport report = CoverageReport.Build(_database, null);

            Assert.AreEqual(0, report.FrameCount);
            Assert.AreEqual(0, report.TotalSelections);
            Assert.AreEqual(0f, report.CoverageFraction);
        }

        [Test]
        public void Build_CountsSelectionsAndUsedFrames()
        {
            var usage = new int[8];
            usage[0] = 3; // clip 0, frame 0 picked three times
            usage[1] = 1; // clip 0, frame 1 picked once
            usage[5] = 2; // clip 1, frame 1 picked twice

            CoverageReport report = CoverageReport.Build(_database, usage);

            Assert.AreEqual(8, report.FrameCount);
            Assert.AreEqual(6, report.TotalSelections);
            Assert.AreEqual(3, report.UsedFrames);
            Assert.AreEqual(5, report.DeadFrames);
            Assert.AreEqual(3f / 8f, report.CoverageFraction, 1e-5f);
        }

        [Test]
        public void Build_AttributesSelectionsToTheOwningClip()
        {
            var usage = new int[8];
            usage[0] = 3;
            usage[5] = 2;

            CoverageReport report = CoverageReport.Build(_database, usage);

            Assert.AreEqual(2, report.Clips.Length);
            Assert.AreEqual(3, report.Clips[0].Selections);
            Assert.AreEqual(1, report.Clips[0].UsedFrames);
            Assert.AreEqual(2, report.Clips[1].Selections);
            Assert.AreEqual(1, report.Clips[1].UsedFrames);
            Assert.AreEqual(0.25f, report.Clips[0].UsedFraction, 1e-5f);
        }

        [Test]
        public void Build_FlagsNeverSelectedClipsAsDead()
        {
            var usage = new int[8];
            usage[2] = 5; // only clip 0 is ever used

            CoverageReport report = CoverageReport.Build(_database, usage);

            Assert.IsFalse(report.Clips[0].IsDead);
            Assert.IsTrue(report.Clips[1].IsDead, "A clip with zero selections must be reported as dead data.");
            Assert.AreEqual(1, report.DeadClipCount);
        }

        [Test]
        public void Build_WithNoSelections_ReportsEverythingDead()
        {
            CoverageReport report = CoverageReport.Build(_database, new int[8]);

            Assert.AreEqual(0, report.UsedFrames);
            Assert.AreEqual(8, report.DeadFrames);
            Assert.AreEqual(2, report.DeadClipCount);
            Assert.AreEqual(0f, report.CoverageFraction);
        }

        [Test]
        public void Build_ToleratesUsageShorterThanDatabase()
        {
            var usage = new int[4]; // only half the frames reported
            usage[0] = 1;

            CoverageReport report = CoverageReport.Build(_database, usage);

            Assert.AreEqual(4, report.FrameCount, "Frame count clamps to the shorter of database and usage.");
            Assert.AreEqual(1, report.TotalSelections);
        }
    }
}
