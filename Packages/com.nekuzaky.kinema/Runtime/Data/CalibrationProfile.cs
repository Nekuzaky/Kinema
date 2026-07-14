using System;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// A named weight preset (combat, exploration, injured...). Authored on the config, baked into
    /// the database, applied at runtime with <see cref="MotionMatchingController.SetCalibrationProfile"/>.
    /// Switching a profile only rebuilds the per-dimension weight table - no rebake needed.
    /// </summary>
    [Serializable]
    public sealed class CalibrationProfile
    {
        public string Name = "Default";
        public FeatureWeights Weights = FeatureWeights.Default;
    }
}
