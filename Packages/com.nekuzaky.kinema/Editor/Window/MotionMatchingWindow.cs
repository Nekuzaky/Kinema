using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// The control surface for the whole tool: a five-tab window (Overview / Database / Bake /
    /// Debug / Settings) to assign clips, bake, inspect the generated data, tune weights and read
    /// live matching state. IMGUI on purpose — it is the fastest way to a dense, reliable tool UI.
    /// </summary>
    public sealed class MotionMatchingWindow : EditorWindow
    {
        #region Private and Protected

        private enum Tab { Overview, Database, Bake, Tags, Debug, Analysis, Settings }
        private static readonly string[] TabNames = { "Overview", "Database", "Bake", "Tags", "Debug", "Analysis", "Settings" };

        private readonly TagTimelineDrawer _tagDrawer = new TagTimelineDrawer();
        private readonly AnalysisTabDrawer _analysisDrawer = new AnalysisTabDrawer();

        [SerializeField] private MotionMatchingConfig _config;
        [SerializeField] private MotionMatchingDatabase _database;
        [SerializeField] private Tab _tab;
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private Vector2 _clipScroll;

        private SerializedObject _configSerialized;
        private MotionMatchingController _controller;

        private string _bakeMessage;
        private MessageType _bakeMessageType = MessageType.None;

        #endregion

        #region Unity API

        [MenuItem("Kinema/Motion Matching/Window %#m", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<MotionMatchingWindow>();
            window.titleContent = new GUIContent("Motion Matching");
            window.minSize = new Vector2(420, 460);
            window.Show();
        }

        private void OnEnable()
        {
            AutoAssign();
            RefreshController();
        }

        private void OnSelectionChange()
        {
            RefreshController();
            Repaint();
        }

        private void OnInspectorUpdate()
        {
            // Keep the live tabs updating during play without hammering repaint elsewhere.
            if ((_tab == Tab.Debug || _tab == Tab.Analysis) && Application.isPlaying)
                Repaint();
        }

        private void OnGUI()
        {
            DrawHeader();
            _tab = (Tab)GUILayout.Toolbar((int)_tab, TabNames, GUILayout.Height(24));
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Overview: DrawOverview(); break;
                case Tab.Database: DrawDatabase(); break;
                case Tab.Bake: DrawBake(); break;
                case Tab.Tags: DrawTags(); break;
                case Tab.Debug: DrawDebug(); break;
                case Tab.Analysis: DrawAnalysis(); break;
                case Tab.Settings: DrawSettings(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region Tools and Utilities — Header

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Motion Matching", MotionMatchingStyles.Title);
                GUILayout.FlexibleSpace();
                DrawStatusPill();
            }
            DrawSeparator();
        }

        private void DrawStatusPill()
        {
            if (_database != null && _database.IsValid)
                MotionMatchingStyles.StatusPill("DATABASE READY", MotionMatchingStyles.Ok);
            else if (_config != null)
                MotionMatchingStyles.StatusPill("NEEDS BAKE", MotionMatchingStyles.Warning);
            else
                MotionMatchingStyles.StatusPill("NO CONFIG", MotionMatchingStyles.Error);
        }

        private static void DrawSeparator()
        {
            Rect r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
            EditorGUILayout.Space(4);
        }

        #endregion

        #region Tools and Utilities — Overview

        private void DrawOverview()
        {
            using (MotionMatchingStyles.BeginSection("Assets"))
            {
                EditorGUI.BeginChangeCheck();
                _config = (MotionMatchingConfig)EditorGUILayout.ObjectField("Config", _config, typeof(MotionMatchingConfig), false);
                _database = (MotionMatchingDatabase)EditorGUILayout.ObjectField("Database", _database, typeof(MotionMatchingDatabase), false);
                if (EditorGUI.EndChangeCheck()) _configSerialized = null;
            }

            if (_config == null)
            {
                using (MotionMatchingStyles.BeginSection("Status"))
                {
                    MotionMatchingStyles.HelpRow("Create or assign a Motion Matching Config to begin. Right-click in the Project window → Create → Kinema → Motion Matching → Config.", MessageType.Info);
                    if (GUILayout.Button("Create Config Asset")) CreateConfigAsset();
                }
                return;
            }

            // Dashboard cards.
            using (new EditorGUILayout.HorizontalScope())
            {
                bool dbOk = _database != null && _database.IsValid;
                MotionMatchingStyles.StatCard(dbOk ? _database.FrameCount.ToString("N0") : "—", "Frames", MotionMatchingStyles.Accent);
                MotionMatchingStyles.StatCard(dbOk ? _database.ClipCount.ToString() : _config.Clips.Count.ToString(), "Clips", MotionMatchingStyles.Accent);
                MotionMatchingStyles.StatCard(_config.Schema.Dimension.ToString(), "Dims / frame", MotionMatchingStyles.Accent);
                float mb = dbOk ? _database.FrameCount * _database.Dimension * (_database.IsHalfPrecision ? 2f : 4f) / (1024f * 1024f) : 0f;
                MotionMatchingStyles.StatCard(dbOk ? mb.ToString("F2") : "—", "MB features", MotionMatchingStyles.Accent);
            }

            using (MotionMatchingStyles.BeginSection("Subsystems"))
            {
                bool dbOk = _database != null && _database.IsValid;
                MotionMatchingStyles.SubsystemRow("Database", dbOk,
                    dbOk ? $"baked {_database.BakeDateUtc} UTC @ {_database.BakeFrameRate} fps" : "not baked");
                MotionMatchingStyles.SubsystemRow("Foot contacts", dbOk && _database.HasContacts,
                    dbOk && _database.HasContacts ? $"{_database.ContactBoneCount} contact bone(s)" : "no contact data (rebake)");
                MotionMatchingStyles.SubsystemRow("Tags", dbOk && _database.HasTags,
                    dbOk && _database.HasTags ? $"{_database.TagNames.Length} tag(s) baked" : _config.TagNames.Count > 0 ? $"{_config.TagNames.Count} declared, not baked" : "none declared");
                MotionMatchingStyles.SubsystemRow("Mirroring", dbOk && _database.HasMirroredFrames,
                    dbOk && _database.HasMirroredFrames ? "mirrored variants baked" : _config.GenerateMirroredVariants ? "enabled, rebake needed" : "off");
                MotionMatchingStyles.SubsystemRow("Calibration profiles", dbOk && _database.CalibrationProfiles.Length > 0,
                    dbOk && _database.CalibrationProfiles.Length > 0 ? $"{_database.CalibrationProfiles.Length} profile(s)" : "none");
                MotionMatchingStyles.SubsystemRow("Storage", dbOk && _database.IsHalfPrecision,
                    dbOk && _database.IsHalfPrecision ? "16-bit half precision" : "32-bit float");

                if (!_config.IsReadyToBake(out string reason))
                    MotionMatchingStyles.HelpRow(reason, MessageType.Warning);
            }

            using (MotionMatchingStyles.BeginSection("Quick Actions"))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Go to Bake")) _tab = Tab.Bake;
                using (new EditorGUI.DisabledScope(_database == null))
                    if (GUILayout.Button("Select Database")) Selection.activeObject = _database;
                using (new EditorGUI.DisabledScope(_config == null))
                    if (GUILayout.Button("Select Config")) Selection.activeObject = _config;
            }
        }

        #endregion

        #region Tools and Utilities — Database

        private void DrawDatabase()
        {
            if (_database == null || !_database.IsValid)
            {
                MotionMatchingStyles.HelpRow("No valid database assigned. Bake one from the Bake tab, or drag a database asset onto the Overview tab.", MessageType.Info);
                return;
            }

            using (MotionMatchingStyles.BeginSection("Summary"))
            {
                MotionMatchingStyles.KeyValue("Frames", _database.FrameCount.ToString("N0"));
                MotionMatchingStyles.KeyValue("Clips", _database.ClipCount.ToString());
                MotionMatchingStyles.KeyValue("Dimensions / frame", _database.Dimension.ToString());
                MotionMatchingStyles.KeyValue("Bake rate", _database.BakeFrameRate + " fps");
                MotionMatchingStyles.KeyValue("Total duration", _database.TotalDurationSeconds.ToString("F2") + " s");
                float mb = _database.FrameCount * _database.Dimension * 4f / (1024f * 1024f);
                MotionMatchingStyles.KeyValue("Feature memory", mb.ToString("F2") + " MB");
                MotionMatchingStyles.KeyValue("Baked", _database.BakeDateUtc + " UTC");
            }

            using (MotionMatchingStyles.BeginSection("Clips — frame share"))
            {
                _clipScroll = EditorGUILayout.BeginScrollView(_clipScroll, GUILayout.MaxHeight(170));
                float total = Mathf.Max(1, _database.FrameCount);
                for (int i = 0; i < _database.ClipCount; i++)
                {
                    MotionClipEntry clip = _database.GetClip(i);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        MotionMatchingStyles.ProportionBar(
                            $"{i:00}  {clip.Name}", clip.FrameCount / total,
                            $"{clip.FrameCount} f · {clip.Length:F2}s", MotionMatchingStyles.Accent);
                        using (new EditorGUI.DisabledScope(clip.Clip == null))
                            if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44)))
                                EditorGUIUtility.PingObject(clip.Clip);
                    }
                }
                EditorGUILayout.EndScrollView();
            }

            DrawFrameInspector();

            using (MotionMatchingStyles.BeginSection("Feature Layout"))
                DrawFeatureLayout(_database.Schema);
        }

        [SerializeField] private int _inspectClip;
        [SerializeField] private int _inspectLocalFrame;
        private Vector3[] _inspectBones;

        /// <summary>
        /// Frame inspector: scrub any baked frame and read back its denormalized data — root speed,
        /// bone positions, contacts and tags. The fastest way to sanity-check a bake.
        /// </summary>
        private void DrawFrameInspector()
        {
            using (MotionMatchingStyles.BeginSection("Frame Inspector"))
            {
                var clipNames = new string[_database.ClipCount];
                for (int i = 0; i < _database.ClipCount; i++) clipNames[i] = $"{i:00} {_database.GetClip(i).Name}";
                _inspectClip = Mathf.Clamp(_inspectClip, 0, _database.ClipCount - 1);
                _inspectClip = EditorGUILayout.Popup("Clip", _inspectClip, clipNames);

                MotionClipEntry clip = _database.GetClip(_inspectClip);
                _inspectLocalFrame = EditorGUILayout.IntSlider("Frame", Mathf.Clamp(_inspectLocalFrame, 0, clip.FrameCount - 1), 0, clip.FrameCount - 1);
                int frame = clip.StartFrame + _inspectLocalFrame;

                MotionFrameInfo info = _database.GetFrame(frame);
                Vector2 rootVel = _database.GetRootVelocity(frame);

                MotionMatchingStyles.KeyValue("Global frame", frame.ToString());
                MotionMatchingStyles.KeyValue("Clip time", info.Time.ToString("F3") + " s");
                MotionMatchingStyles.KeyValue("Root speed", rootVel.magnitude.ToString("F2") + " m/s");

                if (_database.HasContacts)
                {
                    byte contacts = _database.GetContacts(frame);
                    var grounded = new System.Text.StringBuilder();
                    for (int c = 0; c < _database.ContactBoneCount; c++)
                        if ((contacts & (1 << c)) != 0) grounded.Append(grounded.Length > 0 ? ", " : "").Append(_database.GetContactBoneName(c));
                    MotionMatchingStyles.KeyValue("Grounded", grounded.Length > 0 ? grounded.ToString() : "airborne / none");
                }

                if (_database.HasTags)
                {
                    ulong tags = _database.GetFrameTags(frame);
                    var names = new System.Text.StringBuilder();
                    for (int t = 0; t < _database.TagNames.Length && t < 64; t++)
                        if ((tags & (1ul << t)) != 0) names.Append(names.Length > 0 ? ", " : "").Append(_database.TagNames[t]);
                    MotionMatchingStyles.KeyValue("Tags", names.Length > 0 ? names.ToString() : "—");
                }

                int boneCount = _database.Schema.BoneCount;
                if (_inspectBones == null || _inspectBones.Length != boneCount) _inspectBones = new Vector3[boneCount];
                _database.GetBonePositions(frame, _inspectBones);
                for (int b = 0; b < boneCount; b++)
                    MotionMatchingStyles.KeyValue(_database.Schema.BoneNames[b],
                        $"({_inspectBones[b].x:F2}, {_inspectBones[b].y:F2}, {_inspectBones[b].z:F2})  w={_database.Schema.GetBoneWeight(b):F1}");
            }
        }

        private static void DrawFeatureLayout(FeatureSchema schema)
        {
            for (int gi = 0; gi < FeatureGroupExtensions.Count; gi++)
            {
                var g = (FeatureGroup)gi;
                int len = schema.GetGroupLength(g);
                int offset = schema.GetGroupOffset(g);
                MotionMatchingStyles.KeyValue(g.ToDisplayName(), len > 0 ? $"dims [{offset}..{offset + len - 1}]  ({len})" : "—");
            }
        }

        #endregion

        #region Tools and Utilities — Bake

        private void DrawBake()
        {
            _config = (MotionMatchingConfig)EditorGUILayout.ObjectField("Config", _config, typeof(MotionMatchingConfig), false);
            if (_config == null)
            {
                MotionMatchingStyles.HelpRow("Assign a config to bake. It defines the rig, the clips and the feature schema.", MessageType.Info);
                if (GUILayout.Button("Create Config Asset")) CreateConfigAsset();
                return;
            }

            SerializedObject so = GetConfigSerialized();
            so.Update();

            using (MotionMatchingStyles.BeginSection("Rig & Sampling"))
            {
                EditorGUILayout.PropertyField(so.FindProperty("_rigPrefab"));
                EditorGUILayout.PropertyField(so.FindProperty("_bakeFrameRate"));
            }

            using (MotionMatchingStyles.BeginSection("Clips"))
                EditorGUILayout.PropertyField(so.FindProperty("_clips"), true);

            so.ApplyModifiedProperties();

            using (MotionMatchingStyles.BeginSection("Pre-flight"))
            {
                bool hasRig = _config.RigPrefab != null;
                int clipCount = 0; float totalLength = 0f;
                foreach (AnimationClip c in _config.Clips)
                    if (c != null) { clipCount++; totalLength += c.length; }

                MotionMatchingStyles.SubsystemRow("Rig", hasRig, hasRig ? _config.RigPrefab.name : "assign a rig prefab");
                MotionMatchingStyles.SubsystemRow("Clips", clipCount > 0, clipCount > 0 ? $"{clipCount} clip(s), {totalLength:F1}s of motion" : "assign locomotion clips");
                MotionMatchingStyles.SubsystemRow("Schema", _config.Schema.Dimension > 0, $"{_config.Schema.Dimension} dims ({_config.Schema.TrajectoryPointCount} traj pts, {_config.Schema.BoneCount} bones)");

                int estFrames = Mathf.CeilToInt(totalLength * _config.BakeFrameRate);
                if (_config.GenerateMirroredVariants) estFrames *= 2;
                float estMb = estFrames * _config.Schema.Dimension * (_config.HalfPrecision ? 2f : 4f) / (1024f * 1024f);
                MotionMatchingStyles.KeyValue("Estimated frames", estFrames.ToString("N0") + (_config.GenerateMirroredVariants ? " (incl. mirrored)" : ""));
                MotionMatchingStyles.KeyValue("Estimated size", estMb.ToString("F2") + " MB " + (_config.HalfPrecision ? "(16-bit)" : "(32-bit)"));
            }

            using (MotionMatchingStyles.BeginSection("Bake"))
            {
                bool ready = _config.IsReadyToBake(out string reason);
                if (!ready) MotionMatchingStyles.HelpRow(reason, MessageType.Warning);

                using (new EditorGUI.DisabledScope(!ready))
                {
                    GUI.backgroundColor = MotionMatchingStyles.Accent;
                    if (GUILayout.Button(_database != null ? "Rebake Database" : "Bake Database", GUILayout.Height(30)))
                        RunBake();
                    GUI.backgroundColor = Color.white;
                }

                if (!string.IsNullOrEmpty(_bakeMessage))
                    MotionMatchingStyles.HelpRow(_bakeMessage, _bakeMessageType);
            }
        }

        private void RunBake()
        {
            BakeReport report = MotionMatchingBaker.Bake(_config, _database);
            if (report.Success)
            {
                _database = report.Database;
                _configSerialized = null;
                string warnings = report.Warnings.Count > 0 ? $"\n{report.Warnings.Count} warning(s):\n• " + string.Join("\n• ", report.Warnings) : "";
                _bakeMessage = $"Baked {report.FrameCount:N0} frames from {report.ClipCount} clip(s), {report.Dimension} dims → {report.DatabasePath}{warnings}";
                _bakeMessageType = report.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info;
                EditorGUIUtility.PingObject(_database);
            }
            else
            {
                _bakeMessage = "Bake failed: " + report.Error;
                _bakeMessageType = MessageType.Error;
            }
        }

        #endregion

        #region Tools and Utilities — Tags

        private void DrawTags()
        {
            _config = (MotionMatchingConfig)EditorGUILayout.ObjectField("Config", _config, typeof(MotionMatchingConfig), false);
            if (_config == null)
            {
                MotionMatchingStyles.HelpRow("Assign a config to author tags.", MessageType.Info);
                return;
            }

            SerializedObject so = GetConfigSerialized();
            so.Update();
            _tagDrawer.Draw(_config, so);
            so.ApplyModifiedProperties();
        }

        #endregion

        #region Tools and Utilities — Debug

        private void DrawDebug()
        {
            EditorGUI.BeginChangeCheck();
            _controller = (MotionMatchingController)EditorGUILayout.ObjectField("Controller", _controller, typeof(MotionMatchingController), true);
            if (EditorGUI.EndChangeCheck() && _previewMode) TogglePreview(_previewedController, false);

            if (_controller == null)
            {
                MotionMatchingStyles.HelpRow("Select a GameObject with a MotionMatchingController, or enter Play mode. Live matching state appears here.", MessageType.Info);
                return;
            }
            if (!Application.isPlaying)
            {
                MotionMatchingStyles.HelpRow("Enter Play mode to see live matching data.", MessageType.Info);
                return;
            }
            if (!_controller.IsInitialized)
            {
                MotionMatchingStyles.HelpRow("Controller is not initialized (missing or invalid database).", MessageType.Warning);
                return;
            }

            MotionMatchingDebugData debug = _controller.LastDebug;
            if (debug == null || !debug.HasData)
            {
                MotionMatchingStyles.HelpRow("Waiting for the first search…", MessageType.Info);
                return;
            }

            using (MotionMatchingStyles.BeginSection("Selection"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    MotionMatchingStyles.KeyValue("Clip", debug.SelectedClipName ?? "—");
                    if (debug.DidJump) MotionMatchingStyles.StatusPill("JUMP", MotionMatchingStyles.Accent);
                }
                MotionMatchingStyles.KeyValue("Frame", debug.SelectedFrame.ToString());
                MotionMatchingStyles.KeyValue("Clip time", debug.SelectedTime.ToString("F3") + " s");
                MotionMatchingStyles.KeyValue("Now playing", $"clip {_controller.CurrentClipIndex} @ {_controller.CurrentClipTime:F2}s (frame {_controller.CurrentFrame})");
                MotionMatchingStyles.KeyValue("Searches", debug.SearchCount.ToString("N0"));
            }

            using (MotionMatchingStyles.BeginSection("Cost"))
            {
                MotionMatchingStyles.KeyValue("Total", debug.TotalCost.ToString("F3"));
                MotionMatchingStyles.KeyValue("Trajectory", debug.TrajectoryCost.ToString("F3"));
                MotionMatchingStyles.KeyValue("Pose", debug.PoseCost.ToString("F3"));
                MotionMatchingStyles.KeyValue("Continuation", debug.ContinuationCost < 0f ? "n/a (clip end)" : debug.ContinuationCost.ToString("F3"));
            }

            using (MotionMatchingStyles.BeginSection("Cost Breakdown"))
            {
                float max = 1e-4f;
                for (int i = 0; i < debug.GroupCosts.Length; i++) max = Mathf.Max(max, debug.GroupCosts[i]);
                for (int gi = 0; gi < FeatureGroupExtensions.Count; gi++)
                {
                    var g = (FeatureGroup)gi;
                    Color c = gi < 2 ? MotionMatchingStyles.TrajectoryDesired : MotionMatchingStyles.TrajectoryCandidate;
                    MotionMatchingStyles.CostBar(g.ToDisplayName(), debug.GroupCosts[gi], max, c);
                }
            }

            using (MotionMatchingStyles.BeginSection("Legend"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawSwatch(MotionMatchingStyles.TrajectoryDesired); GUILayout.Label("Desired trajectory", MotionMatchingStyles.KeyLabel);
                    GUILayout.Space(12);
                    DrawSwatch(MotionMatchingStyles.TrajectoryCandidate); GUILayout.Label("Candidate trajectory", MotionMatchingStyles.KeyLabel);
                }
            }

            DrawCostSparkline(_controller);
            DrawSnapshotHistory(_controller);
        }

        private float[] _sparkValues;
        private bool[] _sparkJumps;

        /// <summary>Total-cost history (newest right); orange bars mark searches that jumped clips.</summary>
        private void DrawCostSparkline(MotionMatchingController controller)
        {
            SearchSnapshotRecorder recorder = controller.Snapshots;
            if (recorder == null || recorder.Count == 0) return;

            int count = Mathf.Min(recorder.Count, 120);
            if (_sparkValues == null || _sparkValues.Length < count)
            {
                _sparkValues = new float[count];
                _sparkJumps = new bool[count];
            }

            int jumps = 0;
            for (int i = 0; i < count; i++)
            {
                SearchSnapshot s = recorder.GetByAge(count - 1 - i); // oldest -> newest
                _sparkValues[i] = s.TotalCost;
                _sparkJumps[i] = s.Jumped;
                if (s.Jumped) jumps++;
            }

            using (MotionMatchingStyles.BeginSection($"Cost history — last {count} searches, {jumps} jumps"))
                MotionMatchingStyles.BarSparkline(_sparkValues, _sparkJumps, count);
        }

        [SerializeField] private int _snapshotAge;
        [SerializeField] private bool _previewMode;
        private MotionMatchingController _previewedController;

        /// <summary>
        /// Scrub through the recorded matching decisions (0 = latest). With Preview enabled, live
        /// ticking pauses and the exact recorded pose is replayed on the character - a full visual
        /// rewind, not just the numbers - via <see cref="MotionMatchingController.PreviewSnapshot"/>.
        /// </summary>
        private void DrawSnapshotHistory(MotionMatchingController controller)
        {
            SearchSnapshotRecorder recorder = controller.Snapshots;
            if (recorder == null || recorder.Count == 0) return;

            using (MotionMatchingStyles.BeginSection($"History — {recorder.Count} searches recorded"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _snapshotAge = EditorGUILayout.IntSlider("Steps back", _snapshotAge, 0, recorder.Count - 1);
                    bool newPreview = GUILayout.Toggle(_previewMode, "Preview", EditorStyles.miniButton, GUILayout.Width(64));
                    if (newPreview != _previewMode) TogglePreview(controller, newPreview);
                }

                SearchSnapshot s = recorder.GetByAge(_snapshotAge);
                if (s == null) return;

                if (_previewMode) controller.PreviewSnapshot(s);

                MotionMatchingDatabase db = controller.Database;
                string clipName = s.ClipIndex >= 0 && s.ClipIndex < db.ClipCount ? db.GetClip(s.ClipIndex).Name : "—";

                using (new EditorGUILayout.HorizontalScope())
                {
                    MotionMatchingStyles.KeyValue("At", s.Time.ToString("F2") + " s");
                    if (s.Jumped) MotionMatchingStyles.StatusPill("JUMP", MotionMatchingStyles.Accent);
                    if (_previewMode) MotionMatchingStyles.StatusPill("REWOUND", MotionMatchingStyles.TrajectoryCandidate);
                }
                MotionMatchingStyles.KeyValue("Clip", $"{clipName} @ {s.ClipTime:F3}s (frame {s.SelectedFrame})");
                MotionMatchingStyles.KeyValue("Total / Continuation",
                    $"{s.TotalCost:F3} / {(s.ContinuationCost < 0f ? "n/a" : s.ContinuationCost.ToString("F3"))}");

                float max = 1e-4f;
                for (int i = 0; i < s.GroupCosts.Length; i++) max = Mathf.Max(max, s.GroupCosts[i]);
                for (int gi = 0; gi < FeatureGroupExtensions.Count; gi++)
                {
                    Color c = gi < 2 ? MotionMatchingStyles.TrajectoryDesired : MotionMatchingStyles.TrajectoryCandidate;
                    MotionMatchingStyles.CostBar(((FeatureGroup)gi).ToDisplayName(), s.GroupCosts[gi], max, c);
                }
            }
        }

        private void TogglePreview(MotionMatchingController controller, bool enable)
        {
            _previewMode = enable;
            if (enable)
            {
                _previewedController = controller;
            }
            else if (_previewedController != null)
            {
                _previewedController.StopPreview();
                _previewedController = null;
            }
        }

        /// <summary>Never leave the character frozen mid-rewind if the window closes or the target changes.</summary>
        private void OnDisable()
        {
            if (_previewMode && _previewedController != null)
                _previewedController.StopPreview();
        }

        private static void DrawSwatch(Color color)
        {
            Rect r = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
            EditorGUI.DrawRect(r, color);
            GUILayout.Space(4);
        }

        #endregion

        #region Tools and Utilities — Analysis

        private void DrawAnalysis()
        {
            _controller = (MotionMatchingController)EditorGUILayout.ObjectField("Controller", _controller, typeof(MotionMatchingController), true);
            _analysisDrawer.Draw(_controller);
        }

        #endregion

        #region Tools and Utilities — Settings

        private SerializedObject _controllerSerialized;

        /// <summary>
        /// The full parameter surface in one place: every runtime field of the selected controller
        /// (live-editable in play mode) and every bake-time field of the config. Uses plain framed
        /// sections rather than foldout header groups, which cannot contain the List/array drawers
        /// these objects expose.
        /// </summary>
        private void DrawSettings()
        {
            // ----- Controller: all runtime parameters -----
            _controller = (MotionMatchingController)EditorGUILayout.ObjectField("Controller", _controller, typeof(MotionMatchingController), true);

            using (MotionMatchingStyles.BeginSection("Runtime — Controller"))
            {
                if (_controller == null)
                {
                    MotionMatchingStyles.HelpRow("Select a MotionMatchingController (scene object) to edit every runtime parameter here — search cadence, thresholds, transition mode, blend, prediction, debug colors. Editable live in play mode.", MessageType.Info);
                }
                else
                {
                    DrawAllControllerProperties();
                    if (Application.isPlaying && _controller.IsInitialized)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Reset Weights to DB Default"))
                                _controller.ResetWeightsToDatabaseDefault();
                            DrawProfileButtons();
                        }
                    }
                }
            }

            // ----- Config: all bake-time parameters -----
            _config = (MotionMatchingConfig)EditorGUILayout.ObjectField("Config", _config, typeof(MotionMatchingConfig), false);

            using (MotionMatchingStyles.BeginSection("Bake — Config"))
            {
                if (_config == null)
                {
                    MotionMatchingStyles.HelpRow("Assign a config to edit schema, weights, profiles and storage.", MessageType.Info);
                }
                else
                {
                    DrawAllConfigProperties();
                    MotionMatchingStyles.HelpRow("Schema, storage and mirroring changes alter the baked data — rebake afterwards (Bake tab).", MessageType.Warning);
                }
            }
        }

        /// <summary>Every serialized field of the controller, grouped by its own headers, applied live.</summary>
        private void DrawAllControllerProperties()
        {
            if (_controllerSerialized == null || _controllerSerialized.targetObject != _controller)
                _controllerSerialized = new SerializedObject(_controller);

            SerializedObject so = _controllerSerialized;
            so.Update();

            EditorGUI.BeginChangeCheck();
            SerializedProperty it = so.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.name == "m_Script") continue;
                EditorGUILayout.PropertyField(it, true);
            }

            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
                if (Application.isPlaying && _controller.IsInitialized)
                    _controller.NotifySerializedFieldsChanged(); // weights/acceleration take effect immediately.
            }
        }

        /// <summary>Every serialized field of the config (rig, clips, schema, weights, tags, profiles, storage).</summary>
        private void DrawAllConfigProperties()
        {
            SerializedObject so = GetConfigSerialized();
            so.Update();

            SerializedProperty it = so.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.name == "m_Script") continue;
                EditorGUILayout.PropertyField(it, true);
            }

            so.ApplyModifiedProperties();
        }

        /// <summary>One-click calibration profile buttons when the database ships presets.</summary>
        private void DrawProfileButtons()
        {
            MotionMatchingDatabase db = _controller.Database;
            if (db == null || db.CalibrationProfiles.Length == 0) return;

            foreach (CalibrationProfile profile in db.CalibrationProfiles)
                if (GUILayout.Button($"Profile: {profile.Name}"))
                    _controller.SetCalibrationProfile(profile.Name);
        }

        #endregion

        #region Tools and Utilities — Shared

        private SerializedObject GetConfigSerialized()
        {
            if (_configSerialized == null || _configSerialized.targetObject != _config)
                _configSerialized = new SerializedObject(_config);
            return _configSerialized;
        }

        private void AutoAssign()
        {
            if (_config == null) _config = FindFirstAsset<MotionMatchingConfig>();
            if (_database == null) _database = FindFirstAsset<MotionMatchingDatabase>();
        }

        private void RefreshController()
        {
            if (Selection.activeGameObject != null)
            {
                var c = Selection.activeGameObject.GetComponent<MotionMatchingController>();
                if (c != null) { _controller = c; return; }
            }
            if (_controller == null)
                _controller = FindFirstObjectByType<MotionMatchingController>();
        }

        private void CreateConfigAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Motion Matching Config", "MotionMatchingConfig", "asset",
                "Choose where to save the config.");
            if (string.IsNullOrEmpty(path)) return;

            var config = CreateInstance<MotionMatchingConfig>();
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();
            _config = config;
            _configSerialized = null;
            Selection.activeObject = config;
        }

        private static T FindFirstAsset<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids.Length == 0) return null;
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        #endregion
    }
}
