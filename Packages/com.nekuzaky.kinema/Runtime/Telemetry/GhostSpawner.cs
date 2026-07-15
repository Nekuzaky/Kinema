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
    /// The ghost keeps only what it needs to solve locomotion (controller, IK, animator); every
    /// other MonoBehaviour is stripped by a whitelist rather than a blacklist, so gameplay scripts,
    /// input readers and recorders the project added are removed without this class having to know
    /// their types. That is also what keeps this assembly free of a Samples dependency.
    /// </summary>
    public static class GhostSpawner
    {
        #region Public

        /// <summary>Components a ghost keeps. Everything else on the clone is destroyed.</summary>
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
        /// Returns null when the recording is unusable.
        /// </summary>
        public static GameObject Spawn(MotionMatchingController source, SessionRecording recording, bool loop, Color tint)
        {
            if (source == null || recording == null || !recording.IsValid) return null;

            GameObject ghost = UnityEngine.Object.Instantiate(source.gameObject, source.transform.position, source.transform.rotation);
            ghost.name = "Ghost";

            // Instantiate already ran the clone's Awake, so its components are live and its Update
            // would fire this frame. Park it while it is being rebuilt into an NPC.
            ghost.SetActive(false);

            Strip(ghost);
            Tint(ghost, tint);

            var replay = ghost.AddComponent<ReplayLocomotionProvider>();
            replay.Recording = recording;
            replay.Loop = loop;
            replay.RestoreStartPose = true;
            // Global effect: forcing the clock here would dictate the live player's frame rate too.
            replay.ForceRecordedTimestep = false;
            replay.PlayOnStart = true;

            // The clone's controller cached the source's locomotion provider during Awake, and that
            // provider has just been stripped. Without this the ghost stands still holding a dead reference.
            ghost.GetComponent<MotionMatchingController>().SetLocomotionProvider(replay);

            ghost.SetActive(true);
            return ghost;
        }

        #endregion

        #region Tools and Utilities

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
