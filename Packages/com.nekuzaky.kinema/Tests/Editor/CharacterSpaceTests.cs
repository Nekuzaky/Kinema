using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// CharacterSpace is the shared coordinate frame between bake, runtime and debug drawing - a
    /// sign error here would silently mirror trajectories or flip bone offsets everywhere at once.
    /// </summary>
    public class CharacterSpaceTests
    {
        [Test]
        public void ToLocalPoint_ThenToWorldPoint_RoundTrips()
        {
            var space = new CharacterSpace(new Vector3(3f, 0f, -2f), new Vector3(1f, 0f, 1f));
            Vector3 worldPoint = new Vector3(5f, 0f, 1f);

            Vector2 local = space.ToLocalPoint(worldPoint);
            Vector3 back = space.ToWorldPoint(local);

            Assert.AreEqual(worldPoint.x, back.x, 1e-4f);
            Assert.AreEqual(worldPoint.z, back.z, 1e-4f);
        }

        [Test]
        public void ToLocalDirection_ThenToWorldDirection_RoundTrips()
        {
            var space = new CharacterSpace(Vector3.zero, new Vector3(0f, 0f, -1f));
            Vector3 worldDir = new Vector3(1f, 0f, 1f).normalized;

            Vector2 local = space.ToLocalDirection(worldDir);
            Vector3 back = space.ToWorldDirection(local);

            Assert.AreEqual(worldDir.x, back.x, 1e-4f);
            Assert.AreEqual(worldDir.z, back.z, 1e-4f);
        }

        [Test]
        public void Forward_IsFlattenedAndNormalized()
        {
            var space = new CharacterSpace(Vector3.zero, new Vector3(0f, 5f, 3f));
            Assert.AreEqual(0f, space.Forward.y, 1e-5f, "forward must be projected onto the ground plane");
            Assert.AreEqual(1f, space.Forward.magnitude, 1e-4f);
        }

        [Test]
        public void Forward_FallsBackWhenGivenAVerticalVector()
        {
            var space = new CharacterSpace(Vector3.zero, Vector3.up);
            Assert.AreEqual(Vector3.forward, space.Forward, "a purely vertical hint has no ground-plane direction");
        }

        [Test]
        public void OriginIsProjectedToTheGroundPlane()
        {
            var space = new CharacterSpace(new Vector3(1f, 4f, 2f), Vector3.forward);
            Assert.AreEqual(0f, space.Origin.y, 1e-5f);
        }

        [Test]
        public void ToLocalOffset3D_ThenToWorldOffset3D_RoundTrips()
        {
            var space = new CharacterSpace(new Vector3(-1f, 0f, 2f), new Vector3(1f, 0f, 0f));
            Vector3 worldPoint = new Vector3(0.5f, 1.2f, 3f);

            Vector3 local = space.ToLocalOffset3D(worldPoint);
            Vector3 back = space.ToWorldOffset3D(local);

            Assert.AreEqual(worldPoint.x, back.x, 1e-4f);
            Assert.AreEqual(worldPoint.y, back.y, 1e-4f);
            Assert.AreEqual(worldPoint.z, back.z, 1e-4f);
        }

        [Test]
        public void ToLocalOffset3D_RoundTrips_WhenTheCharacterIsAboveTheWorldOrigin()
        {
            // Same round-trip, on a character standing on a hill. The pair must stay inverses
            // wherever the character is, not only at y = 0 where the bake happens to sample.
            var space = new CharacterSpace(new Vector3(-1f, 50f, 2f), new Vector3(1f, 0f, 0f));
            Vector3 worldPoint = new Vector3(0.5f, 51.2f, 3f);

            Vector3 back = space.ToWorldOffset3D(space.ToLocalOffset3D(worldPoint));

            Assert.AreEqual(worldPoint.x, back.x, 1e-4f);
            Assert.AreEqual(worldPoint.y, back.y, 1e-4f);
            Assert.AreEqual(worldPoint.z, back.z, 1e-4f);
        }

        [Test]
        public void BoneHeight_IsMeasuredFromTheCharacter_NotFromWorldZero()
        {
            // The struct's contract: features are "invariant to where the character stands". The
            // bake samples the rig at the world origin, so a foot 0.1 m off the ground bakes as
            // y = 0.1. The same pose on a hill at y = 50 must query as 0.1 too - if it queries as
            // 50.1 the cost function compares a height against a completely different quantity, and
            // every candidate frame is judged by how high the terrain is.
            var atOrigin = new CharacterSpace(new Vector3(0f, 0f, 0f), Vector3.forward);
            var onAHill = new CharacterSpace(new Vector3(0f, 50f, 0f), Vector3.forward);

            float atOriginHeight = atOrigin.ToLocalOffset3D(new Vector3(0f, 0.1f, 0f)).y;
            float onAHillHeight = onAHill.ToLocalOffset3D(new Vector3(0f, 50.1f, 0f)).y;

            Assert.AreEqual(0.1f, atOriginHeight, 1e-4f);
            Assert.AreEqual(atOriginHeight, onAHillHeight, 1e-4f,
                "the same pose must produce the same feature at any altitude");
        }

        [Test]
        public void ToLocalVector3D_IsUnaffectedByAltitude()
        {
            // Velocities are differences, so they were always altitude-invariant. Pinned so the
            // height fix cannot quietly introduce an offset here.
            var onAHill = new CharacterSpace(new Vector3(0f, 50f, 0f), Vector3.forward);
            Vector3 velocity = new Vector3(0f, 2f, 0f);

            Assert.AreEqual(2f, onAHill.ToLocalVector3D(velocity).y, 1e-4f);
        }
    }
}
