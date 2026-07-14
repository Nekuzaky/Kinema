using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Locks grounded feet in place using the contact flags baked into the database. Runs in
    /// LateUpdate (after the PlayableGraph has written the pose) and solves an analytic two-bone IK
    /// per leg toward the world position captured when the contact started; the correction blends
    /// out smoothly on release. Kills the residual foot sliding that trajectory matching alone
    /// cannot remove.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Foot Lock IK")]
    [RequireComponent(typeof(MotionMatchingController))]
    public sealed class FootLockIK : MonoBehaviour
    {
        #region Public

        [SerializeField, Range(0f, 1f)] private float _weight = 1f;

        [Tooltip("Seconds to blend the correction out after the foot lifts.")]
        [SerializeField, Range(0.02f, 0.4f)] private float _releaseTime = 0.12f;

        [Tooltip("Unlock if the locked point drifts further than this from the animated foot (meters).")]
        [SerializeField, Range(0.05f, 0.6f)] private float _breakDistance = 0.25f;

        public float Weight { get => _weight; set => _weight = Mathf.Clamp01(value); }

        #endregion

        #region Private and Protected

        private struct Leg
        {
            public Transform Upper, Lower, Foot;
            public bool Locked;
            public Vector3 LockPosition;
            public float Blend;      // 1 = fully corrected
            public bool Valid;
        }

        private MotionMatchingController _controller;
        private Leg[] _legs;
        private bool _resolved;

        #endregion

        #region Unity API

        private void Awake()
        {
            _controller = GetComponent<MotionMatchingController>();
        }

        private void LateUpdate()
        {
            if (_weight <= 0f || _controller == null || !_controller.IsInitialized) return;

            MotionMatchingDatabase db = _controller.Database;
            if (db == null || !db.HasContacts) return;
            if (!_resolved) Resolve(db);

            byte contacts = db.GetContacts(_controller.CurrentFrame);
            float dt = Time.deltaTime;

            for (int i = 0; i < _legs.Length; i++)
            {
                if (!_legs[i].Valid) continue;
                bool grounded = (contacts & (1 << i)) != 0;
                UpdateLeg(ref _legs[i], grounded, dt);
            }
        }

        #endregion

        #region Tools and Utilities

        /// <summary>Finds upper/lower/foot chains from the database's contact bone names.</summary>
        private void Resolve(MotionMatchingDatabase db)
        {
            _resolved = true;
            _legs = new Leg[db.ContactBoneCount];

            for (int i = 0; i < _legs.Length; i++)
            {
                string footName = db.GetContactBoneName(i);
                Transform foot = FindDeep(transform, footName);
                if (foot == null || foot.parent == null || foot.parent.parent == null)
                {
                    _legs[i].Valid = false;
                    continue;
                }
                _legs[i] = new Leg
                {
                    Foot = foot,
                    Lower = foot.parent,
                    Upper = foot.parent.parent,
                    Valid = true
                };
            }
        }

        private void UpdateLeg(ref Leg leg, bool grounded, float dt)
        {
            Vector3 animatedFoot = leg.Foot.position;

            if (grounded)
            {
                if (!leg.Locked)
                {
                    leg.Locked = true;
                    leg.LockPosition = animatedFoot;
                }
                // Keep the lock on the ground plane where the contact started.
                if ((leg.LockPosition - animatedFoot).magnitude > _breakDistance)
                {
                    leg.Locked = false; // too far: let it go rather than stretch the leg.
                }
                leg.Blend = leg.Locked ? Mathf.MoveTowards(leg.Blend, 1f, dt / Mathf.Max(_releaseTime, 1e-3f)) : leg.Blend;
            }
            else
            {
                leg.Locked = false;
            }

            if (!leg.Locked)
                leg.Blend = Mathf.MoveTowards(leg.Blend, 0f, dt / Mathf.Max(_releaseTime, 1e-3f));

            float w = leg.Blend * _weight;
            if (w <= 1e-4f) return;

            Vector3 target = Vector3.Lerp(animatedFoot, leg.LockPosition, w);
            SolveTwoBone(leg.Upper, leg.Lower, leg.Foot, target);
        }

        /// <summary>Analytic two-bone IK: aims the chain at the target, then sets the knee angle by law of cosines.</summary>
        private static void SolveTwoBone(Transform upper, Transform lower, Transform foot, Vector3 target)
        {
            Vector3 a = upper.position;
            Vector3 b = lower.position;
            Vector3 c = foot.position;

            float lab = (b - a).magnitude;
            float lcb = (c - b).magnitude;
            float lat = Mathf.Clamp((target - a).magnitude, 0.01f, lab + lcb - 1e-3f);

            // Current and desired interior angles.
            float acAb = AngleBetween(c - a, b - a);
            float baBc = AngleBetween(a - b, c - b);
            float acAt = AngleBetween(c - a, target - a);

            float acAbDesired = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float baBcDesired = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

            Vector3 axis = Vector3.Cross(c - a, b - a);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(c - a, Vector3.up);
            axis.Normalize();

            // Bend the knee to reach the correct chain length.
            upper.rotation = Quaternion.AngleAxis((acAbDesired - acAb) * Mathf.Rad2Deg, axis) * upper.rotation;
            lower.rotation = Quaternion.AngleAxis((baBcDesired - baBc) * Mathf.Rad2Deg, axis) * lower.rotation;

            // Swing the whole chain so the foot lands on the target.
            Vector3 axis2 = Vector3.Cross(foot.position - a, target - a);
            if (axis2.sqrMagnitude > 1e-8f)
            {
                float swing = AngleBetween(foot.position - a, target - a);
                upper.rotation = Quaternion.AngleAxis(swing * Mathf.Rad2Deg, axis2.normalized) * upper.rotation;
            }
        }

        private static float AngleBetween(Vector3 u, Vector3 v)
        {
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(u.normalized, v.normalized), -1f, 1f));
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        #endregion
    }
}
