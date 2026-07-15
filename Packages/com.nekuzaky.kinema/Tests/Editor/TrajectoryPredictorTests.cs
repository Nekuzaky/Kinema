using NUnit.Framework;
using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// The spring predictor is closed-form math; these tests hold it against a numerically
    /// integrated critically damped spring, and pin the property that motivated it - a C1 start,
    /// where velocity barely changes in the first instants instead of snapping.
    /// </summary>
    public sealed class TrajectoryPredictorTests
    {
        private static TrajectorySample[] PredictOne(PredictionModel model, float time, Vector3 currentVel, Vector3 desiredVel)
        {
            var space = new CharacterSpace(Vector3.zero, Vector3.forward);
            var settings = TrajectoryPredictionSettings.Default;
            settings.Model = model;
            var buffer = new TrajectorySample[1];
            TrajectoryPredictor.Predict(space, currentVel, desiredVel, Vector3.zero,
                new[] { time }, settings, history: null, now: 0f, buffer);
            return buffer;
        }

        [Test]
        public void CriticalSpring_MatchesNumericalIntegration()
        {
            var current = new Vector3(0f, 0f, 3f);
            var desired = new Vector3(2f, 0f, 0f);
            float tau = TrajectoryPredictionSettings.Default.PositionResponse;
            float omega = 2f / tau;

            foreach (float t in new[] { 0.2f, 0.4f, 1.0f })
            {
                // Integrate v(s) = desired + (current - desired)(1 + ws)e^{-ws} step by step.
                Vector3 position = Vector3.zero;
                const int steps = 4000;
                float h = t / steps;
                for (int i = 0; i < steps; i++)
                {
                    float s = (i + 0.5f) * h;
                    Vector3 velocity = desired + (current - desired) * ((1f + omega * s) * Mathf.Exp(-omega * s));
                    position += velocity * h;
                }

                TrajectorySample sample = PredictOne(PredictionModel.CriticalSpring, t, current, desired)[0];
                Assert.That(sample.Position.x, Is.EqualTo(position.x).Within(2e-3f), $"x at t={t}");
                Assert.That(sample.Position.y, Is.EqualTo(position.z).Within(2e-3f), $"forward at t={t}");
            }
        }

        [Test]
        public void CriticalSpring_StartsWithCurrentVelocity()
        {
            // C1 start: over a tiny horizon the trajectory must follow the CURRENT velocity almost
            // exactly - the spring has not begun to bend it yet. The lag bends immediately.
            var current = new Vector3(0f, 0f, 3f);
            var desired = new Vector3(3f, 0f, 0f);   // hard 90 degree turn
            const float t = 0.02f;

            TrajectorySample spring = PredictOne(PredictionModel.CriticalSpring, t, current, desired)[0];
            TrajectorySample lag = PredictOne(PredictionModel.ExponentialLag, t, current, desired)[0];

            Vector2 ballistic = new Vector2(0f, current.z * t);
            float springError = (spring.Position - ballistic).magnitude;
            float lagError = (lag.Position - ballistic).magnitude;

            Assert.Less(springError, lagError * 0.1f, "the spring start should hug the current velocity an order of magnitude tighter than the lag");
        }

        [Test]
        public void BothModels_ConvergeToDesiredVelocity()
        {
            var current = new Vector3(0f, 0f, 1f);
            var desired = new Vector3(0f, 0f, 4f);

            foreach (PredictionModel model in new[] { PredictionModel.CriticalSpring, PredictionModel.ExponentialLag })
            {
                TrajectorySample near = PredictOne(model, 2.0f, current, desired)[0];
                TrajectorySample far = PredictOne(model, 2.5f, current, desired)[0];
                float speed = (far.Position.y - near.Position.y) / 0.5f;
                Assert.That(speed, Is.EqualTo(4f).Within(0.05f), $"{model} should travel at the desired speed far out");
            }
        }
    }
}
