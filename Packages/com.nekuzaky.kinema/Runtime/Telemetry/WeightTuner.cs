using System.Collections.Generic;
using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Tunes the matching weights against a recorded session, automatically.
    ///
    /// The pieces already exist: a deterministic replay (same intent, same timestep, every pass) and
    /// a quality metric (foot slide, matcher jumps). This closes the loop - coordinate descent over
    /// the weights, one full replay per evaluation, keep whatever the metric says is better. What a
    /// studio tunes by hand across afternoons becomes: record a lap, press tune, wait.
    ///
    /// Runs on the player character (not a ghost): it needs the deterministic timestep, which is a
    /// global effect, so tuning is a dedicated session rather than something behind gameplay.
    /// Expect minutes of real time - every candidate weight set replays the entire take.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Weight Tuner")]
    [RequireComponent(typeof(MotionMatchingController))]
    [RequireComponent(typeof(MotionQualityProbe))]
    [RequireComponent(typeof(ReplayLocomotionProvider))]
    public sealed class WeightTuner : MonoBehaviour
    {
        #region Public

        [Tooltip("Coordinate-descent sweeps over the full weight set.")]
        [SerializeField, Range(1, 4)] private int _rounds = 2;

        [Tooltip("Multiplicative step tried on each weight (both up and down).")]
        [SerializeField, Range(1.1f, 2f)] private float _step = 1.35f;

        [Tooltip("How much a matcher jump per second costs, in foot-slide-m/s equivalents. Penalizes flicker, not decisiveness.")]
        [SerializeField, Range(0f, 0.5f)] private float _jumpPenalty = 0.05f;

        public bool IsTuning { get; private set; }
        public int PassesDone { get; private set; }
        public int PassesPlanned { get; private set; }
        public float BestScore { get; private set; } = float.MaxValue;
        public FeatureWeights BestWeights { get; private set; }

        #endregion

        #region Private and Protected

        private MotionMatchingController _controller;
        private MotionQualityProbe _probe;
        private ReplayLocomotionProvider _replay;

        private List<Candidate> _plan;
        private int _planIndex;
        private bool _passRunning;

        private struct Candidate
        {
            public FeatureWeights Weights;
            public int Parameter;      // -1 = baseline / re-baseline
            public string Label;
        }

        #endregion

        #region Unity API

        private void Awake()
        {
            _controller = GetComponent<MotionMatchingController>();
            _probe = GetComponent<MotionQualityProbe>();
            _replay = GetComponent<ReplayLocomotionProvider>();
        }

        private void Update()
        {
            if (!IsTuning) return;

            // A pass ends when the replay runs out of tape.
            if (_passRunning && !_replay.IsReplaying)
            {
                _passRunning = false;
                ScorePass();
            }

            if (!_passRunning)
            {
                if (_planIndex >= _plan.Count) { Finish(); return; }
                StartPass(_plan[_planIndex]);
            }
        }

        #endregion

        #region Main API

        /// <summary>Starts tuning against the replay's assigned recording. One evaluation = one full replay.</summary>
        public void StartTuning()
        {
            if (IsTuning) return;
            SessionRecording recording = _replay.Recording;
            if (recording == null || !recording.IsValid)
            {
                Debug.LogWarning("[Kinema] WeightTuner needs a recording on the ReplayLocomotionProvider.", this);
                return;
            }

            BuildPlan();
            BestScore = float.MaxValue;
            BestWeights = _controller.Weights;
            _planIndex = 0;
            PassesDone = 0;
            PassesPlanned = _plan.Count;
            IsTuning = true;

            float minutes = _plan.Count * recording.Duration / 60f;
            Debug.Log($"[Kinema] Tuning: {_plan.Count} passes x {recording.Duration:F0}s take (~{minutes:F1} min of replay). " +
                      "Deterministic timestep is on for the duration.", this);
        }

        public void StopTuning()
        {
            if (!IsTuning) return;
            IsTuning = false;
            _replay.Stop();
            _controller.Weights = BestWeights;
            Debug.Log($"[Kinema] Tuning stopped after {PassesDone}/{PassesPlanned} passes. Best score {BestScore:F4} applied.", this);
        }

        #endregion

        #region Tools and Utilities

        /// <summary>
        /// Coordinate descent, laid out as a flat plan: baseline, then each parameter up and down,
        /// repeated for the configured rounds. Winners are folded in as passes complete, so later
        /// rounds descend from the improved point.
        /// </summary>
        private void BuildPlan()
        {
            _plan = new List<Candidate> { new Candidate { Weights = _controller.Weights, Parameter = -1, Label = "baseline" } };
            int parameters = _controller.Database != null && _controller.Database.HasFootPhases ? 6 : 5;

            for (int round = 0; round < _rounds; round++)
            for (int p = 0; p < parameters; p++)
            {
                // Weights are filled in at pass start from the current best - placeholders here.
                _plan.Add(new Candidate { Parameter = p, Label = $"r{round} p{p} x{_step:F2}" });
                _plan.Add(new Candidate { Parameter = p, Label = $"r{round} p{p} /{_step:F2}" });
            }
        }

        private void StartPass(Candidate candidate)
        {
            FeatureWeights weights = candidate.Parameter < 0
                ? candidate.Weights
                : Scale(BestWeights, candidate.Parameter, candidate.Label.Contains("x") ? _step : 1f / _step);

            _controller.Weights = weights;
            _plan[_planIndex] = new Candidate { Weights = weights, Parameter = candidate.Parameter, Label = candidate.Label };

            _probe.ResetMetrics();
            _controller.ResetTelemetry();
            _replay.Loop = false;
            _replay.RestoreStartPose = true;
            _replay.ForceRecordedTimestep = true;   // identical clocks or the passes are not comparable
            _replay.Play();
            _passRunning = true;
        }

        private void ScorePass()
        {
            float score = _probe.FootSlideRate + _jumpPenalty * _probe.JumpsPerSecond;
            Candidate candidate = _plan[_planIndex];

            if (score < BestScore)
            {
                BestScore = score;
                BestWeights = candidate.Weights;
                Debug.Log($"[Kinema] Tuning {candidate.Label}: {score:F4} <- new best " +
                          $"(slide {_probe.FootSlideRate:F3} m/s, {_probe.JumpsPerSecond:F1} jumps/s)", this);
            }

            PassesDone++;
            _planIndex++;
        }

        private void Finish()
        {
            IsTuning = false;
            _replay.ForceRecordedTimestep = false;
            _controller.Weights = BestWeights;
            Debug.Log($"[Kinema] Tuning done: best score {BestScore:F4} after {PassesDone} passes. Weights applied - " +
                      "copy them into the config's Default Weights to keep them past this session.", this);
        }

        private static FeatureWeights Scale(FeatureWeights weights, int parameter, float factor)
        {
            switch (parameter)
            {
                case 0: weights.TrajectoryPosition *= factor; break;
                case 1: weights.TrajectoryDirection *= factor; break;
                case 2: weights.BonePosition *= factor; break;
                case 3: weights.BoneVelocity *= factor; break;
                case 4: weights.RootVelocity *= factor; break;
                case 5: weights.FootPhase = Mathf.Max(0.05f, weights.FootPhase) * factor; break;
            }
            return weights;
        }

        #endregion
    }
}
