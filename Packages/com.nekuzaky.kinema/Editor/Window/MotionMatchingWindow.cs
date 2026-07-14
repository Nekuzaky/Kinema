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

        private enum Tab { Overview, Database, Bake, Debug, Settings }
        private static readonly string[] TabNames = { "Overview", "Database", "Bake", "Debug", "Settings" };

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
            // Keep the Debug tab live during play without hammering repaint elsewhere.
            if (_tab == Tab.Debug && Application.isPlaying)
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
                case Tab.Debug: DrawDebug(); break;
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

            using (MotionMatchingStyles.BeginSection("Status"))
            {
                if (_config == null)
                {
                    MotionMatchingStyles.HelpRow("Create or assign a Motion Matching Config to begin. Right-click in the Project window → Create → Kinema → Motion Matching → Config.", MessageType.Info);
                    if (GUILayout.Button("Create Config Asset")) CreateConfigAsset();
                    return;
                }

                bool ready = _config.IsReadyToBake(out string reason);
                MotionMatchingStyles.KeyValue("Config", _config.name);
                MotionMatchingStyles.KeyValue("Clips assigned", _config.Clips.Count.ToString());
                MotionMatchingStyles.KeyValue("Rig", _config.RigPrefab != null ? _config.RigPrefab.name : "— none —");
                MotionMatchingStyles.KeyValue("Feature dimensions", _config.Schema.Dimension.ToString());

                EditorGUILayout.Space(2);
                if (_database != null && _database.IsValid)
                {
                    MotionMatchingStyles.KeyValue("Database frames", _database.FrameCount.ToString("N0"));
                    MotionMatchingStyles.KeyValue("Baked", _database.BakeDateUtc + " UTC");
                }
                else
                {
                    MotionMatchingStyles.HelpRow(ready ? "Config is ready. Open the Bake tab to generate the database." : reason, ready ? MessageType.Info : MessageType.Warning);
                }
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

            using (MotionMatchingStyles.BeginSection("Clips"))
            {
                _clipScroll = EditorGUILayout.BeginScrollView(_clipScroll, GUILayout.MaxHeight(180));
                for (int i = 0; i < _database.ClipCount; i++)
                {
                    MotionClipEntry clip = _database.GetClip(i);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"{i:00}", MotionMatchingStyles.KeyLabel, GUILayout.Width(24));
                        GUILayout.Label(clip.Name, GUILayout.Width(160));
                        GUILayout.Label($"{clip.FrameCount} f", MotionMatchingStyles.KeyLabel, GUILayout.Width(50));
                        GUILayout.Label($"{clip.Length:F2}s", MotionMatchingStyles.KeyLabel, GUILayout.Width(50));
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(clip.Clip == null))
                            if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44)))
                                EditorGUIUtility.PingObject(clip.Clip);
                    }
                }
                EditorGUILayout.EndScrollView();
            }

            using (MotionMatchingStyles.BeginSection("Feature Layout"))
                DrawFeatureLayout(_database.Schema);
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

        #region Tools and Utilities — Debug

        private void DrawDebug()
        {
            _controller = (MotionMatchingController)EditorGUILayout.ObjectField("Controller", _controller, typeof(MotionMatchingController), true);

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
        }

        private static void DrawSwatch(Color color)
        {
            Rect r = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
            EditorGUI.DrawRect(r, color);
            GUILayout.Space(4);
        }

        #endregion

        #region Tools and Utilities — Settings

        private void DrawSettings()
        {
            _config = (MotionMatchingConfig)EditorGUILayout.ObjectField("Config", _config, typeof(MotionMatchingConfig), false);
            if (_config == null)
            {
                MotionMatchingStyles.HelpRow("Assign a config to edit its schema and default weights.", MessageType.Info);
                return;
            }

            SerializedObject so = GetConfigSerialized();
            so.Update();

            using (MotionMatchingStyles.BeginSection("Default Weights"))
            {
                EditorGUILayout.PropertyField(so.FindProperty("_defaultWeights"), true);
                MotionMatchingStyles.HelpRow("Weights are normalized-space multipliers per feature group. Baked into the database as its default; a controller can override them at runtime.", MessageType.None);
            }

            using (MotionMatchingStyles.BeginSection("Feature Schema"))
            {
                SerializedProperty schema = so.FindProperty("_schema");
                EditorGUILayout.PropertyField(schema.FindPropertyRelative("TrajectoryTimes"), true);
                EditorGUILayout.PropertyField(schema.FindPropertyRelative("BoneNames"), true);
                MotionMatchingStyles.HelpRow("Changing the schema changes the feature layout — rebake the database afterwards.", MessageType.Warning);
            }

            so.ApplyModifiedProperties();

            if (_controller != null && Application.isPlaying)
                using (MotionMatchingStyles.BeginSection("Live Controller"))
                    if (GUILayout.Button("Apply Default Weights to Controller"))
                        _controller.Weights = _config.DefaultWeights;
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
