using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Turns an <see cref="IAIBrain"/>'s high-level command into locomotion intent, through the same
    /// <see cref="ILocomotionProvider"/> the player's input uses. The brain decides *what* (go here,
    /// follow that, flee); this decides *how fast and which way* and hands it to the matcher. Because
    /// the two are separate, the brain can be a rule set today and a language model tomorrow with no
    /// change here, and the NPC always runs the identical motion matching stack as the player.
    ///
    /// Add an <see cref="IAIBrain"/> component (e.g. ScriptedAIBrain or LLMAIBrain) beside this; if
    /// none is present it idles. The current command and status are exposed for the AI window.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/AI Command Provider")]
    [RequireComponent(typeof(MotionMatchingController))]
    public sealed class AICommandProvider : MonoBehaviour, ILocomotionProvider
    {
        #region Public

        [SerializeField, Min(0f)] private float _maxSpeed = 3.5f;

        [Tooltip("Stop when closer than this to a MoveTo/Follow goal (meters).")]
        [SerializeField, Min(0.1f)] private float _arriveDistance = 1f;

        [Tooltip("Ease speed down within this distance of the goal (meters).")]
        [SerializeField, Min(0.5f)] private float _slowRadius = 2.5f;

        [Tooltip("Flee/Follow: desired standoff distance from the target (meters).")]
        [SerializeField, Min(0.5f)] private float _standoff = 2f;

        public Vector3 DesiredVelocity { get; private set; }
        public Vector3 DesiredFacing => Vector3.zero; // face travel

        /// <summary>The command currently being executed - a manual override if one is active, else the brain's.</summary>
        public AIAgentCommand Command =>
            OverrideActive ? _override : _brain != null ? _brain.Command : AIAgentCommand.Idle;

        /// <summary>Brain status line for the AI window, or "no brain" when none is attached.</summary>
        public string Status =>
            OverrideActive ? $"manual: {_override.Reason} ({_overrideUntil - Time.time:F0}s)" :
            _brain != null ? _brain.Status : "no brain (idle)";

        public bool ReachedGoal { get; private set; }

        /// <summary>True while a hand-issued command from the AI window is holding control.</summary>
        public bool OverrideActive => Time.time < _overrideUntil;

        /// <summary>
        /// Hand a one-off command to this agent for <paramref name="seconds"/>, from the AI window.
        /// The brain keeps running underneath and takes over again when the override lapses - a nudge,
        /// not a rewire.
        /// </summary>
        public void OverrideCommand(AIAgentCommand command, float seconds = 6f)
        {
            _override = command;
            _overrideUntil = Time.time + Mathf.Max(0.1f, seconds);
        }

        #endregion

        #region Private and Protected

        private IAIBrain _brain;
        private Transform _player;
        private float _commandStart;
        private AIGoal _lastGoal;
        private AIAgentCommand _override;
        private float _overrideUntil;

        #endregion

        #region Unity API

        private void Awake()
        {
            _brain = GetComponent<IAIBrain>();
            // A player reference lets Follow/Flee brains reason about the protagonist without wiring.
            var player = GetPlayer();
            if (player != null) _player = player.transform;
        }

        private void Update()
        {
            var context = new AIContext
            {
                Position = transform.position,
                Velocity = DesiredVelocity,
                Player = _player,
                DistanceToPlayer = _player != null ? Flat(_player.position - transform.position).magnitude : Mathf.Infinity,
                ReachedGoal = ReachedGoal,
                TimeSinceCommand = Time.time - _commandStart
            };
            _brain?.Tick(context);

            AIAgentCommand command = Command;
            if (command.Goal != _lastGoal) { _lastGoal = command.Goal; _commandStart = Time.time; }

            DesiredVelocity = Resolve(command);
        }

        #endregion

        #region Tools and Utilities

        private Vector3 Resolve(AIAgentCommand command)
        {
            float speed = _maxSpeed * Mathf.Clamp01(command.SpeedScale <= 0f ? 1f : command.SpeedScale);

            switch (command.Goal)
            {
                case AIGoal.MoveTo:
                case AIGoal.Patrol:
                case AIGoal.Wander:
                    return SeekTo(command.Position, speed, _arriveDistance);

                case AIGoal.Follow:
                    if (command.Target == null) { ReachedGoal = true; return Vector3.zero; }
                    return SeekTo(command.Target.position, speed, _standoff);

                case AIGoal.Flee:
                    if (command.Target == null) { ReachedGoal = true; return Vector3.zero; }
                    Vector3 away = Flat(transform.position - command.Target.position);
                    ReachedGoal = away.magnitude > _standoff * 3f;
                    return away.sqrMagnitude > 1e-4f ? away.normalized * speed : Vector3.zero;

                default:
                    ReachedGoal = true;
                    return Vector3.zero;
            }
        }

        private Vector3 SeekTo(Vector3 target, float speed, float stopDistance)
        {
            Vector3 toGoal = Flat(target - transform.position);
            float distance = toGoal.magnitude;

            if (distance <= stopDistance) { ReachedGoal = true; return Vector3.zero; }
            ReachedGoal = false;

            float eased = speed * Mathf.Clamp01((distance - stopDistance) / _slowRadius);
            return toGoal / Mathf.Max(distance, 1e-4f) * eased;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Finds the player: a MotionMatchingController that is NOT itself AI-driven.</summary>
        private static GameObject GetPlayer()
        {
            foreach (var controller in FindObjectsByType<MotionMatchingController>(FindObjectsSortMode.None))
                if (controller.GetComponent<AICommandProvider>() == null)
                    return controller.gameObject;
            return null;
        }

        #endregion
    }
}
