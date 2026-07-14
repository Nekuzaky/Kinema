namespace Kinema.MotionMatching
{
    /// <summary>
    /// Logical groups a feature dimension can belong to. Weighting, normalization and
    /// the debug cost breakdown all operate at this granularity, so the enum is the
    /// single source of truth for "what kind of value lives at dimension N".
    /// </summary>
    public enum FeatureGroup
    {
        TrajectoryPosition = 0,
        TrajectoryDirection = 1,
        BonePosition = 2,
        BoneVelocity = 3,
        RootVelocity = 4
    }

    public static class FeatureGroupExtensions
    {
        public const int Count = 5;

        public static string ToDisplayName(this FeatureGroup group)
        {
            switch (group)
            {
                case FeatureGroup.TrajectoryPosition: return "Trajectory Position";
                case FeatureGroup.TrajectoryDirection: return "Trajectory Direction";
                case FeatureGroup.BonePosition: return "Bone Position";
                case FeatureGroup.BoneVelocity: return "Bone Velocity";
                case FeatureGroup.RootVelocity: return "Root Velocity";
                default: return group.ToString();
            }
        }
    }
}
