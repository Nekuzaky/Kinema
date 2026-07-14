using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Samples.Editor
{
    /// <summary>
    /// Generates a small set of locomotion clips (idle / walk / run / turns) procedurally, directly on
    /// a rig's transforms — no external animation assets required. Root motion is authored exactly
    /// (so trajectory matching and the collision motor behave correctly), with a best-effort leg/arm
    /// swing layered on top so the pose features carry real signal.
    ///
    /// Clips are transform-curve based, so the rig must be imported as <b>Generic</b>.
    /// </summary>
    public static class ProceduralLocomotionAuthor
    {
        #region Private and Protected

        private const int Fps = 30;

        private struct BoneRef
        {
            public string Path;
            public Quaternion RestWorld;
            public Quaternion ParentRestWorld;
            public Quaternion RestLocal;
            public Vector3 RestLocalPos;
            public Vector3 RestWorldPos;
            public bool Valid;
        }

        private struct ClipSpec
        {
            public string Name;
            public float Length;
            public int Cycles;      // full leg cycles across the clip (keeps the loop seamless)
            public float Forward;   // m/s
            public float Strafe;    // m/s
            public float YawRate;   // deg/s
            public float LegAmp;
            public float KneeAmp;
            public float ArmAmp;
            public float Bob;
        }

        #endregion

        #region Main API

        public static AnimationClip[] Generate(GameObject rigPrefab, string outputFolder)
        {
            GameObject instance = Object.Instantiate(rigPrefab);
            try
            {
                Transform root = instance.transform;
                var bones = new Dictionary<string, BoneRef>();
                foreach (string key in new[] { "LeftUpLeg", "LeftLeg", "RightUpLeg", "RightLeg",
                                               "LeftArm", "LeftForeArm", "RightArm", "RightForeArm", "Hips" })
                    bones[key] = FindBone(root, key);

                var specs = new[]
                {
                    new ClipSpec { Name = "Loco_Idle",      Length = 2.0f, Cycles = 1, Forward = 0.0f, YawRate = 0f,   LegAmp = 1.5f, KneeAmp = 2f,  ArmAmp = 2f,  Bob = 0.006f },
                    new ClipSpec { Name = "Loco_WalkFwd",   Length = 1.0f, Cycles = 2, Forward = 1.5f, YawRate = 0f,   LegAmp = 22f,  KneeAmp = 26f, ArmAmp = 18f, Bob = 0.03f },
                    new ClipSpec { Name = "Loco_RunFwd",    Length = 0.7f, Cycles = 2, Forward = 4.2f, YawRate = 0f,   LegAmp = 32f,  KneeAmp = 48f, ArmAmp = 30f, Bob = 0.05f },
                    new ClipSpec { Name = "Loco_TurnLeft",  Length = 1.0f, Cycles = 2, Forward = 1.3f, YawRate = 75f,  LegAmp = 20f,  KneeAmp = 24f, ArmAmp = 16f, Bob = 0.03f },
                    new ClipSpec { Name = "Loco_TurnRight", Length = 1.0f, Cycles = 2, Forward = 1.3f, YawRate = -75f, LegAmp = 20f,  KneeAmp = 24f, ArmAmp = 16f, Bob = 0.03f },
                };

                var clips = new List<AnimationClip>();
                foreach (ClipSpec spec in specs)
                    clips.Add(BuildClip(spec, bones, outputFolder));

                AssetDatabase.SaveAssets();
                return clips.ToArray();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        #endregion

        #region Tools and Utilities

        private static AnimationClip BuildClip(ClipSpec spec, Dictionary<string, BoneRef> bones, string folder)
        {
            int frames = Mathf.RoundToInt(spec.Length * Fps);
            float dt = 1f / Fps;

            // Per-frame root motion (integrated so turns follow a proper arc).
            var rootPosX = new AnimationCurve();
            var rootPosY = new AnimationCurve();
            var rootPosZ = new AnimationCurve();
            var rootRot = new QuatCurves();

            Vector3 pos = Vector3.zero;
            float headingDeg = 0f;
            var perFrameTime = new float[frames + 1];

            for (int f = 0; f <= frames; f++)
            {
                float t = f * dt;
                perFrameTime[f] = t;

                rootPosX.AddKey(t, pos.x);
                rootPosY.AddKey(t, pos.y);
                rootPosZ.AddKey(t, pos.z);
                rootRot.Add(t, Quaternion.AngleAxis(headingDeg, Vector3.up));

                float headingRad = headingDeg * Mathf.Deg2Rad;
                Vector3 forward = new Vector3(Mathf.Sin(headingRad), 0f, Mathf.Cos(headingRad));
                Vector3 right = new Vector3(Mathf.Cos(headingRad), 0f, -Mathf.Sin(headingRad));
                pos += (forward * spec.Forward + right * spec.Strafe) * dt;
                headingDeg += spec.YawRate * dt;
            }

            var clip = new AnimationClip { frameRate = Fps };
            SetCurve(clip, "", "m_LocalPosition.x", rootPosX);
            SetCurve(clip, "", "m_LocalPosition.y", rootPosY);
            SetCurve(clip, "", "m_LocalPosition.z", rootPosZ);
            rootRot.Apply(clip, "");

            // Limb + hips-bob layer.
            BuildLimbCurves(clip, spec, bones, frames, dt);

            clip.EnsureQuaternionContinuity();
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string path = $"{folder}/{spec.Name}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                return existing;
            }
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static void BuildLimbCurves(AnimationClip clip, ClipSpec spec, Dictionary<string, BoneRef> bones, int frames, float dt)
        {
            var upLegL = new QuatCurves(); var legL = new QuatCurves();
            var upLegR = new QuatCurves(); var legR = new QuatCurves();
            var armL = new QuatCurves(); var foreArmL = new QuatCurves();
            var armR = new QuatCurves(); var foreArmR = new QuatCurves();
            var hipsPosY = new AnimationCurve();

            float omega = 2f * Mathf.PI * spec.Cycles;

            for (int f = 0; f <= frames; f++)
            {
                float u = frames > 0 ? (float)f / frames : 0f; // 0..1 across the clip
                float phaseL = omega * u;
                float phaseR = phaseL + Mathf.PI;

                AddSwing(upLegL, bones["LeftUpLeg"], spec.LegAmp * Mathf.Sin(phaseL), f * dt);
                AddSwing(upLegR, bones["RightUpLeg"], spec.LegAmp * Mathf.Sin(phaseR), f * dt);
                AddSwing(legL, bones["LeftLeg"], -spec.KneeAmp * Mathf.Clamp01(Mathf.Sin(phaseL + 2f)), f * dt);
                AddSwing(legR, bones["RightLeg"], -spec.KneeAmp * Mathf.Clamp01(Mathf.Sin(phaseR + 2f)), f * dt);

                // Arms: lowered from the T-pose to the sides, then swinging opposite to the same-side leg.
                AddArmChain(armL, foreArmL, bones["LeftArm"], bones["LeftForeArm"],
                    spec.ArmAmp * Mathf.Sin(phaseR), f * dt);
                AddArmChain(armR, foreArmR, bones["RightArm"], bones["RightForeArm"],
                    spec.ArmAmp * Mathf.Sin(phaseL), f * dt);

                if (bones["Hips"].Valid)
                    hipsPosY.AddKey(f * dt, bones["Hips"].RestLocalPos.y + spec.Bob * Mathf.Sin(2f * phaseL));
            }

            upLegL.Apply(clip, bones["LeftUpLeg"].Path); upLegR.Apply(clip, bones["RightUpLeg"].Path);
            legL.Apply(clip, bones["LeftLeg"].Path); legR.Apply(clip, bones["RightLeg"].Path);
            armL.Apply(clip, bones["LeftArm"].Path); armR.Apply(clip, bones["RightArm"].Path);
            foreArmL.Apply(clip, bones["LeftForeArm"].Path); foreArmR.Apply(clip, bones["RightForeArm"].Path);
            if (bones["Hips"].Valid) SetCurve(clip, bones["Hips"].Path, "m_LocalPosition.y", hipsPosY);
        }

        // Rotates a bone about the character's world +X (flexion) by angleDeg, expressed as a local rotation.
        private static void AddSwing(QuatCurves curves, BoneRef bone, float angleDeg, float time)
        {
            if (!bone.Valid) return;
            Quaternion desiredWorld = Quaternion.AngleAxis(angleDeg, Vector3.right) * bone.RestWorld;
            Quaternion local = Quaternion.Inverse(bone.ParentRestWorld) * desiredWorld;
            curves.Add(time, local);
        }

        private const float ArmDropDegrees = 72f;   // T-pose -> arms by the sides
        private const float ElbowBendDegrees = 14f; // slight natural elbow bend

        /// <summary>
        /// Arms need two corrections the legs don't: a constant drop from the T-pose to the body's
        /// sides (roll about the forward axis, direction from the shoulder's rest side), and a
        /// forearm whose local rotation is computed against the arm's NEW world rotation - not the
        /// rest pose - so the elbow follows the lowered shoulder.
        /// </summary>
        private static void AddArmChain(QuatCurves armCurves, QuatCurves foreCurves,
            BoneRef arm, BoneRef fore, float swingDeg, float time)
        {
            if (!arm.Valid) return;

            float side = arm.RestWorldPos.x >= 0f ? -1f : 1f; // rotate toward -Y whichever side the arm is on
            Quaternion drop = Quaternion.AngleAxis(side * ArmDropDegrees, Vector3.forward);

            Quaternion armWorld = Quaternion.AngleAxis(swingDeg, Vector3.right) * drop * arm.RestWorld;
            armCurves.Add(time, Quaternion.Inverse(arm.ParentRestWorld) * armWorld);

            if (!fore.Valid) return;
            Quaternion foreWorld = Quaternion.AngleAxis(swingDeg * 0.4f - ElbowBendDegrees, Vector3.right) * drop * fore.RestWorld;
            foreCurves.Add(time, Quaternion.Inverse(armWorld) * foreWorld);
        }

        private static BoneRef FindBone(Transform root, string suffix)
        {
            string lower = suffix.ToLowerInvariant();
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
            {
                if (!t.name.ToLowerInvariant().EndsWith(lower)) continue;
                Transform parent = t.parent != null ? t.parent : root;
                return new BoneRef
                {
                    Path = AnimationUtility.CalculateTransformPath(t, root),
                    RestWorld = t.rotation,
                    ParentRestWorld = parent.rotation,
                    RestLocal = t.localRotation,
                    RestLocalPos = t.localPosition,
                    RestWorldPos = t.position,
                    Valid = true
                };
            }
            return new BoneRef { Valid = false };
        }

        private static void SetCurve(AnimationClip clip, string path, string property, AnimationCurve curve)
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        /// <summary>Accumulates quaternion keyframes into four component curves. Reference type so it can
        /// be mutated through method calls.</summary>
        private sealed class QuatCurves
        {
            private AnimationCurve _x, _y, _z, _w;

            public void Add(float time, Quaternion q)
            {
                _x ??= new AnimationCurve(); _y ??= new AnimationCurve();
                _z ??= new AnimationCurve(); _w ??= new AnimationCurve();
                _x.AddKey(time, q.x); _y.AddKey(time, q.y); _z.AddKey(time, q.z); _w.AddKey(time, q.w);
            }

            public void Apply(AnimationClip clip, string path)
            {
                if (_x == null) return;
                SetCurve(clip, path, "m_LocalRotation.x", _x);
                SetCurve(clip, path, "m_LocalRotation.y", _y);
                SetCurve(clip, path, "m_LocalRotation.z", _z);
                SetCurve(clip, path, "m_LocalRotation.w", _w);
            }
        }

        #endregion
    }
}
