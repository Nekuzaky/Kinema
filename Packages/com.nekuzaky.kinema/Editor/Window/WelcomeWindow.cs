using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// First-run popup: opens once per installed version, points at the docs, briefs the two menu
    /// entry points, and links out. Auto-open is tracked in <c>EditorPrefs</c> (project-wide, not
    /// per-user Application.persistentDataPath) keyed by version, so upgrading the package reopens
    /// it once - a good moment to notice something changed - while staying quiet on every other
    /// domain reload.
    /// </summary>
    [InitializeOnLoad]
    public static class WelcomeWindowLauncher
    {
        private const string DocsUrl = "https://github.com/Nekuzaky/Kinema/wiki";
        private const string SponsorsUrl = "https://github.com/sponsors/Nekuzaky";
        private const string WebsiteUrl = "https://www.nekuzaky.com";
        private const string ShownVersionPrefKey = "Kinema.WelcomeShownVersion";

        static WelcomeWindowLauncher()
        {
            // Deferred: UnityEditor.PackageManager.PackageInfo lookup and window creation are unreliable inside the static
            // constructor during a domain reload triggered mid-import.
            EditorApplication.delayCall += ShowIfNewVersion;
        }

        private static void ShowIfNewVersion()
        {
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WelcomeWindowLauncher).Assembly);
            if (package == null) return;

            string shown = EditorPrefs.GetString(ShownVersionPrefKey, "");
            if (shown == package.version) return;

            EditorPrefs.SetString(ShownVersionPrefKey, package.version);
            WelcomeWindow.Open(package.version);
        }

        [MenuItem("Tools/Kinema/Welcome", priority = 100)]
        private static void OpenFromMenu()
        {
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WelcomeWindowLauncher).Assembly);
            WelcomeWindow.Open(package != null ? package.version : "");
        }

        internal static void OpenDocs() => Application.OpenURL(DocsUrl);
        internal static void OpenSponsors() => Application.OpenURL(SponsorsUrl);
        internal static void OpenWebsite() => Application.OpenURL(WebsiteUrl);
    }

    /// <summary>The popup itself: brief, three link buttons, close.</summary>
    public sealed class WelcomeWindow : EditorWindow
    {
        #region Main API

        internal static void Open(string version)
        {
            var window = GetWindow<WelcomeWindow>(utility: true, title: "Kinema Motion Matching");
            window._version = version;
            window.minSize = new Vector2(380, 360);
            window.maxSize = window.minSize;
            window.ShowUtility();
        }

        #endregion

        #region Private and Protected

        private string _version = "";
        private GUIStyle _title, _body, _step;

        #endregion

        #region Unity API

        private void OnGUI()
        {
            EnsureStyles();

            GUILayout.Space(10);
            GUILayout.Label("Thanks for using Kinema Motion Matching", _title);
            if (!string.IsNullOrEmpty(_version))
                GUILayout.Label("v" + _version, EditorStyles.miniLabel);

            GUILayout.Space(8);
            GUILayout.Label(
                "Data-driven motion matching locomotion for Unity: bake AnimationClips into a " +
                "searchable database, then match, blend and debug them live from one editor window.\n\n" +
                "Built and maintained by one developer - if it saves you time, a sponsorship keeps it moving.",
                _body);

            GUILayout.Space(12);
            GUILayout.Label("Quick start", EditorStyles.boldLabel);
            GUILayout.Label("1.  Tools > Kinema > Motion Matching Window (Ctrl+Shift+M) - bake a database.", _step);
            GUILayout.Label("2.  Or Tools > Kinema > Demo Scene - one click, fully wired scene.", _step);
            GUILayout.Label("3.  Director tab: play, record and direct ghosts on the live character.", _step);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Open Documentation", GUILayout.Height(28)))
                WelcomeWindowLauncher.OpenDocs();

            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("♥ Sponsor", GUILayout.Height(24)))
                    WelcomeWindowLauncher.OpenSponsors();
                if (GUILayout.Button("Website", GUILayout.Height(24)))
                    WelcomeWindowLauncher.OpenWebsite();
            }

            GUILayout.Space(8);
            if (GUILayout.Button("Close", GUILayout.Height(22)))
                Close();

            GUILayout.Space(10);
        }

        #endregion

        #region Tools and Utilities

        private void EnsureStyles()
        {
            _title ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            _body ??= new GUIStyle(EditorStyles.wordWrappedLabel);
            _step ??= new GUIStyle(EditorStyles.label) { wordWrap = true, margin = new RectOffset(4, 4, 1, 1) };
        }

        #endregion
    }
}
