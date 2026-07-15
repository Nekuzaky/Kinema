using System;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Tuning for how eagerly the predicted trajectory reacts to a change of intent. Higher
    /// response = the prediction snaps to the desired velocity/facing sooner (more responsive,
    /// less smooth); lower = longer, smoother anticipation curves.
    /// </summary>
    /// <summary>How the future trajectory approaches the desired velocity.</summary>
    public enum PredictionModel
    {
        /// <summary>
        /// Critically damped spring: velocity starts changing with zero acceleration and ramps in,
        /// the way a body with mass actually commits to a turn. Smoother queries into and out of
        /// direction changes.
        /// </summary>
        CriticalSpring = 0,

        /// <summary>First-order lag: velocity snaps toward the target immediately. The v1.0 behaviour.</summary>
        ExponentialLag = 1
    }

    [Serializable]
    public struct TrajectoryPredictionSettings
    {
        [Tooltip("How the prediction approaches the desired velocity. Critical spring reads as a body with mass; exponential lag reacts instantly.")]
        public PredictionModel Model;

        [Tooltip("Seconds for the predicted position to catch up to the desired velocity. Lower = snappier.")]
        [Range(0.05f, 1.0f)] public float PositionResponse;

        [Tooltip("Seconds for the predicted facing to catch up to the desired facing. Lower = snappier.")]
        [Range(0.05f, 1.0f)] public float DirectionResponse;

        public static TrajectoryPredictionSettings Default => new TrajectoryPredictionSettings
        {
            Model = PredictionModel.CriticalSpring,
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

                // Future: integral of a velocity approaching desiredVel, either as a first-order lag
                // or as a critically damped spring (zero initial acceleration - the C1 start a real
                // body has). Both are closed-form, so prediction stays allocation- and iteration-free.
                float posBlend, dirBlend;
                if (settings.Model == PredictionModel.CriticalSpring)
                {
                    // Velocity transient (1 + wt)e^{-wt}; its integral is (2 - e^{-wt}(2 + wt)) / w.
                    float wp = 2f / tauPos;
                    posBlend = (2f - Mathf.Exp(-wp * t) * (2f + wp * t)) / wp;

                    // Spring step response for the facing blend, C1 at t = 0.
                    float wd = 2f / tauDir;
                    dirBlend = 1f - Mathf.Exp(-wd * t) * (1f + wd * t);
                }
                else
                {
                    posBlend = tauPos * (1f - Mathf.Exp(-t / tauPos));
                    dirBlend = 1f - Mathf.Exp(-t / tauDir);
                }

                Vector3 worldOffset = desiredVel * t + (currentVel - desiredVel) * posBlend;
                Vector3 worldPoint = space.Origin + worldOffset;
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
