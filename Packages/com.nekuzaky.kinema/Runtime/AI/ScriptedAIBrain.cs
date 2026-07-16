using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// A deterministic rule-set brain: patrol a set of waypoints, wander a radius, or follow the
    /// player. No dependencies, no network - the always-available default, and the fallback an
    /// <c>LLMAIBrain</c> drops back to when it has no answer yet. Same <see cref="IAIBrain"/>
    /// contract as any other brain, so the AI window drives it identically.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Scripted AI Brain")]
    public sealed class ScriptedAIBrain : MonoBehaviour, IAIBrain
    {
        #region Public

        public enum Behaviour { Wander, Patrol, FollowPlayer }

        [SerializeField] private Behaviour _behaviour = Behaviour.Wander;

        [Tooltip("Wander radius around the spawn point (Wander).")]
        [SerializeField, Min(1f)] private float _wanderRadius = 8f;

        [Tooltip("Waypoints to patrol in order (Patrol). Empty falls back to Wander.")]
        [SerializeField] private Transform[] _waypoints;

        [Tooltip("Seconds to idle at each goal before moving on.")]
        [SerializeField, Min(0f)] private float _pause = 1.2f;

        [SerializeField, Range(0f, 1f)] private float _speedScale = 0.8f;

        public AIAgentCommand Command { get; private set; } = AIAgentCommand.Idle;
        public string Status { get; private set; } = "idle";

        #endregion

        #region Private and Protected

        private Vector3 _home;
        private int _waypointIndex;
        private float _pauseUntil;
        private bool _started;

        #endregion

        #region Unity API

        private void Start()
        {
            _home = transform.position;
            _started = true;
            Repick(default);
        }

        #endregion

        #region IAIBrain

        public void Tick(in AIContext context)
        {
            if (!_started) return;

            if (_behaviour == Behaviour.FollowPlayer)
            {
                if (context.Player != null)
                {
                    Command = AIAgentCommand.FollowTarget(context.Player, _speedScale, "following the player");
                    Status = $"following ({context.DistanceToPlayer:F0} m)";
                }
                else { Command = AIAgentCommand.Idle; Status = "no player to follow"; }
                return;
            }

            // Wander / Patrol: advance to the next goal once the current one is reached and the pause
            // has elapsed. The pause is what returns the agent to idle instead of pin-balling.
            if (context.ReachedGoal)
            {
                if (_pauseUntil <= 0f) { _pauseUntil = Time.time + _pause; Status = "arrived, pausing"; }
                else if (Time.time >= _pauseUntil) { _pauseUntil = 0f; Repick(context); }
            }
        }

        #endregion

        #region Tools and Utilities

        private void Repick(in AIContext context)
        {
            if (_behaviour == Behaviour.Patrol && _waypoints != null && _waypoints.Length > 0)
            {
                _waypointIndex = (_waypointIndex + 1) % _waypoints.Length;
                Transform wp = _waypoints[_waypointIndex];
                if (wp != null)
                {
                    Command = AIAgentCommand.MoveTo(wp.position, _speedScale, $"patrol to waypoint {_waypointIndex}");
                    Status = $"patrolling to WP{_waypointIndex}";
                    return;
                }
            }

            // Wander (also the patrol fallback when waypoints are missing).
            Vector2 disc = Random.insideUnitCircle * _wanderRadius;
            Vector3 goal = _home + new Vector3(disc.x, 0f, disc.y);
            Command = AIAgentCommand.MoveTo(goal, _speedScale, "wandering");
            Status = "wandering";
        }

        #endregion
    }
}
