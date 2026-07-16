namespace Kinema.MotionMatching
{
    /// <summary>
    /// How the pose half of a feature vector is built - which is to say, what "these two poses are
    /// close" is taken to mean.
    /// </summary>
    public enum PoseCostMode
    {
        /// <summary>
        /// Bone positions and bone velocities as separate blocks, summed with per-group weights.
        /// The classic layout, and the one every motion matching talk shows.
        ///
        /// It has a known flaw: it adds positions to velocities, so the weight balancing them is
        /// arbitrary - there is nothing to derive it from, which is why this package ships a weight
        /// tuner to find it by experiment. And it evaluates the two independently, so it cannot see
        /// that a position offset is harmless when the velocity offset is set to absorb it.
        /// </summary>
        Naive,

        /// <summary>
        /// One composite per bone, <c>2*pos/y + vel/y^2</c>, replacing both blocks: the displacement
        /// an inertialized transition onto that bone would actually cause (Holden, "Inertialization
        /// Transition Cost", 2022).
        ///
        /// Three consequences, in order of how much they matter:
        /// <list type="bullet">
        /// <item>The cost measures what the transition will look like rather than a proxy for it. A
        /// positive position offset paired with the right negative velocity offset is cheap, because
        /// the spring absorbs it - which is true, and the naive sum cannot express it.</item>
        /// <item>There is no velocity weight left to tune. It falls out of the half-life.</item>
        /// <item>The vector loses 3*B dimensions, off every distance evaluation and off the Learned
        /// Motion Matching training set.</item>
        /// </list>
        ///
        /// It assumes the transition really is inertialized, and that
        /// <see cref="FeatureSchema.InertializationHalflife"/> is the half-life it runs at. Under a
        /// fixed crossfade the number still behaves like a sensible blend of position and velocity,
        /// but it stops being a measurement of anything.
        /// </summary>
        InertializationCost
    }
}
