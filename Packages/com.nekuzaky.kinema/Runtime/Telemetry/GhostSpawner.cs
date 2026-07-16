using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Clones a matched character into a ghost NPC that replays a recorded session through its own
    /// motion matching. Lives in the runtime assembly so the editor window can spawn ghosts too -
    /// the sample hotkey component and the Director tab both funnel through here.
    ///
    /// Two body options. Default: clone the source character, stripped by whitelist so gameplay
    /// scripts, input readers and recorders are removed without this class knowing their types.
    /// Or hand in a different rig: the ghost is built on that model instead, with the controller's
    /// serialized settings copied over, and Humanoid retargeting maps the same database onto the new
    /// proportions - the recorded performance on another character.
    ///
    /// Every ghost carries a <see cref="PoseRecorder"/> capturing from its first frame, so the
    /// performance it produces can be baked to an AnimationClip afterwards.
    /// </summary>
    public static class GhostSpawner
    {
        #region Public

        /// <summary>Components a cloned ghost keeps. Everything else on the clone is destroyed.</summary>
        private static readonly Type[] Keep =
        {
            typeof(MotionMatchingController),
            typeof(FootLockIK),
            typeof(GroundAdaptationIK)
        };

        #endregion

        #region Main API

        /// <summary>
        /// Spawns a ghost of <paramref name="source"/> replaying <paramref name="recording"/>.
        /// With <paramref name="rig"/> set, the ghost is built on that model instead of a clone.
        /// Returns null when the recording is unusable.
        /// </summary>
        public static GameObject Spawn(MotionMatchingController source, SessionRecording recording, bool loop, Color tint, GameObject rig = null, bool exact = true)
        {
            if (source == null || recording == null || !recording.IsValid) return null;

            GameObject ghost = rig != null ? BuildOnRig(source, rig) : BuildClone(source);
            if (ghost == null) return null;
            ghost.name = "Ghost";

            Tint(ghost, tint);

            var replay = ghost.AddComponent<ReplayLocomotionProvider>();
            replay.Recording = recording;
            replay.Loop = loop;
            replay.RestoreStartPose = true;
            // Global effect: forcing the clock here would dictate the live player's frame rate too.
            replay.ForceRecordedTimestep = false;
            replay.PlayOnStart = true;
            // Exact by default: reproduce the original transform and selected frames, not re-match
            // the intent. A ghost on a *different* rig cannot show the source database's frames, so
            // it falls back to intent replay (retargeting is the whole point there).
            replay.ExactReplay = exact && rig == null;
            ghost.GetComponent<MotionMatchingController>().SetLocomotionProvider(replay);

            // Capture the ghost's own performance from its first frame, so it can be baked to a clip.
            var poseRecorder = ghost.AddComponent<PoseRecorder>();

            ghost.SetActive(true);
            poseRecorder.StartRecording();
            return ghost;
        }

        #endregion

        #region Tools and Utilities

        private static GameObject BuildClone(MotionMatchingController source)
        {
            GameObject ghost = UnityEngine.Object.Instantiate(source.gameObject, source.transform.position, source.transform.rotation);

            // Instantiate already ran the clone's Awake, so its components are live and its Update
            // would fire this frame. Park it while it is being rebuilt into an NPC.
            ghost.SetActive(false);
            Strip(ghost);
            return ghost;
        }

        /// <summary>
        /// Builds the ghost on a different model: instantiate the rig, copy the controller's (and
        /// IK's) serialized settings via the JSON round-trip - asset references included - and point
        /// the controller at the rig's own Animator. Humanoid retargeting does the rest, which is
        /// also the requirement: a Generic rig cannot receive a Humanoid-baked database.
        /// </summary>
        private static GameObject BuildOnRig(MotionMatchingController source, GameObject rig)
        {
            GameObject ghost = UnityEngine.Object.Instantiate(rig, source.transform.position, source.transform.rotation);
            ghost.SetActive(false);

            var animator = ghost.GetComponentInChildren<Animator>();
            if (animator == null) animator = ghost.AddComponent<Animator>();
            animator.applyRootMotion = true;
            if (!animator.isHuman)
                Debug.LogWarning($"[Kinema] Ghost rig '{rig.name}' is not Humanoid: the database cannot retarget onto it and the ghost will not animate. Import the model as Humanoid.", ghost);

            foreach (Type type in Keep)
            {
                Component original = source.GetComponent(type);
                if (original == null) continue;
                Component copy = ghost.AddComponent(type);
                // Serialized fields only, asset references included - exactly what a ghost needs.
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(original), copy);
            }

            // The JSON copy carried the source's Animator reference; re-point before OnEnable runs.
            ghost.GetComponent<MotionMatchingController>().SetAnimator(animator);
            return ghost;
        }

        private static void Strip(GameObject ghost)
        {
            var doomed = new List<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in ghost.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;   // already-broken scripts on the rig
                bool kept = false;
                foreach (Type type in Keep)
                {
                    if (type.IsInstanceOfType(behaviour)) { kept = true; break; }
                }
                if (!kept) doomed.Add(behaviour);
            }
            foreach (MonoBehaviour behaviour in doomed) UnityEngine.Object.Destroy(behaviour);

            StripColliders(ghost);
        }

        /// <summary>
        /// A ghost is a recording being played back, not a body in the world: it must not collide
        /// with anything, and nothing must collide with it.
        ///
        /// Colliders are not MonoBehaviours, so the whitelist above never touched them and every
        /// ghost kept the source character's CharacterController. Two consequences, both reported as
        /// bugs: a ghost spawns on top of the player, and two overlapping CharacterControllers get
        /// depenetrated - the player is shoved sideways, apparently moving on its own. And because a
        /// ghost has no motor to resolve its own movement, it drifts through the level as a roaming
        /// capsule the player then walks into: an invisible wall.
        ///
        /// Disabled rather than destroyed: CharacterController is a [RequireComponent] dependency of
        /// components the ghost keeps, and Destroy would be refused.
        /// </summary>
        private static void StripColliders(GameObject ghost)
        {
            foreach (Collider collider in ghost.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

        private static void Tint(GameObject ghost, Color color)
        {
            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Material source = renderers[0].sharedMaterial;
            Material material = source != null ? new Material(source) : null;
            if (material == null) return;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;

            foreach (Renderer renderer in renderers) renderer.sharedMaterial = material;
        }

        #endregion
    }
}
