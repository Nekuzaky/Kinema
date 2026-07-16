using UnityEngine;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// One channel for everything Kinema says, off by default.
    ///
    /// Two rules, and they are what make a log worth reading:
    ///
    /// <b>Log transitions, never state.</b> "The vault fired", "the AI changed goal", "the masks
    /// resolved to this" - things that happened once, at a moment, for a reason. What the character
    /// is doing right now is state; it belongs in the diagnostics report and on the stats label,
    /// where it can be read without being drowned in. A line printed every frame is not information,
    /// it is a denial of service against the one line that mattered.
    ///
    /// <b>Off by default.</b> Every call still costs a string to build and a stack trace to capture,
    /// per character, whether or not anyone is reading - so the guard is on the caller's side, and
    /// the message is only ever built when someone asked for it.
    ///
    /// Turn it on in the window's Settings tab, or set <see cref="Verbose"/> from code.
    /// </summary>
    public static class KinemaLog
    {
        #region Public

        /// <summary>
        /// Whether the channel prints. Set from the window's Settings tab, which persists it and
        /// re-applies it after a domain reload, so it survives entering play mode.
        /// </summary>
        public static bool Verbose;

        /// <summary>A thing that happened. Guard the call site with <see cref="Verbose"/> when the message costs anything to build.</summary>
        public static void Event(string message, Object context = null)
        {
            if (Verbose) Debug.Log("[Kinema] " + message, context);
        }

        /// <summary>
        /// Something is set up wrong and will silently do nothing. Always prints, verbose or not: a
        /// warning nobody sees is the failure it was warning about.
        /// </summary>
        public static void Misconfigured(string message, Object context = null) =>
            Debug.LogWarning("[Kinema] " + message, context);

        #endregion
    }
}
