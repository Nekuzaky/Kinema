using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Writes <see cref="GaitClassifier"/> suggestions into a <see cref="MotionMatchingConfig"/> as
    /// real <see cref="ClipTagTrack"/> ranges - the "accept" half of motion-based auto-tagging
    /// (<see cref="AutoTagSuggestions"/> is the "look" half). Goes through the same SerializedObject
    /// path the Tags tab uses, so undo works and the asset dirties correctly. Tag names
    /// ("Idle"/"Walk"/"Run"/"Turn") are created in the config's vocabulary if absent; a Turning
    /// range writes both its gait tag and "Turn" over the same span.
    /// </summary>
    public static class AutoTagApplier
    {
        public const string TurnTagName = "Turn";

        /// <summary>
        /// Applies <paramref name="ranges"/> (clip indices resolved through
        /// <paramref name="database"/>) onto <paramref name="config"/>. Existing ranges on each
        /// touched clip's track are replaced - auto-tagging a clip twice must not stack duplicates;
        /// clips the suggestions never mention keep their hand-authored tracks untouched. Returns
        /// the number of tag ranges written.
        /// </summary>
        public static int Apply(MotionMatchingConfig config, MotionMatchingDatabase database, IReadOnlyList<GaitClassifier.Range> ranges)
        {
            if (config == null || database == null || ranges == null || ranges.Count == 0) return 0;

            var serialized = new SerializedObject(config);
            SerializedProperty tagNames = serialized.FindProperty("_tagNames");
            SerializedProperty tracks = serialized.FindProperty("_tagTracks");

            int written = 0;
            var clearedClips = new HashSet<AnimationClip>();

            foreach (GaitClassifier.Range range in ranges)
            {
                AnimationClip clip = database.GetClip(range.ClipIndex).Clip;
                if (clip == null) continue; // synthetic entries carry no clip; nothing to tag.

                SerializedProperty track = FindOrCreateTrack(tracks, clip);
                SerializedProperty trackRanges = track.FindPropertyRelative("Ranges");
                if (clearedClips.Add(clip)) trackRanges.ClearArray();

                AppendRange(trackRanges, EnsureTagIndex(tagNames, range.Gait.ToString()), range);
                written++;
                if (range.Turning)
                {
                    AppendRange(trackRanges, EnsureTagIndex(tagNames, TurnTagName), range);
                    written++;
                }
            }

            serialized.ApplyModifiedProperties();
            return written;
        }

        private static void AppendRange(SerializedProperty trackRanges, int tagIndex, GaitClassifier.Range range)
        {
            trackRanges.InsertArrayElementAtIndex(trackRanges.arraySize);
            SerializedProperty r = trackRanges.GetArrayElementAtIndex(trackRanges.arraySize - 1);
            r.FindPropertyRelative("TagIndex").intValue = tagIndex;
            r.FindPropertyRelative("Start").floatValue = range.StartTime;
            r.FindPropertyRelative("End").floatValue = range.EndTime;
        }

        /// <summary>Index of <paramref name="name"/> in the config's tag vocabulary, adding it if
        /// missing. Returns -1 (range will be inert) when the vocabulary is already at the 64-tag
        /// mask limit rather than silently corrupting bit indices.</summary>
        private static int EnsureTagIndex(SerializedProperty tagNames, string name)
        {
            for (int i = 0; i < tagNames.arraySize; i++)
                if (tagNames.GetArrayElementAtIndex(i).stringValue == name)
                    return i;

            if (tagNames.arraySize >= 64) return -1;
            tagNames.InsertArrayElementAtIndex(tagNames.arraySize);
            tagNames.GetArrayElementAtIndex(tagNames.arraySize - 1).stringValue = name;
            return tagNames.arraySize - 1;
        }

        private static SerializedProperty FindOrCreateTrack(SerializedProperty tracks, AnimationClip clip)
        {
            for (int i = 0; i < tracks.arraySize; i++)
            {
                SerializedProperty t = tracks.GetArrayElementAtIndex(i);
                if (t.FindPropertyRelative("Clip").objectReferenceValue == clip) return t;
            }

            tracks.InsertArrayElementAtIndex(tracks.arraySize);
            SerializedProperty created = tracks.GetArrayElementAtIndex(tracks.arraySize - 1);
            created.FindPropertyRelative("Clip").objectReferenceValue = clip;
            created.FindPropertyRelative("Ranges").ClearArray();
            return created;
        }
    }
}
