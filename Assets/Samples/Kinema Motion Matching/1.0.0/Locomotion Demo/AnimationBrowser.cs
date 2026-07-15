using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kinema.MotionMatching.Samples
{
    /// <summary>
    /// In-game browser for the whole database: every clip playable on demand, every tag toggleable,
    /// and the live quality numbers next to them.
    ///
    /// A motion matching database is opaque from the outside - you see whatever the matcher decided
    /// to pick, which is exactly the frames that fit what you happened to be doing. Data that is
    /// never selected is never seen, so a broken, mis-tagged or badly baked clip can sit in the set
    /// indefinitely. This makes the set walkable: force any clip, watch it at its authored rate, then
    /// hand control back to the matcher.
    /// </summary>
    [AddComponentMenu("Kinema/Motion Matching/Samples/Animation Browser")]
    [RequireComponent(typeof(MotionMatchingController))]
    public sealed class AnimationBrowser : MonoBehaviour
    {
        #region Public

        [Tooltip("Toggles the browser overlay.")]
        [SerializeField] private Key _toggleKey = Key.Tab;

        [SerializeField] private bool _visibleOnStart = true;

        [Tooltip("Width of the clip list panel, in pixels.")]
        [SerializeField, Range(220f, 520f)] private float _panelWidth = 320f;

        #endregion

        #region Private and Protected

        private MotionMatchingController _controller;
        private MotionQualityProbe _probe;
        private MotionMatchingDatabase _database;

        private readonly List<int> _filtered = new();
        private string[] _clipLabels;
        private string _filter = "";
        private Vector2 _scroll;
        private bool _visible;
        private int _playing = -1;

        private GUIStyle _rowStyle, _activeRowStyle, _headerStyle, _statStyle;
        private bool _stylesReady;

        #endregion

        #region Unity API

        private void Awake()
        {
            _controller = GetComponent<MotionMatchingController>();
            _probe = GetComponent<MotionQualityProbe>();
            _visible = _visibleOnStart;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_toggleKey].wasPressedThisFrame) _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible || !_controller.IsInitialized) return;
            if (_database == null) BuildLabels();
            if (!_stylesReady) BuildStyles();

            GUILayout.BeginArea(new Rect(10f, 10f, _panelWidth, Screen.height - 20f), GUI.skin.box);
            DrawStatus();
            DrawTags();
            DrawClipList();
            GUILayout.EndArea();
        }

        #endregion

        #region Tools and Utilities — Panels

        private void DrawStatus()
        {
            GUILayout.Label($"Kinema — {_database.ClipCount} clips / {_database.FrameCount:N0} frames", _headerStyle);

            bool overriding = _controller.IsOverridingClip;
            GUILayout.Label(overriding
                ? $"Playing: {ClipName(_playing)}  (matching off)"
                : $"Matched: {ClipName(_controller.CurrentClipIndex)}", _statStyle);

            GUILayout.Label($"frame {_controller.CurrentFrame}   contacts {ContactText()}   warp {_controller.CurrentStrideWarp:F2}x", _statStyle);
            if (_probe != null)
                GUILayout.Label($"foot slide {_probe.FootSlideRate:F3} m/s   cost {_probe.AverageCost:F2}   jumps/s {_probe.JumpsPerSecond:F1}", _statStyle);

            using (new GUILayout.HorizontalScope())
            {
                bool wasEnabled = GUI.enabled;
                GUI.enabled = overriding;
                if (GUILayout.Button("Prev")) Step(-1);
                if (GUILayout.Button("Next")) Step(1);
                GUI.enabled = wasEnabled;

                if (GUILayout.Button(overriding ? "Back to matching" : "Matching live"))
                {
                    _controller.StopClipOverride();
                    _playing = -1;
                }
            }

            GUILayout.Space(4f);
        }

        private void DrawTags()
        {
            if (!_database.HasTags) return;

            GUILayout.Label("Tags — require / exclude", _headerStyle);
            string[] names = _database.TagNames;

            for (int i = 0; i < names.Length; i++)
            {
                ulong mask = 1ul << i;
                bool required = (_controller.RequiredTags & mask) != 0;
                bool excluded = (_controller.ExcludedTags & mask) != 0;

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(names[i], _statStyle, GUILayout.Width(_panelWidth - 130f));

                    bool nowRequired = GUILayout.Toggle(required, "req", GUI.skin.button, GUILayout.Width(45f));
                    bool nowExcluded = GUILayout.Toggle(excluded, "excl", GUI.skin.button, GUILayout.Width(45f));

                    // Requiring and excluding the same tag would make every frame ineligible.
                    if (nowRequired != required)
                    {
                        _controller.RequiredTags = nowRequired ? _controller.RequiredTags | mask : _controller.RequiredTags & ~mask;
                        if (nowRequired) _controller.ExcludedTags &= ~mask;
                    }
                    else if (nowExcluded != excluded)
                    {
                        _controller.ExcludedTags = nowExcluded ? _controller.ExcludedTags | mask : _controller.ExcludedTags & ~mask;
                        if (nowExcluded) _controller.RequiredTags &= ~mask;
                    }
                }
            }
            GUILayout.Space(4f);
        }

        private void DrawClipList()
        {
            GUILayout.Label("Clips — click to play", _headerStyle);

            string filter = GUILayout.TextField(_filter);
            if (filter != _filter) { _filter = filter; RefreshFilter(); }

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (int index in _filtered)
            {
                bool active = _controller.IsOverridingClip ? index == _playing : index == _controller.CurrentClipIndex;
                if (GUILayout.Button(_clipLabels[index], active ? _activeRowStyle : _rowStyle))
                    Play(index);
            }
            GUILayout.EndScrollView();
        }

        #endregion

        #region Tools and Utilities

        private void Play(int clipIndex)
        {
            _playing = clipIndex;
            _controller.PlayClipOverride(clipIndex);
        }

        private void Step(int direction)
        {
            if (_filtered.Count == 0) return;
            int at = _filtered.IndexOf(_playing);
            if (at < 0) { Play(_filtered[0]); return; }
            Play(_filtered[(at + direction + _filtered.Count) % _filtered.Count]);
        }

        private void BuildLabels()
        {
            _database = _controller.Database;
            _clipLabels = new string[_database.ClipCount];

            for (int i = 0; i < _clipLabels.Length; i++)
            {
                MotionClipEntry entry = _database.GetClip(i);
                string name = !string.IsNullOrEmpty(entry.Name) ? entry.Name : $"clip {i}";
                _clipLabels[i] = $"{name}   ({entry.FrameCount} f)";
            }
            RefreshFilter();
        }

        private void RefreshFilter()
        {
            _filtered.Clear();
            for (int i = 0; i < _clipLabels.Length; i++)
                if (string.IsNullOrEmpty(_filter) || _clipLabels[i].IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    _filtered.Add(i);
        }

        private string ClipName(int clipIndex)
        {
            if (clipIndex < 0 || _clipLabels == null || clipIndex >= _clipLabels.Length) return "-";
            return _clipLabels[clipIndex];
        }

        private string ContactText()
        {
            if (!_database.HasContacts) return "n/a";
            byte contacts = _controller.CurrentContacts;
            if (contacts == 0) return "airborne";

            var text = "";
            for (int i = 0; i < _database.ContactBoneCount; i++)
                if ((contacts & (1 << i)) != 0)
                    text += (text.Length > 0 ? "+" : "") + _database.GetContactBoneName(i);
            return text;
        }

        private void BuildStyles()
        {
            _stylesReady = true;
            _headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _statStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 11 };
            _activeRowStyle = new GUIStyle(_rowStyle) { fontStyle = FontStyle.Bold };
            _activeRowStyle.normal.textColor = new Color(0.4f, 1f, 0.6f);
        }

        #endregion
    }
}
