using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// Covers what <see cref="AICommandProvider"/>'s obstacle avoidance decides, against real
    /// colliders. Each test pins one of the rules that make the difference between an agent that
    /// reads the world and one that grinds along it.
    ///
    /// Like the rest of the PlayMode suite these own the clock (`TickMode.Manual` + `Step`) rather
    /// than yielding frames. That is not a style choice here: the steer is smoothed against dt, and a
    /// headless frame lasts microseconds, so yielding frames advanced the smoothing by nearly nothing
    /// and every agent read as "not steering" regardless of what it had decided.
    /// </summary>
    internal sealed class AIAvoidancePlayModeTests
    {
        private readonly List<Object> _spawned = new();

        #region Unity API

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        #endregion

        #region Tests

        [Test]
        public void WallAhead_SteersAside()
        {
            AICommandProvider agent = SpawnAgent(Vector3.zero);
            SpawnBox("Wall", new Vector3(0f, 1f, 1.5f), new Vector3(6f, 2f, 0.5f));

            Settle(agent, AIAgentCommand.MoveTo(new Vector3(0f, 0f, 12f), 1f, "test"));

            Assert.IsTrue(agent.Avoiding, "A 2 m wall 1.5 m ahead is inside the feelers; the agent should be steering.");
            Assert.Greater(Mathf.Abs(agent.DesiredVelocity.normalized.x), 0.1f,
                "Steering should push the velocity off the straight-ahead axis.");
        }

        [Test]
        public void RampAhead_ClimbsInsteadOfSteering()
        {
            AICommandProvider agent = SpawnAgent(Vector3.zero);

            // A 30-degree face - inside the 45-degree walkable default. Its collider still reaches
            // metres up, which is exactly the trap: judged by height this reads as a wall.
            GameObject ramp = SpawnBox("Ramp", new Vector3(0f, 0f, 1f), new Vector3(6f, 0.5f, 6f));
            ramp.transform.rotation = Quaternion.Euler(-30f, 0f, 0f);
            Physics.SyncTransforms();

            // Without this the test is vacuous: if the feeler misses the ramp entirely, Avoiding is
            // false because nothing was found, not because a slope was judged walkable.
            Assert.IsTrue(Physics.Raycast(agent.transform.position + Vector3.up * 0.6f, Vector3.forward,
                    out RaycastHit hit, 2.5f),
                "Test setup: the feeler must actually strike the ramp for this to prove anything.");
            Assert.Less(Vector3.Angle(hit.normal, Vector3.up), 45f, "Test setup: the struck face must be walkable.");
            Assert.Greater(hit.collider.bounds.max.y, 1f,
                "Test setup: the ramp's collider must reach high enough that a height test would call it a wall.");

            Settle(agent, AIAgentCommand.MoveTo(new Vector3(0f, 0f, 12f), 1f, "test"));

            Assert.IsFalse(agent.Avoiding, "A walkable slope is not an obstacle - the agent should climb it, not round it.");
        }

        [Test]
        public void LowCrate_IsSteppedOverNotAvoided()
        {
            AICommandProvider agent = SpawnAgent(Vector3.zero);
            SpawnBox("Crate", new Vector3(0f, 0.1f, 1.5f), new Vector3(2f, 0.2f, 0.5f));

            Settle(agent, AIAgentCommand.MoveTo(new Vector3(0f, 0f, 12f), 1f, "test"));

            Assert.Greater(agent.DesiredVelocity.z, 0.1f,
                "The agent should be walking at the crate - otherwise 'not avoiding' proves nothing.");
            Assert.IsFalse(agent.Avoiding, "A crate below the passable height is stepped over, so it must not trigger steering.");
        }

        [Test]
        public void FollowTarget_IsNotTreatedAsAnObstacle()
        {
            // The player stands closer than the feelers reach. If it counted as an obstacle the agent
            // would swerve away from the very thing it is closing on, and orbit it forever.
            MotionMatchingController player = SpawnPlayer(new Vector3(0f, 0f, 1.5f));
            AICommandProvider agent = SpawnAgent(Vector3.zero);

            Settle(agent, AIAgentCommand.FollowTarget(player.transform, 1f, "test"));

            Assert.IsFalse(agent.Avoiding, "A Follow target must read as clear, not as a wall.");
        }

        [Test]
        public void OpenGround_DoesNotSteer()
        {
            AICommandProvider agent = SpawnAgent(Vector3.zero);

            Settle(agent, AIAgentCommand.MoveTo(new Vector3(0f, 0f, 12f), 1f, "test"));

            Assert.IsFalse(agent.Avoiding, "Nothing ahead: the agent should run straight.");
            Assert.Greater(agent.DesiredVelocity.z, 0.1f, "It should still be heading for the goal.");
        }

        [Test]
        public void MaxSpeed_StaysInsideTheRangeTheDataCovers()
        {
            // The player's provider caps at 3 m/s because that is what the bake actually holds.
            // An agent asking for more starves the search and slides the feet, so it must not.
            AICommandProvider agent = SpawnAgent(Vector3.zero);

            Settle(agent, AIAgentCommand.MoveTo(new Vector3(0f, 0f, 40f), 1f, "test"));

            // A cap is an assertion of absence, so it needs proof the agent is actually moving:
            // a stalled agent exceeds no speed limit and would pass this on a dead provider.
            Assert.Greater(agent.DesiredVelocity.magnitude, 0.1f, "The agent should be underway at all.");
            Assert.LessOrEqual(agent.DesiredVelocity.magnitude, 3f + 1e-3f,
                "A full-speed command must not exceed the 3 m/s the database covers.");
        }

        #endregion

        #region Tools and Utilities

        /// <summary>
        /// Pushes a command and steps the agent enough for the smoothed steer to build up - a full
        /// second at a fixed 1/30 s, which is what the smoothing needs to reach its target.
        ///
        /// Stepping rather than yielding frames is what makes these tests mean anything: the steer is
        /// damped against dt, and a headless frame takes microseconds, so yielding 30 real frames
        /// advanced the smoothing by almost nothing and the agent read as "not steering" no matter
        /// what it had decided.
        /// </summary>
        private static void Settle(AICommandProvider agent, AIAgentCommand command)
        {
            Physics.SyncTransforms();
            for (int i = 0; i < 30; i++)
            {
                // Re-issued every step: the override is what stands in for a brain here, and it
                // lapses on a timer.
                agent.OverrideCommand(command, 5f);
                agent.Step(1f / 30f);
            }
        }

        /// <summary>
        /// Floor under the whole test area. Without it every one of these tests is a lie: the
        /// provider stops at ledges by default, an agent standing over a void has nowhere to walk, so
        /// its velocity is zero - which fails the tests that assert movement and, worse, silently
        /// satisfies the ones that assert an absence ("not avoiding", "speed within range"). A
        /// standing-still agent avoids nothing and exceeds no speed limit.
        /// </summary>
        private void SpawnGround()
        {
            SpawnBox("Ground", new Vector3(0f, -0.5f, 8f), new Vector3(60f, 1f, 60f));
        }

        private AICommandProvider SpawnAgent(Vector3 position)
        {
            SpawnGround();
            MotionMatchingController controller = SpawnCharacter("AI Agent", position);
            var provider = controller.gameObject.AddComponent<AICommandProvider>();
            provider.Ticking = MotionMatchingController.TickMode.Manual;
            return provider;
        }

        /// <summary>A character with no AICommandProvider - which is exactly how the provider finds
        /// the player. Must exist before the agent's Awake runs.</summary>
        private MotionMatchingController SpawnPlayer(Vector3 position)
        {
            MotionMatchingController player = SpawnCharacter("Player", position);
            var capsule = player.gameObject.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            Physics.SyncTransforms();
            return player;
        }

        private MotionMatchingController SpawnCharacter(string name, Vector3 position)
        {
            AnimationClip clip = PlayModeTestRig.CreateLocomotionClip();
            MotionMatchingDatabase database = PlayModeTestRig.CreateDatabase(clip);
            _spawned.Add(clip);
            _spawned.Add(database);

            MotionMatchingController controller = PlayModeTestRig.CreateCharacter(name, database);
            controller.transform.position = position;
            _spawned.Add(controller.gameObject);
            return controller;
        }

        private GameObject SpawnBox(string name, Vector3 centre, Vector3 size)
        {
            var box = new GameObject(name);
            box.transform.position = centre;
            box.AddComponent<BoxCollider>().size = size;
            _spawned.Add(box);
            Physics.SyncTransforms();
            return box;
        }

        #endregion
    }
}
