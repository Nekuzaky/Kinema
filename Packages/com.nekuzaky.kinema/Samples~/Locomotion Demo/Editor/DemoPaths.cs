using System.IO;
using UnityEditor;

namespace Kinema.MotionMatching.Samples.Editor
{
    /// <summary>
    /// Resolves every demo asset path relative to wherever this sample was imported, so the demo
    /// works whether it lives under <c>Assets/Samples/…</c>, a custom folder, or anywhere else.
    /// The root is discovered from this script's own location rather than hard-coded.
    /// </summary>
    internal static class DemoPaths
    {
        #region Private and Protected

        private static string _root;

        #endregion

        #region Main API

        /// <summary>Folder that contains the sample (the parent of the Editor folder holding these scripts).</summary>
        internal static string SampleRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(_root) && AssetDatabase.IsValidFolder(_root))
                    return _root;

                foreach (string guid in AssetDatabase.FindAssets("t:MonoScript DemoPaths"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith("/DemoPaths.cs")) continue;

                    string editorDir = Normalize(Path.GetDirectoryName(path));   // …/Locomotion Demo/Editor
                    _root = Normalize(Path.GetDirectoryName(editorDir));         // …/Locomotion Demo
                    return _root;
                }
                return "Assets";
            }
        }

        internal static string Materials => SampleRoot + "/Materials";
        internal static string Character => SampleRoot + "/Character";
        internal static string Animations => Character + "/Animations";
        internal static string Generated => Animations + "/Generated";
        internal static string ScenePath => SampleRoot + "/KinemaDemo.unity";
        internal static string ConfigPath => SampleRoot + "/KinemaDemoConfig.asset";
        internal static string DatabasePath => SampleRoot + "/KinemaDemoConfigDatabase.asset";

        #endregion

        #region Tools and Utilities

        private static string Normalize(string path) => path.Replace("\\", "/");

        #endregion
    }
}
