using System;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// A single point of a locomotion trajectory, expressed on the horizontal ground plane
    /// in character space (origin = root, +Z = root facing). Both the desired trajectory
    /// (built from input) and the baked candidate trajectories use this type, which keeps
    /// the query, the database and the debug visualization speaking the same language.
    /// </summary>
    [Serializable]
    public struct TrajectorySample
    {
        #region Public

        /// <summary>Ground-plane offset from the character root, in meters (x = right, y = forward).</summary>
        public Vector2 Position;

        /// <summary>Ground-plane facing direction, normalized (x = right, y = forward).</summary>
        public Vector2 Direction;

        public TrajectorySample(Vector2 position, Vector2 direction)
        {
            Position = position;
            Direction = direction;
        }

        #endregion

        #region Tools and Utilities

        /// <summary>Lifts the 2D ground-plane position back into a local 3D offset (y = 0).</summary>
        public Vector3 LocalPosition3D => new Vector3(Position.x, 0f, Position.y);

        /// <summary>Lifts the 2D facing direction back into a local 3D direction (y = 0).</summary>
        public Vector3 LocalDirection3D => new Vector3(Direction.x, 0f, Direction.y);

        #endregion
    }
}
