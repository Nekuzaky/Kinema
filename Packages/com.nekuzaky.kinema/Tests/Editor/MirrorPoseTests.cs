using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// Pins the reflection <see cref="MirrorPose"/> applies, and the naming it pairs bones by.
    ///
    /// Worth pinning because mirroring fails quietly. A wrong reflection does not throw: the
    /// character plays a plausible pose that is not the one the search costed, and the only symptom
    /// is motion that looks subtly off. Doubling a database with mirrored variants is the cheapest
    /// coverage there is - and only worth doing if this half is provably right.
    ///
    /// What these cannot cover: whether a given rig's left and right bones carry mirrored local axes.
    /// Mirroring local transforms assumes they do, and many rigs (Mixamo among them) give both sides
    /// the same axes instead. That assumption is why the class is marked experimental, and no test
    /// without a rig in it can settle it.
    /// </summary>
    public class MirrorPoseTests
    {
        #region Reflection

        [Test]
        public void ReflectPosition_FlipsXOnly()
        {
            Vector3 mirrored = MirrorPose.ReflectPosition(new Vector3(0.3f, 1.2f, -0.5f));

            Assert.AreEqual(-0.3f, mirrored.x, 1e-6f);
            Assert.AreEqual(1.2f, mirrored.y, 1e-6f, "height must survive a left/right mirror");
            Assert.AreEqual(-0.5f, mirrored.z, 1e-6f, "forward must survive a left/right mirror");
        }

        [Test]
        public void Reflect_IsItsOwnInverse()
        {
            // A mirror applied twice is the identity. If it were not, playback would drift every time
            // the matcher crossed between a frame and its twin.
            var p = new Vector3(0.3f, 1.2f, -0.5f);
            Assert.AreEqual(p, MirrorPose.ReflectPosition(MirrorPose.ReflectPosition(p)));

            Quaternion q = Quaternion.Euler(11f, 34f, -57f);
            Quaternion twice = MirrorPose.ReflectRotation(MirrorPose.ReflectRotation(q));
            Assert.AreEqual(0f, Quaternion.Angle(q, twice), 1e-3f);
        }

        [Test]
        public void ReflectRotation_KeepsTurnsAboutX_AndReversesTurnsAboutYAndZ()
        {
            // M = diag(-1,1,1) maps R to M R M: a rotation about M*axis by -angle. The x axis is the
            // one the mirror leaves alone, so a turn about it is unchanged; the other two reverse.
            AssertSameRotation(Quaternion.AngleAxis(40f, Vector3.right),
                MirrorPose.ReflectRotation(Quaternion.AngleAxis(40f, Vector3.right)));

            AssertSameRotation(Quaternion.AngleAxis(-40f, Vector3.up),
                MirrorPose.ReflectRotation(Quaternion.AngleAxis(40f, Vector3.up)));

            AssertSameRotation(Quaternion.AngleAxis(-40f, Vector3.forward),
                MirrorPose.ReflectRotation(Quaternion.AngleAxis(40f, Vector3.forward)));
        }

        [Test]
        public void ReflectRotation_MatchesReflectingTheVectorsItRotates()
        {
            // The definition, checked end to end: mirroring a rotation must be the same as mirroring
            // its input, rotating, and mirroring back. This is the property the job's formula exists
            // to satisfy, independent of how it happens to be written.
            Quaternion q = Quaternion.Euler(23f, -41f, 15f);
            var v = new Vector3(0.4f, -0.7f, 0.2f);

            Vector3 viaFormula = MirrorPose.ReflectRotation(q) * v;
            Vector3 viaDefinition = MirrorPose.ReflectPosition(q * MirrorPose.ReflectPosition(v));

            Assert.AreEqual(viaDefinition.x, viaFormula.x, 1e-4f);
            Assert.AreEqual(viaDefinition.y, viaFormula.y, 1e-4f);
            Assert.AreEqual(viaDefinition.z, viaFormula.z, 1e-4f);
        }

        #endregion

        #region Pairing

        [Test]
        public void MirrorName_SwapsSidesBothWays()
        {
            Assert.AreEqual("RightFoot", MirrorPose.MirrorName("LeftFoot"));
            Assert.AreEqual("LeftFoot", MirrorPose.MirrorName("RightFoot"));
            Assert.AreEqual("mixamorig1:RightArm", MirrorPose.MirrorName("mixamorig1:LeftArm"));
            Assert.AreEqual("mixamorig1:LeftUpLeg", MirrorPose.MirrorName("mixamorig1:RightUpLeg"));
        }

        [Test]
        public void MirrorName_LeavesCentreBonesUnpaired()
        {
            // Null means "pair with yourself": a spine has no counterpart and must be reflected in
            // place, not swapped with anything.
            Assert.IsNull(MirrorPose.MirrorName("Hips"));
            Assert.IsNull(MirrorPose.MirrorName("mixamorig1:Spine1"));
            Assert.IsNull(MirrorPose.MirrorName("Head"));
        }

        [Test]
        public void MirrorName_PairingIsSymmetric()
        {
            // Every bone must be its partner's partner. If it were not, two bones could claim the same
            // source and one side of the body would be dropped.
            foreach (string name in new[] { "LeftFoot", "RightHand", "mixamorig1:LeftToeBase" })
                Assert.AreEqual(name, MirrorPose.MirrorName(MirrorPose.MirrorName(name)));
        }

        #endregion

        private static void AssertSameRotation(Quaternion expected, Quaternion actual) =>
            Assert.AreEqual(0f, Quaternion.Angle(expected, actual), 1e-3f,
                $"expected {expected.eulerAngles}, got {actual.eulerAngles}");
    }
}
