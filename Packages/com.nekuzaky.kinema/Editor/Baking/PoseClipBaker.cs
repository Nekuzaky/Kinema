using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Turns a recorded <see cref="PoseTake"/> into a real <see cref="AnimationClip"/> asset.
    ///
    /// The result is a transform-curve (Generic) clip: it keys the root's position/rotation and each
    /// bone's local rotation directly. That is what makes it an exact record of the performance, and
    /// also what limits it - a Humanoid Animator ignores transform curves in favour of muscle data,
    /// so the clip plays back on a rig read as Generic (an Animator with no avatar, the Animation
    /// window, or Timeline). <see cref="BakeReport"/> callers get told this rather than discovering it
    /// on an empty playback.
    /// </summary>
    public static class PoseClipBaker
    {
        #region Main API

        /// <summary>Writes the take to an AnimationClip asset at <paramref name="path"/>, overwriting in place.</summary>
        public static AnimationClip Bake(PoseTake take, string path, bool loop = false)
        {
            if (take == null || !take.IsValid)
            {
                Debug.LogError("[Kinema] Cannot bake an empty take.");
                return null;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool isNew = clip == null;
            if (isNew) clip = new AnimationClip();
            else clip.ClearCurves();

            clip.name = System.IO.Path.GetFileNameWithoutExtension(path);
            clip.legacy = false;

            WriteRootCurves(clip, take);
            WriteBoneCurves(clip, take);

            // Keys come from wall-clock samples, so they are not on a fixed grid; quaternion keys can
            // also flip sign between frames and interpolate the long way round without this.
            clip.EnsureQuaternionContinuity();

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (isNew) AssetDatabase.CreateAsset(clip, path);
            else EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Kinema] Baked take → {path}: {take.FrameCount} frames, {take.Duration:F1}s, " +
                      $"{take.BoneCount} bones, {take.DistanceTravelled():F1} m travelled. " +
                      "Transform-curve clip: play it on a rig read as Generic (Animator with no avatar, " +
                      "Animation window, or Timeline); a Humanoid Animator ignores transform curves.");
            return clip;
        }

        #endregion

        #region Tools and Utilities

        private static void WriteRootCurves(AnimationClip clip, PoseTake take)
        {
            int frames = take.FrameCount;
            var px = new AnimationCurve(); var py = new AnimationCurve(); var pz = new AnimationCurve();
            var rx = new AnimationCurve(); var ry = new AnimationCurve();
            var rz = new AnimationCurve(); var rw = new AnimationCurve();

            for (int f = 0; f < frames; f++)
            {
                float t = take.Times[f];
                Vector3 p = take.RootPositions[f];
                Quaternion r = take.RootRotations[f];

                px.AddKey(t, p.x); py.AddKey(t, p.y); pz.AddKey(t, p.z);
                rx.AddKey(t, r.x); ry.AddKey(t, r.y); rz.AddKey(t, r.z); rw.AddKey(t, r.w);
            }

            clip.SetCurve("", typeof(Transform), "localPosition.x", px);
            clip.SetCurve("", typeof(Transform), "localPosition.y", py);
            clip.SetCurve("", typeof(Transform), "localPosition.z", pz);
            clip.SetCurve("", typeof(Transform), "localRotation.x", rx);
            clip.SetCurve("", typeof(Transform), "localRotation.y", ry);
            clip.SetCurve("", typeof(Transform), "localRotation.z", rz);
            clip.SetCurve("", typeof(Transform), "localRotation.w", rw);
        }

        private static void WriteBoneCurves(AnimationClip clip, PoseTake take)
        {
            int frames = take.FrameCount;
            int bones = take.BoneCount;

            for (int b = 0; b < bones; b++)
            {
                var rx = new AnimationCurve(); var ry = new AnimationCurve();
                var rz = new AnimationCurve(); var rw = new AnimationCurve();

                for (int f = 0; f < frames; f++)
                {
                    float t = take.Times[f];
                    Quaternion r = take.GetBoneRotation(f, b);
                    rx.AddKey(t, r.x); ry.AddKey(t, r.y); rz.AddKey(t, r.z); rw.AddKey(t, r.w);
                }

                string path = take.BonePaths[b];
                clip.SetCurve(path, typeof(Transform), "localRotation.x", rx);
                clip.SetCurve(path, typeof(Transform), "localRotation.y", ry);
                clip.SetCurve(path, typeof(Transform), "localRotation.z", rz);
                clip.SetCurve(path, typeof(Transform), "localRotation.w", rw);
            }
        }

        #endregion
    }
}
