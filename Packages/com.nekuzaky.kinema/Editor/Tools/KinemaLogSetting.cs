using UnityEditor;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Persists <see cref="KinemaLog.Verbose"/> across domain reloads.
    ///
    /// Without this the toggle would look like it works and then quietly lie: entering play mode
    /// reloads the domain and resets every static, so verbose logging would switch itself off at the
    /// exact moment it was turned on for. `InitializeOnLoadMethod` runs after each reload, which is
    /// what makes the pref the source of truth rather than the field.
    /// </summary>
    public static class KinemaLogSetting
    {
        private const string Key = "Kinema.VerboseLogging";

        public static bool Verbose
        {
            get => EditorPrefs.GetBool(Key, false);
            set
            {
                EditorPrefs.SetBool(Key, value);
                KinemaLog.Verbose = value;
            }
        }

        [InitializeOnLoadMethod]
        private static void Apply() => KinemaLog.Verbose = EditorPrefs.GetBool(Key, false);
    }
}
