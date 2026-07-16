using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// A horizontal ground-plane frame of reference: an origin plus a facing direction.
    /// All matching happens in this space so that pose and trajectory features are invariant
    /// to where the character stands and which way world-north is. Used identically at bake
    /// time (to express baked features) and at runtime (to express the query).
    /// </summary>
    public readonly struct CharacterSpace
    {
        #region Public

        public readonly Vector3 Origin;
        public readonly Vector3 Forward;
        public readonly Vector3 Right;

        /// <summary>
        /// The character's own altitude. <see cref="Origin"/> is flattened to y = 0 because every
        /// trajectory feature is built from the horizontal projection; this keeps the piece that
        /// projection throws away, so bone heights can be measured from the character's feet rather
        /// than from world zero.
        /// </summary>
        public readonly float GroundY;

        public CharacterSpace(Vector3 origin, Vector3 forward)
        {
            Origin = new Vector3(origin.x, 0f, origin.z);
            GroundY = origin.y;
            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            Forward = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            Right = new Vector3(Forward.z, 0f, -Forward.x);
        }

        public static CharacterSpace FromTransform(Transform t)
        {
            return new CharacterSpace(t.position, t.forward);
        }

        #endregion

        #region Main API

        /// <summary>World point -> local ground-plane offset (x = right, y = forward).</summary>
        public Vector2 ToLocalPoint(Vector3 worldPoint)
        {
            Vector3 delta = worldPoint - Origin;
            return new Vector2(Vector3.Dot(delta, Right), Vector3.Dot(delta, Forward));
        }

        /// <summary>World direction -> local ground-plane direction (x = right, y = forward).</summary>
        public Vector2 ToLocalDirection(Vector3 worldDirection)
        {
            return new Vector2(Vector3.Dot(worldDirection, Right), Vector3.Dot(worldDirection, Forward));
        }

        /// <summary>Local ground-plane offset -> world point.</summary>
        public Vector3 ToWorldPoint(Vector2 localPoint)
        {
            return Origin + Right * localPoint.x + Forward * localPoint.y;
        }

        /// <summary>Local ground-plane direction -> world direction.</summary>
        public Vector3 ToWorldDirection(Vector2 localDirection)
        {
            return Right * localDirection.x + Forward * localDirection.y;
        }

        /// <summary>
        /// World point -> full 3D local offset (x = right, y = height above the character's feet,
        /// z = forward). Used for bone positions, where vertical placement (a lifted foot) is
        /// meaningful.
        ///
        /// The height is relative to <see cref="GroundY"/>, not to world zero. Measuring it from
        /// world zero happens to agree at the origin - which is exactly where the baker samples the
        /// rig - and diverges by the terrain's altitude everywhere else, so the query for a
        /// character on a hill at y = 50 would compare a foot at 50.1 against a baked 0.1 and the
        /// search would rank candidates by how high the ground is.
        /// </summary>
        public Vector3 ToLocalOffset3D(Vector3 worldPoint)
        {
            Vector3 delta = worldPoint - Origin;
            return new Vector3(Vector3.Dot(delta, Right), worldPoint.y - GroundY, Vector3.Dot(delta, Forward));
        }

        /// <summary>World vector -> full 3D local vector (x = right, y = up, z = forward). Used for bone velocities.</summary>
        public Vector3 ToLocalVector3D(Vector3 worldVector)
        {
            return new Vector3(Vector3.Dot(worldVector, Right), worldVector.y, Vector3.Dot(worldVector, Forward));
        }

        /// <summary>Full 3D local offset (x = right, y = height above the character's feet,
        /// z = forward) -> world point. Inverse of <see cref="ToLocalOffset3D"/>.</summary>
        public Vector3 ToWorldOffset3D(Vector3 localOffset)
        {
            return Origin + Vector3.up * GroundY
                   + Right * localOffset.x + Vector3.up * localOffset.y + Forward * localOffset.z;
        }

        #endregion
    }
}
