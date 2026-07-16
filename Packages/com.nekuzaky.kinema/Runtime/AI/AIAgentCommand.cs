using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>What a brain wants the body to do. High-level intent, not locomotion.</summary>
    public enum AIGoal
    {
        Idle,
        MoveTo,   // walk to Position
        Follow,   // stay near Target
        Flee,     // move away from Target
        Patrol,   // handled by the brain issuing MoveTo between waypoints
        Wander    // handled by the brain issuing MoveTo to random points
    }

    /// <summary>
    /// One high-level order for an AI agent. A brain (scripted or LLM) produces these; an
    /// <see cref="AICommandProvider"/> turns them into the same locomotion intent a player's input
    /// would, so the NPC drives the identical motion matching stack. Keeping the two layers apart -
    /// deciding *what to do* vs *how to move* - is what lets an LLM steer a character without knowing
    /// anything about animation.
    /// </summary>
    [System.Serializable]
    public struct AIAgentCommand
    {
        public AIGoal Goal;

        /// <summary>World destination for <see cref="AIGoal.MoveTo"/>.</summary>
        public Vector3 Position;

        /// <summary>Subject for <see cref="AIGoal.Follow"/> / <see cref="AIGoal.Flee"/>.</summary>
        public Transform Target;

        /// <summary>0..1 scale on the agent's max speed - a brain can ask for a walk or a sprint.</summary>
        [Range(0f, 1f)] public float SpeedScale;

        /// <summary>Human-readable justification (an LLM fills this) - shown in the AI window.</summary>
        public string Reason;

        public static AIAgentCommand Idle => new AIAgentCommand { Goal = AIGoal.Idle, SpeedScale = 0f, Reason = "idle" };

        public static AIAgentCommand MoveTo(Vector3 position, float speed = 1f, string reason = "move")
            => new AIAgentCommand { Goal = AIGoal.MoveTo, Position = position, SpeedScale = Mathf.Clamp01(speed), Reason = reason };

        public static AIAgentCommand FollowTarget(Transform target, float speed = 1f, string reason = "follow")
            => new AIAgentCommand { Goal = AIGoal.Follow, Target = target, SpeedScale = Mathf.Clamp01(speed), Reason = reason };
    }

    /// <summary>Everything a brain needs to decide the next command, gathered by the provider each tick.</summary>
    public struct AIContext
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Transform Player;
        public float DistanceToPlayer;
        public bool ReachedGoal;
        public float TimeSinceCommand;
    }

    /// <summary>
    /// Decides an agent's goals. Implementations range from a deterministic rule set
    /// (<c>ScriptedAIBrain</c>) to a language model (<c>LLMAIBrain</c>). The provider calls
    /// <see cref="Tick"/> every frame; the brain updates <see cref="Command"/> whenever it decides to
    /// - immediately for a rule set, or when an async request returns for an LLM - so slow brains
    /// never stall the frame.
    /// </summary>
    public interface IAIBrain
    {
        void Tick(in AIContext context);
        AIAgentCommand Command { get; }

        /// <summary>Short status for the AI window ("patrolling to WP2", "thinking", "following player").</summary>
        string Status { get; }
    }
}
