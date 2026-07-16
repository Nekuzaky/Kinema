using System;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// The runtime side of Learned Motion Matching (Holden et al., SIGGRAPH 2020): three small
    /// networks that stand in for the feature database and the linear search, so a shipping game
    /// carries a few megabytes of weights instead of gigabytes of mocap features.
    ///
    /// The flow the controller would drive, once a backend implements this:
    /// <list type="number">
    ///   <item><b>Project</b> a query (predicted trajectory + current pose) to the nearest point in a
    ///   learned latent space - this replaces the nearest-neighbour <see cref="MotionMatcher"/>.</item>
    ///   <item><b>Step</b> the latent forward one frame most of the time, so a full projection is
    ///   only needed when the intent changes - this is what makes it cheaper than searching every
    ///   frame.</item>
    ///   <item><b>Decompress</b> the latent back to the full pose feature vector the animation graph
    ///   plays.</item>
    /// </list>
    ///
    /// This is the seam. It is deliberately backend-agnostic: a Unity Sentis implementation running
    /// exported ONNX weights is the intended one, but nothing here depends on Sentis, so the package
    /// takes no ML dependency until a backend is added. The controller is NOT wired to this yet - the
    /// data export (Tools &gt; Kinema &gt; Learned MM) and this interface are the foundation; the
    /// Sentis backend and the controller path are the next steps.
    ///
    /// All calls are span-based and expected to be allocation-free on the hot path: a real
    /// implementation reuses its input/output tensors.
    /// </summary>
    public interface ILearnedMotionModel : IDisposable
    {
        /// <summary>Dimension of the learned latent code (the compressed motion state).</summary>
        int LatentSize { get; }

        /// <summary>Dimension of the full feature/pose vector - matches the baked schema's dimension.</summary>
        int FeatureSize { get; }

        /// <summary>
        /// Projector: map a normalized query feature vector to the nearest latent code. Replaces the
        /// database search. <paramref name="queryFeatures"/> is <see cref="FeatureSize"/> long,
        /// <paramref name="latentOut"/> is <see cref="LatentSize"/> long.
        /// </summary>
        void Project(ReadOnlySpan<float> queryFeatures, Span<float> latentOut);

        /// <summary>
        /// Stepper: advance a latent code one frame. Called instead of projecting while the intent is
        /// stable, which is most frames. Both spans are <see cref="LatentSize"/> long.
        /// </summary>
        void Step(ReadOnlySpan<float> latent, Span<float> nextLatentOut);

        /// <summary>
        /// Decompressor: reconstruct the full pose feature vector from a latent code, for the graph to
        /// play. <paramref name="latent"/> is <see cref="LatentSize"/> long,
        /// <paramref name="poseFeaturesOut"/> is <see cref="FeatureSize"/> long.
        /// </summary>
        void Decompress(ReadOnlySpan<float> latent, Span<float> poseFeaturesOut);
    }
}
