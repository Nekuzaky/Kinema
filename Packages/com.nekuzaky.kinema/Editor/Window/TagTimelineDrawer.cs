using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// The Tags tab: a per-clip timeline where tag ranges are authored visually. One lane per tag,
    /// ranges drawn as colored blocks over the clip's duration, edited with numeric fields below.
    /// Ranges are stored on the config (<see cref="ClipTagTrack"/>) and baked into the database as
    /// per-frame 64-bit masks.
    /// </summary>
    public sealed class TagTimelineDrawer
    {
        #region Private and Protected

        private static readonly Color[] LaneColors =
        {
            new Color(0.30f, 0.72f, 1.00f), new Color(1.00f, 0.70f, 0.10f),
            new Color(0.40f, 0.85f, 0.45f), new Color(0.85f, 0.45f, 0.85f),
            new Color(1.00f, 0.42f, 0.38f), new Color(0.45f, 0.85f, 0.85f),
            new Color(0.85f, 0.85f, 0.35f), new Color(0.60f, 0.60f, 1.00f)
        };

        private int _clipIndex;
        private Vector2 _scroll;

        #endregion

        #region Main API

        public void Draw(MotionMatchingConfig config, SerializedObject serialized)
        {
            using (MotionMatchingStyles.BeginSection("Tag Vocabulary"))
            {
                SerializedProperty names = serialized.FindProperty("_tagNames");
                EditorGUILayout.PropertyField(names, new GUIContent("Tag Names (max 64)"), true);
            }

            IReadOnlyList<AnimationClip> clips = config.Clips;
            if (clips.Count == 0)
            {
                MotionMatchingStyles.HelpRow("Assign clips on the Bake tab first; then author tag ranges here.", MessageType.Info);
                return;
            }
            if (config.TagNames.Count == 0)
            {
                MotionMatchingStyles.HelpRow("Declare at least one tag name above (e.g. Strafe, Crouch, Injured).", MessageType.Info);
                return;
            }

            var clipNames = new string[clips.Count];
            for (int i = 0; i < clips.Count; i++)
                clipNames[i] = clips[i] != null ? clips[i].name : $"<missing {i}>";
            _clipIndex = Mathf.Clamp(_clipIndex, 0, clips.Count - 1);
            _clipIndex = EditorGUILayout.Popup("Clip", _clipIndex, clipNames);

            AnimationClip clip = clips[_clipIndex];
            if (clip == null) return;

            SerializedProperty trackProp = FindOrCreateTrack(config, serialized, clip);
            SerializedProperty ranges = trackProp.FindPropertyRelative("Ranges");

            using (MotionMatchingStyles.BeginSection($"Timeline — {clip.name} ({clip.length:F2}s)"))
            {
                DrawLanes(config, ranges, clip.length);
                EditorGUILayout.Space(6);
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(220));
                DrawRangeList(config, ranges, clip.length);
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("Add Range"))
                {
                    ranges.InsertArrayElementAtIndex(ranges.arraySize);
                    SerializedProperty r = ranges.GetArrayElementAtIndex(ranges.arraySize - 1);
                    r.FindPropertyRelative("TagIndex").intValue = 0;
                    r.FindPropertyRelative("Start").floatValue = 0f;
                    r.FindPropertyRelative("End").floatValue = clip.length;
                }
            }

            MotionMatchingStyles.HelpRow("Tag edits require a rebake to reach the database.", MessageType.None);
        }

        #endregion

        #region Tools and Utilities

        private static SerializedProperty FindOrCreateTrack(MotionMatchingConfig config, SerializedObject serialized, AnimationClip clip)
        {
            SerializedProperty tracks = serialized.FindProperty("_tagTracks");
            for (int i = 0; i < tracks.arraySize; i++)
            {
                SerializedProperty t = tracks.GetArrayElementAtIndex(i);
                if (t.FindPropertyRelative("Clip").objectReferenceValue == clip)
                    return t;
            }
            tracks.InsertArrayElementAtIndex(tracks.arraySize);
            SerializedProperty created = tracks.GetArrayElementAtIndex(tracks.arraySize - 1);
            created.FindPropertyRelative("Clip").objectReferenceValue = clip;
            created.FindPropertyRelative("Ranges").ClearArray();
            return created;
        }

        private static void DrawLanes(MotionMatchingConfig config, SerializedProperty ranges, float clipLength)
        {
            int tagCount = Mathf.Min(config.TagNames.Count, 64);
            for (int tag = 0; tag < tagCount; tag++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(config.TagNames[tag], MotionMatchingStyles.KeyLabel, GUILayout.Width(110));

                    Rect lane = GUILayoutUtility.GetRect(60, 16, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(lane, new Color(0f, 0f, 0f, 0.28f));

                    for (int i = 0; i < ranges.arraySize; i++)
                    {
                        SerializedProperty r = ranges.GetArrayElementAtIndex(i);
                        if (r.FindPropertyRelative("TagIndex").intValue != tag) continue;

                        float s = Mathf.Clamp01(r.FindPropertyRelative("Start").floatValue / Mathf.Max(clipLength, 1e-4f));
                        float e = Mathf.Clamp01(r.FindPropertyRelative("End").floatValue / Mathf.Max(clipLength, 1e-4f));
                        if (e <= s) continue;

                        var block = new Rect(lane.x + lane.width * s, lane.y + 2, lane.width * (e - s), lane.height - 4);
                        EditorGUI.DrawRect(block, LaneColors[tag % LaneColors.Length]);
                    }
                }
            }
        }

        private static void DrawRangeList(MotionMatchingConfig config, SerializedProperty ranges, float clipLength)
        {
            var tagNames = new string[Mathf.Min(config.TagNames.Count, 64)];
            for (int i = 0; i < tagNames.Length; i++) tagNames[i] = config.TagNames[i];

            for (int i = 0; i < ranges.arraySize; i++)
            {
                SerializedProperty r = ranges.GetArrayElementAtIndex(i);
                SerializedProperty tagIndex = r.FindPropertyRelative("TagIndex");
                SerializedProperty start = r.FindPropertyRelative("Start");
                SerializedProperty end = r.FindPropertyRelative("End");

                using (new EditorGUILayout.HorizontalScope())
                {
                    tagIndex.intValue = EditorGUILayout.Popup(tagIndex.intValue, tagNames, GUILayout.Width(110));
                    GUILayout.Label("from", MotionMatchingStyles.KeyLabel, GUILayout.Width(32));
                    start.floatValue = Mathf.Clamp(EditorGUILayout.FloatField(start.floatValue, GUILayout.Width(56)), 0f, clipLength);
                    GUILayout.Label("to", MotionMatchingStyles.KeyLabel, GUILayout.Width(18));
                    end.floatValue = Mathf.Clamp(EditorGUILayout.FloatField(end.floatValue, GUILayout.Width(56)), start.floatValue, clipLength);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(22)))
                    {
                        ranges.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
            }
        }

        #endregion
    }
}
