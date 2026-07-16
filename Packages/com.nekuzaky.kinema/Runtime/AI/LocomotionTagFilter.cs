using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Keeps named tags out of the locomotion search, permanently.
    ///
    /// A baked database holds everything the capture contained, and the search will happily pick any
    /// of it: a crouch clip is a perfectly good match for walking if nothing says otherwise, and a
    /// jump clip is a fine way to cross flat ground. The character then does something that reads as
    /// random, but is the cost function doing exactly its job over a candidate set nobody narrowed.
    ///
    /// The player gets this narrowing from whatever drives its stance - it has to, since crouching is
    /// a thing the player asks for. An AI has no such component and so, without this, searches the
    /// whole database.
    ///
    /// Costs nothing per frame: the masks resolve once and the search filters on them anyway.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Locomotion Tag Filter")]
    [RequireComponent(typeof(MotionMatchingController))]
    [DisallowMultipleComponent]
    public sealed class LocomotionTagFilter : MonoBehaviour
    {
        #region Public

        [Tooltip("Tags never eligible for the locomotion search. Airborne motion is event-driven, and " +
                 "a stance the character never adopts should not be competing for every step.")]
        [SerializeField] private string[] _excludedTags = { "Jump", "Crouch" };

        /// <summary>The mask this component contributes, once resolved.</summary>
        public ulong ExcludedMask { get; private set; }

        #endregion

        #region Private and Protected

        private MotionMatchingController _controller;
        private bool _resolved;

        #endregion

        #region Unity API

        private void Awake() => _controller = GetComponent<MotionMatchingController>();

        private void Update()
        {
            // Resolved on the first tick rather than in Awake: the mask comes from the database, and
            // the controller may be handed one after this component wakes - SwitchDatabase exists.
            if (_resolved || !_controller.IsInitialized) return;
            Resolve();
        }

        #endregion

        #region Main API

        /// <summary>Re-resolves against the controller's current database. Call after SwitchDatabase.</summary>
        public void Resolve()
        {
            _resolved = true;
            ExcludedMask = 0ul;

            MotionMatchingDatabase database = _controller.Database;
            if (database == null || !database.HasTags)
            {
                Debug.LogWarning($"[Kinema] '{name}': the database carries no tags, so nothing is " +
                                 "filtered and every clip in it competes for every step. Rebake with " +
                                 "tag ranges authored.", this);
                return;
            }

            foreach (string tag in _excludedTags)
            {
                ulong mask = database.GetTagMask(tag);
                if (mask == 0ul)
                {
                    Debug.LogWarning($"[Kinema] '{name}': no tag named '{tag}' in the database, so it " +
                                     "excludes nothing. Check the spelling against the bake.", this);
                    continue;
                }
                ExcludedMask |= mask;
            }

            // Or-ed, not assigned: something else may own exclusions too, and clobbering them would
            // silently widen the search back out.
            _controller.ExcludedTags |= ExcludedMask;
        }

        #endregion
    }
}
