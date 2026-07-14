using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// A live snapshot of the last matching decision, updated in place each search to stay
    /// allocation-free. Consumed by the runtime gizmos, the controller inspector and the
    /// editor window's Debug tab. This is the single window into "what is the matcher thinking".
    /// </summary>
    public sealed class MotionMatchingDebugData
    {
        #region Public

        public bool HasData;

        public int SelectedFrame;
        public int SelectedClipIndex;
        public string SelectedClipName;
        public float SelectedTime;

        public float TotalCost;
        public float TrajectoryCost;
        public float PoseCost;
        public float ContinuationCost;
        public readonly float[] GroupCosts = new float[FeatureGroupExtensions.Count];

        public bool DidJump;
        public int SearchCount;

        /// <summary>Desired trajectory (from input), character space. Cloned per search for drawing.</summary>
        public TrajectorySample[] DesiredTrajectory;

        /// <summary>Trajectory of the selected candidate, character space.</summary>
        public TrajectorySample[] CandidateTrajectory;

        #endregion

        #region Main API

        public void CopyGroupCosts(float[] source)
        {
            for (int i = 0; i < GroupCosts.Length && i < source.Length; i++)
                GroupCosts[i] = source[i];
        }

        public void Clear()
        {
            HasData = false;
            SelectedFrame = -1;
            SelectedClipIndex = -1;
            SelectedClipName = null;
            SelectedTime = 0f;
            TotalCost = TrajectoryCost = PoseCost = ContinuationCost = 0f;
            DidJump = false;
            for (int i = 0; i < GroupCosts.Length; i++) GroupCosts[i] = 0f;
        }

        #endregion
    }
}
