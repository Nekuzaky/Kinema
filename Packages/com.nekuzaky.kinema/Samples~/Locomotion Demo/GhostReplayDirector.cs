using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kinema.MotionMatching.Samples
{
    /// <summary>
    /// Records what you do, then sends an NPC out to do it again.
    ///
    /// Two captures run at once because they answer different questions. The session recording holds
    /// your *intent* - the velocity you asked for each frame - and the ghost is driven by that,
    /// re-running its own matching against it. So the ghost is not a video of you: it makes its own
    /// decisions and only reproduces the trajectory. Two ghosts spawned from one take can differ
    /// slightly, and that is the system working, not a bug.
    ///
    /// The pose recording holds the *result* - the actual bones after matching, blending and IK - and
    /// is what gets baked into an AnimationClip, where an exact record is the whole point.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Samples/Ghost Replay Director")]
    [RequireComponent(typeof(MotionMatchingController))]
    [RequireComponent(typeof(SessionRecorder))]
    public sealed class GhostReplayDirector : MonoBehaviour
    {
        #region Public

        [Tooltip("Starts and stops recording.")]
        [SerializeField] private Key _recordKey = Key.R;

        [Tooltip("Sends a ghost out to redo the last take.")]
        [SerializeField] private Key _spawnGhostKey = Key.G;

        [Tooltip("Removes every ghost currently running. Not C - that is the crouch stance.")]
        [SerializeField] private Key _clearGhostsKey = Key.K;

        [Tooltip("Ghosts loop their take instead of stopping at the end.")]
        [SerializeField] private bool _loopGhosts = true;

        [Tooltip("Send a ghost out automatically as soon as a recording stops.")]
        [SerializeField] private bool _spawnOnStop = true;

        [SerializeField] private Color _ghostColor = new Color(0.2f, 0.9f, 1f, 1f);

        public bool IsRecording => _sessionRecorder.IsRecording;
        public int GhostCount => _ghosts.Count;
        public float LastTakeDuration => _lastRecording != null ? _lastRecording.Duration : 0f;

        /// <summary>Pose take of the last recording, for the editor to bake into a clip. Null until one exists.</summary>
        public PoseTake LastPoseTake => _poseRecorder != null ? _poseRecorder.LastTake : null;

        #endregion

        #region Private and Protected

        private MotionMatchingController _controller;
        private SessionRecorder _sessionRecorder;
        private PoseRecorder _poseRecorder;
        private SessionRecording _lastRecording;
        private readonly List<GameObject> _ghosts = new();
        private Material _ghostMaterial;

        #endregion

        #region Unity API

        private void Awake()
        {
            _controller = GetComponent<MotionMatchingController>();
            _sessionRecorder = GetComponent<SessionRecorder>();
            _poseRecorder = GetComponent<PoseRecorder>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            // The browser overlay has a text field; typing in it must not fire these shortcuts.
            if (GUIUtility.keyboardControl != 0) return;

            if (keyboard[_recordKey].wasPressedThisFrame) ToggleRecording();
            if (keyboard[_spawnGhostKey].wasPressedThisFrame) SpawnGhost();
            if (keyboard[_clearGhostsKey].wasPressedThisFrame) ClearGhosts();
        }

        private void OnDestroy()
        {
            if (_ghostMaterial != null) Destroy(_ghostMaterial);
        }

        #endregion

        #region Main API

        public void ToggleRecording()
        {
            if (IsRecording) StopRecording();
            else StartRecording();
        }

        public void StartRecording()
        {
            _sessionRecorder.StartRecording();
            if (_poseRecorder != null) _poseRecorder.StartRecording();
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            _sessionRecorder.StopRecording();
            if (_poseRecorder != null) _poseRecorder.StopRecording();

            if (_sessionRecorder.RecordedFrameCount < 2)
            {
                Debug.LogWarning("[Kinema] Take too short to replay; discarded.", this);
                return;
            }

            // A runtime ScriptableObject: assets cannot be written in play mode, and the ghost only
            // needs the data in memory. Saving it to disk is the editor's job.
            _lastRecording = ScriptableObject.CreateInstance<SessionRecording>();
            _sessionRecorder.WriteTo(_lastRecording, "runtime");

            Debug.Log($"[Kinema] Recorded {_lastRecording.FrameCount} frames, {_lastRecording.Duration:F1}s, " +
                      $"{_lastRecording.DistanceTravelled():F1} m travelled.", this);

            if (_spawnOnStop) SpawnGhost();
        }

        /// <summary>Clones the character, strips everything that makes it a player, and hands it the tape.</summary>
        public void SpawnGhost()
        {
            if (_lastRecording == null || !_lastRecording.IsValid)
            {
                Debug.LogWarning("[Kinema] Nothing recorded yet - press the record key, move around, press it again.", this);
                return;
            }

            GameObject ghost = Instantiate(gameObject, transform.position, transform.rotation);
            ghost.name = $"Ghost {_ghosts.Count + 1}";

            // Instantiate already ran the clone's Awake, so its components are live and its Update
            // would fire this frame. Park it while it is being rebuilt into an NPC.
            ghost.SetActive(false);

            StripPlayerComponents(ghost);
            TintGhost(ghost);

            var replay = ghost.AddComponent<ReplayLocomotionProvider>();
            replay.Recording = _lastRecording;
            replay.Loop = _loopGhosts;
            replay.RestoreStartPose = true;
            // Global effect: forcing the clock here would dictate the live player's frame rate too.
            replay.ForceRecordedTimestep = false;
            replay.PlayOnStart = true;

            // The clone's controller cached the input provider during Awake, and that provider has
            // just been stripped. Without this the ghost would stand still holding a dead reference.
            ghost.GetComponent<MotionMatchingController>().SetLocomotionProvider(replay);

            ghost.SetActive(true);
            _ghosts.Add(ghost);
            Debug.Log($"[Kinema] Ghost {_ghosts.Count} replaying {_lastRecording.Duration:F1}s of intent through its own matching.", this);
        }

        public void ClearGhosts()
        {
            foreach (GameObject ghost in _ghosts)
                if (ghost != null) Destroy(ghost);
            _ghosts.Clear();
        }

        #endregion

        #region Tools and Utilities

        /// <summary>
        /// A ghost keeps matching, IK and the motor - it has to solve its own locomotion. It loses
        /// anything that reads input, records, or would recursively spawn more ghosts.
        /// </summary>
        private static void StripPlayerComponents(GameObject ghost)
        {
            Destroy(ghost.GetComponent<GhostReplayDirector>());
            DestroyIfPresent<LocomotionInputProvider>(ghost);
            DestroyIfPresent<AnimationBrowser>(ghost);
            DestroyIfPresent<VaultTrigger>(ghost);
            DestroyIfPresent<SessionRecorder>(ghost);
            DestroyIfPresent<PoseRecorder>(ghost);
            DestroyIfPresent<StanceTagController>(ghost);
            DestroyIfPresent<MotionQualityProbe>(ghost);
        }

        private static void DestroyIfPresent<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            if (component != null) Destroy(component);
        }

        private void TintGhost(GameObject ghost)
        {
            var renderers = ghost.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            if (_ghostMaterial == null)
            {
                Material source = renderers[0].sharedMaterial;
                _ghostMaterial = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (_ghostMaterial.HasProperty("_BaseColor")) _ghostMaterial.SetColor("_BaseColor", _ghostColor);
                else _ghostMaterial.color = _ghostColor;
            }

            foreach (Renderer renderer in renderers) renderer.sharedMaterial = _ghostMaterial;
        }

        #endregion
    }
}
