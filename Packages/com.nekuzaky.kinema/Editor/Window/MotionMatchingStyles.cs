using System;
using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Shared visual language for the motion matching editor UI: colors, section framing, status
    /// pills and cost bars. Centralizing it here is what makes the window and the inspectors feel
    /// like one tool instead of three separate IMGUI dumps.
    /// </summary>
    public static class MotionMatchingStyles
    {
        #region Public

        public static readonly Color Accent = new Color(0.30f, 0.72f, 1.00f);
        public static readonly Color Ok = new Color(0.40f, 0.85f, 0.45f);
        public static readonly Color Warning = new Color(1.00f, 0.78f, 0.25f);
        public static readonly Color Error = new Color(1.00f, 0.42f, 0.38f);
        public static readonly Color Muted = new Color(1f, 1f, 1f, 0.45f);
        public static readonly Color TrajectoryDesired = new Color(0.20f, 0.80f, 1.00f);
        public static readonly Color TrajectoryCandidate = new Color(1.00f, 0.70f, 0.10f);

        #endregion

        #region Private and Protected

        private static bool _init;
        private static GUIStyle _title;
        private static GUIStyle _sectionTitle;
        private static GUIStyle _keyLabel;
        private static GUIStyle _valueLabel;
        private static GUIStyle _cardBox;
        private static GUIStyle _pill;

        #endregion

        #region Main API

        public static GUIStyle Title { get { Ensure(); return _title; } }
        public static GUIStyle SectionTitle { get { Ensure(); return _sectionTitle; } }
        public static GUIStyle KeyLabel { get { Ensure(); return _keyLabel; } }
        public static GUIStyle ValueLabel { get { Ensure(); return _valueLabel; } }
        public static GUIStyle CardBox { get { Ensure(); return _cardBox; } }

        /// <summary>Draws a framed section with a title; dispose the returned scope to close it.</summary>
        public static Section BeginSection(string title) => new Section(title);

        public static void StatusPill(string text, Color color)
        {
            Ensure();
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label(" " + text + " ", _pill);
            GUI.backgroundColor = prev;
        }

        public static void KeyValue(string key, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(key, KeyLabel, GUILayout.Width(150));
                GUILayout.Label(value, ValueLabel);
            }
        }

        /// <summary>Horizontal labeled bar, filled proportionally to value/max. Great for cost breakdowns.</summary>
        public static void CostBar(string label, float value, float max, Color color)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, KeyLabel, GUILayout.Width(150));

                Rect rect = GUILayoutUtility.GetRect(60, 16, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));

                float t = max > 1e-6f ? Mathf.Clamp01(value / max) : 0f;
                var fill = new Rect(rect.x, rect.y, rect.width * t, rect.height);
                EditorGUI.DrawRect(fill, color);

                var labelRect = new Rect(rect.x + 4, rect.y, rect.width - 8, rect.height);
                GUI.Label(labelRect, value.ToString("F3"), ValueLabel);
            }
        }

        public static void HelpRow(string message, MessageType type) => EditorGUILayout.HelpBox(message, type);

        #endregion

        #region Tools and Utilities

        private static void Ensure()
        {
            if (_init) return;
            _init = true;

            _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, margin = new RectOffset(4, 4, 6, 6) };
            _sectionTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            _keyLabel = new GUIStyle(EditorStyles.label) { normal = { textColor = Muted }, fontSize = 11 };
            _valueLabel = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            _cardBox = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(2, 2, 2, 6)
            };
            _pill = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white, background = Texture2D.whiteTexture },
                padding = new RectOffset(8, 8, 2, 2),
                margin = new RectOffset(2, 2, 2, 2)
            };
        }

        /// <summary>Disposable framed-section scope, used with a <c>using</c> block.</summary>
        public readonly struct Section : IDisposable
        {
            private readonly EditorGUILayout.VerticalScope _scope;

            public Section(string title)
            {
                _scope = new EditorGUILayout.VerticalScope(CardBox);
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect accent = GUILayoutUtility.GetRect(3, 15, GUILayout.Width(3));
                    EditorGUI.DrawRect(accent, Accent);
                    GUILayout.Space(4);
                    GUILayout.Label(title, SectionTitle);
                }
                GUILayout.Space(4);
            }

            public void Dispose() => _scope.Dispose();
        }

        #endregion
    }
}
