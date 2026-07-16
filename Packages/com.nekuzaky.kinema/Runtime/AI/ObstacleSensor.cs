using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>What the ground ahead affords. Only kinds the character can actually act on exist.</summary>
    public enum ObstacleKind
    {
        /// <summary>Clear floor ahead.</summary>
        None,

        /// <summary>Low enough to walk over without acting - a kerb, a plank.</summary>
        Step,

        /// <summary>Waist-high, thin, with floor to land on: a vault clears it.</summary>
        Vault,

        /// <summary>The floor runs out but resumes within a jump's reach.</summary>
        Gap,

        /// <summary>Nothing in the data gets past this - a tall wall, or a drop too wide to jump.</summary>
        Blocked
    }

    /// <summary>One sensing result. A value type: read it, do not hold it.</summary>
    public struct ObstacleReading
    {
        public ObstacleKind Kind;

        /// <summary>Metres to the obstacle face, or to the near edge of a gap.</summary>
        public float Distance;

        /// <summary>Obstacle top above the character's feet. Zero for a gap.</summary>
        public float Height;

        /// <summary>Obstacle thickness, or gap width. Zero when not measured.</summary>
        public float Depth;

        public Vector3 Point;
        public Vector3 Normal;

        /// <summary>
        /// What was hit, when something was. Null for a Gap - a hole has nothing in it - and that is
        /// the point of carrying it: a reading that can only give coordinates makes every log about
        /// it read "over (2.00, 0.80, 8.51)", which says where but never what.
        /// </summary>
        public Collider Collider;

        /// <summary>True when there is floor on the far side to arrive on.</summary>
        public bool LandingClear;
    }

    /// <summary>
    /// Classifies what is ahead so a character can pick an action rather than guess: step over it,
    /// vault it, jump the gap, or accept that nothing in the data gets past it.
    ///
    /// One sensor per character, read by everyone. Before this each consumer cast its own rays every
    /// frame - the vault trigger one, the AI provider four - and none of them agreed. Sensing once on
    /// a timer and sharing the answer is both cheaper and the only way two consumers can act on the
    /// same picture of the world.
    ///
    /// <b>Cost.</b> Five casts per sense at <see cref="_senseInterval"/> (10 Hz by default, the same
    /// cadence as the matcher's search), and none at all while the character is not moving - a still
    /// character's surroundings are not changing. That is ~50 casts/second/character against the
    /// ~60-240 the per-frame probes it replaces were doing.
    ///
    /// <b>What it will not tell you.</b> There is no Climb and no Cover kind. Both are trivial to
    /// sense - a tall wall is the easiest thing here to recognise - and neither is here, because
    /// motion matching plays the data it has and there is no climb or cover motion in a locomotion
    /// capture. A sensor that reports an affordance the character cannot perform is a lie with extra
    /// steps. When those clips exist, they slot in as new kinds and nothing else changes.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Obstacle Sensor")]
    [DisallowMultipleComponent]
    public sealed class ObstacleSensor : MonoBehaviour
    {
        #region Public

        [Tooltip("How far ahead to look (meters).")]
        [SerializeField, Min(0.5f)] private float _range = 2f;

        [Tooltip("Obstacles up to this high are walked over, no action needed (meters).")]
        [SerializeField, Min(0f)] private float _stepHeight = 0.3f;

        [Tooltip("Obstacles up to this high can be vaulted, if thin enough and there is somewhere to land (meters).")]
        [SerializeField, Min(0.1f)] private float _vaultHeight = 1.15f;

        [Tooltip("Thicker than this and a vault would land on top of it, not past it (meters).")]
        [SerializeField, Min(0.1f)] private float _vaultDepth = 1.2f;

        [Tooltip("Widest gap a jump can clear (meters). Beyond it the edge reads as Blocked.")]
        [SerializeField, Min(0.5f)] private float _maxGapWidth = 3f;

        [Tooltip("A drop deeper than this is a gap, not a step down (meters).")]
        [SerializeField, Min(0.2f)] private float _ledgeDrop = 1.5f;

        [Tooltip("Seconds between sensings. The world does not change faster than the search reacts to it.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _senseInterval = 0.1f;

        [Tooltip("Skip sensing below this speed - a still character's surroundings are not changing.")]
        [SerializeField, Min(0f)] private float _idleSpeed = 0.15f;

        [SerializeField] private LayerMask _layers = ~0;

        /// <summary>The latest reading. Refreshed on the sense timer, not per frame.</summary>
        public ObstacleReading Reading => _reading;

        /// <summary>Sense right now, ignoring the timer. For the frame an action must be decided on.</summary>
        public ObstacleReading SenseNow()
        {
            _reading = Sense(Heading());
            _nextSense = Time.time + _senseInterval;
            return _reading;
        }

        #endregion

        #region Private and Protected

        /// <summary>Chest height: above ground undulation, below the head, and where a vault contacts.</summary>
        private const float ProbeHeight = 0.6f;

        private ObstacleReading _reading;
        private float _nextSense;
        private ILocomotionProvider _provider;

        #endregion

        #region Unity API

        private void Awake() => _provider = GetComponent<ILocomotionProvider>();

        private void Update()
        {
            // A character going nowhere is not about to walk into anything, and its surroundings are
            // not changing. On a crowd this is most of the saving, since most agents are idle at any
            // moment. Anyone needing an answer while standing still calls SenseNow.
            Vector3 intent = _provider != null ? _provider.DesiredVelocity : Vector3.zero;
            intent.y = 0f;
            if (intent.magnitude < _idleSpeed)
            {
                _reading = default;
                return;
            }

            if (Time.time < _nextSense) return;
            _nextSense = Time.time + _senseInterval;

            ObstacleKind was = _reading.Kind;
            _reading = Sense(Heading());

            // The change, not the reading: this runs 10x a second, and "still None" 10x a second is
            // the noise that hides the one Blocked that explains why an agent stopped.
            if (KinemaLog.Verbose && _reading.Kind != was)
                KinemaLog.Event($"{name}: sees {was} -> {_reading.Kind}" +
                                (_reading.Kind == ObstacleKind.None
                                    ? ""
                                    : $" '{(_reading.Collider != null ? _reading.Collider.name : "gap")}' " +
                                      $"at {_reading.Distance:F2}m, top {_reading.Height:F2}m, " +
                                      $"{_reading.Depth:F2}m deep, landing {(_reading.LandingClear ? "clear" : "BLOCKED")}"),
                    this);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.position + Vector3.up * ProbeHeight;
            Gizmos.color = _reading.Kind switch
            {
                ObstacleKind.Vault => Color.cyan,
                ObstacleKind.Gap => Color.yellow,
                ObstacleKind.Blocked => Color.red,
                ObstacleKind.Step => Color.green,
                _ => Color.grey
            };
            Gizmos.DrawLine(origin, origin + Heading() * _range);
            if (_reading.Kind != ObstacleKind.None) Gizmos.DrawWireSphere(_reading.Point, 0.12f);
        }

        #endregion

        #region Main API

        /// <summary>
        /// Casts: one forward, then either the vault pair (far side floor, thickness) or the gap
        /// walk. Ordered so the common case - clear floor ahead - costs two.
        /// </summary>
        private ObstacleReading Sense(Vector3 heading)
        {
            Vector3 origin = transform.position + Vector3.up * ProbeHeight;
            var reading = new ObstacleReading { Kind = ObstacleKind.None };

            if (Physics.Raycast(origin, heading, out RaycastHit hit, _range, _layers,
                    QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform))
            {
                reading.Distance = hit.distance;
                reading.Point = hit.point;
                reading.Normal = hit.normal;
                reading.Collider = hit.collider;
                reading.Height = hit.collider.bounds.max.y - transform.position.y;

                if (reading.Height <= _stepHeight)
                {
                    reading.Kind = ObstacleKind.Step;
                    return reading;
                }

                // A walkable face is a ramp, not an obstacle: judging it by height would call a ramp
                // a wall, since its collider reaches to the top of the slope.
                if (Vector3.Angle(hit.normal, Vector3.up) <= 45f)
                {
                    reading.Kind = ObstacleKind.None;
                    return reading;
                }

                if (reading.Height > _vaultHeight)
                {
                    // Where a Climb would go, once there is a climb clip to play.
                    reading.Kind = ObstacleKind.Blocked;
                    return reading;
                }

                // Thickness, measured by looking back at it from beyond: a block deep enough to stand
                // on is not something you vault, it is something you climb onto, and the vault would
                // warp the character into the middle of it.
                reading.Depth = MeasureDepth(origin, heading, hit.distance);
                float far = hit.distance + reading.Depth;
                reading.LandingClear = HasFloor(heading, far + 0.4f, reading.Height + _ledgeDrop);

                reading.Kind = reading.Depth <= _vaultDepth && reading.LandingClear
                    ? ObstacleKind.Vault
                    : ObstacleKind.Blocked;
                return reading;
            }

            // Nothing at chest height. The floor is the only thing left that can stop us, and a
            // horizontal ray can never see it missing.
            return SenseFloor(heading, reading);
        }

        #endregion

        #region Tools and Utilities

        /// <summary>Thickness of what was just hit, by casting back through it from the far side.</summary>
        private float MeasureDepth(Vector3 origin, Vector3 heading, float frontDistance)
        {
            Vector3 behind = origin + heading * (frontDistance + _vaultDepth + 0.5f);
            return Physics.Raycast(behind, -heading, out RaycastHit back, _vaultDepth + 0.5f, _layers,
                       QueryTriggerInteraction.Ignore)
                ? Mathf.Max(0f, _vaultDepth + 0.5f - back.distance)
                : _vaultDepth + 0.5f; // no far face within reach: too deep to vault.
        }

        /// <summary>
        /// Walks the floor forward in strides. The first missing sample is the near edge; sampling
        /// on until it returns gives the width, which is what decides jump versus Blocked.
        /// </summary>
        private ObstacleReading SenseFloor(Vector3 heading, ObstacleReading reading)
        {
            const float Stride = 0.5f;

            float edge = -1f;
            for (float d = Stride; d <= _range; d += Stride)
            {
                if (HasFloor(heading, d, _ledgeDrop)) continue;
                edge = d;
                break;
            }

            if (edge < 0f) return reading; // floor all the way: None.

            reading.Distance = edge;
            reading.Point = transform.position + heading * edge;

            for (float d = edge + Stride; d <= edge + _maxGapWidth; d += Stride)
            {
                if (!HasFloor(heading, d, _ledgeDrop)) continue;
                reading.Kind = ObstacleKind.Gap;
                reading.Depth = d - edge;
                reading.LandingClear = true;
                return reading;
            }

            reading.Kind = ObstacleKind.Blocked; // a drop with no far side within a jump.
            return reading;
        }

        private bool HasFloor(Vector3 heading, float distance, float depth)
        {
            Vector3 probe = transform.position + Vector3.up * 0.1f + heading * distance;
            return Physics.Raycast(probe, Vector3.down, depth + 0.1f, _layers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Where the character intends to go, falling back to where it faces.</summary>
        private Vector3 Heading()
        {
            Vector3 v = _provider != null ? _provider.DesiredVelocity : Vector3.zero;
            v.y = 0f;
            if (v.magnitude > _idleSpeed) return v.normalized;

            Vector3 f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        }

        #endregion
    }
}
