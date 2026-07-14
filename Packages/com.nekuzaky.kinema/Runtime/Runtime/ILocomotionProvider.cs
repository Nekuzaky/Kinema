using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Decouples the matching controller from where locomotion intent comes from. Implement this on
    /// any component (player input, AI agent, path follower, network proxy) and the controller will
    /// drive the trajectory from it. Keeping intent behind an interface is what lets the same
    /// controller serve players and NPCs without change.
    /// </summary>
    public interface ILocomotionProvider
    {
        /// <summary>Desired horizontal velocity in world space, meters per second.</summary>
        Vector3 DesiredVelocity { get; }

        /// <summary>
        /// Desired horizontal facing in world space. Return <see cref="Vector3.zero"/> to let the
        /// controller fall back to the direction of travel.
        /// </summary>
        Vector3 DesiredFacing { get; }
    }
}
