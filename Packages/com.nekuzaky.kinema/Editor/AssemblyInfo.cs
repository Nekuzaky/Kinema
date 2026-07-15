using System.Runtime.CompilerServices;

// The EditMode suite exercises bake internals (idle pruning) directly:
// building a rig-driven end-to-end bake inside a test is not worth the cost.
[assembly: InternalsVisibleTo("Kinema.MotionMatching.Tests.Editor")]
