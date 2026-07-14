using System;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Tuning for how eagerly the predicted trajectory reacts to a change of intent. Higher
    /// response = the prediction snaps to the desired velocity/facing sooner (more responsive,
    /// less smooth); lower = longer, smoother anticipation curves.
    /// </summary>
    [Serializable]
    public struct TrajectoryPredictionSettings
    {
        [Tooltip("Seconds for the predicted position to catch up to the desired velocity. Lower = snappier.")]
        [Range(0.05f, 1.0f)] public float PositionResponse;

        [Tooltip("Seconds for the predicted facing to catch up to the desired facing. Lower = snappier.")]
        [Range(0.05f, 1.0f)] public float DirectionResponse;

        public static TrajectoryPredictionSettings Default => new TrajectoryPredictionSettings
        {
            PositionResponse = 0.35f,
            DirectionResponse = 0.25f
        };
    }

    /// <summary>
    /// Builds the desired character-space trajectory from the current and desired locomotion state.
    /// Uses a first-order lag (closed-form exponential approach), which is cheap, unconditionally
    /// stable and produces the smooth "leaning into a turn" curves motion matching feeds on.
    /// </summary>
    public static class TrajectoryPredictor
    {
        #region Main API

        /// <summary>
        /// Fills <paramref name="buffer"/> with one sample per schema trajectory time, in the
        /// character space of <paramref name="space"/>.
        /// </summary>
        /// <param name="currentWorldVelocity">Current horizontal velocity (m/s, world).</param>
        /// <param name="desiredWorldVelocity">Target horizontal velocity (m/s, world).</param>
        /// <param name="desiredWorldFacing">Target horizontal facing (world). Zero falls back to velocity direction.</param>
        /// <param name="history">Recent root transforms used to fill the past (negative) trajectory times. May be null.</param>
        /// <param name="now">Current absolute time (seconds), paired with <paramref name="history"/>.</param>
        public static void Predict(
            CharacterSpace space,
            Vector3 currentWorldVelocity,
            Vector3 desiredWorldVelocity,
            Vector3 desiredWorldFacing,
            float[] trajectoryTimes,
            TrajectoryPredictionSettings settings,
            TrajectoryHistory history,
            float now,
            TrajectorySample[] buffer)
        {
            Vector3 currentVel = Flatten(currentWorldVelocity);
            Vector3 desiredVel = Flatten(desiredWorldVelocity);

            Vector3 currentFacing = space.Forward;
            Vector3 desiredFacing = Flatten(desiredWorldFacing);
            if (desiredFacing.sqrMagnitude < 1e-6f)
                desiredFacing = desiredVel.sqrMagnitude > 1e-6f ? desiredVel.normalized : currentFacing;
            else
                desiredFacing.Normalize();

            float tauPos = Mathf.Max(0.01f, settings.PositionResponse);
            float tauDir = Mathf.Max(0.01f, settings.DirectionResponse);

            int count = Mathf.Min(trajectoryTimes.Length, buffer.Length);
            for (int i = 0; i < count; i++)
            {
                float t = trajectoryTimes[i];

                // Past: replay the recorded root path, expressed in the current character space.
                if (t < 0f)
                {
                    if (history != null && history.HasData)
                    {
                        history.Sample(now + t, out Vector3 pastPos, out Vector3 pastFwd);
                        buffer[i] = new TrajectorySample(space.ToLocalPoint(pastPos), space.ToLocalDirection(pastFwd));
                    }
                    else
                    {
                        buffer[i] = new TrajectorySample(Vector2.zero, new Vector2(0f, 1f));
                    }
                    continue;
                }

                // Future: integral of a velocity that approaches desiredVel with time constant tauPos.
                float posBlend = tauPos * (1f - Mathf.Exp(-t / tauPos));
                Vector3 worldOffset = desiredVel * t + (currentVel - desiredVel) * posBlend;
                Vector3 worldPoint = space.Origin + worldOffset;

                // Facing: exponential approach from current to desired facing.
                float dirBlend = 1f - Mathf.Exp(-t / tauDir);
                Vector3 worldDir = Vector3.Slerp(currentFacing, desiredFacing, dirBlend);

                buffer[i] = new TrajectorySample(
                    space.ToLocalPoint(worldPoint),
                    space.ToLocalDirection(worldDir));
            }
        }

        #endregion

        #region Tools and Utilities

        private static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

        #endregion
    }
}
