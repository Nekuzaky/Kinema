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
                foreach (string key in new[] { "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot",
                                               "LeftArm", "LeftForeArm", "RightArm", "RightForeArm", "Hips", "Spine" })
                    bones[key] = FindBone(root, key);

                // Human cadence: one full leg cycle = two steps. Walk ~1.7 steps/s, run ~3 steps/s.
                var specs = new[]
                {
                    new ClipSpec { Name = "Loco_Idle",      Length = 2.0f,  Cycles = 1, Forward = 0.0f, YawRate = 0f,   LegAmp = 1.5f, KneeAmp = 2f,  ArmAmp = 2f,  Bob = 0.006f },
                    new ClipSpec { Name = "Loco_WalkFwd",   Length = 1.15f, Cycles = 1, Forward = 1.4f, YawRate = 0f,   LegAmp = 26f,  KneeAmp = 32f, ArmAmp = 14f, Bob = 0.025f },
                    new ClipSpec { Name = "Loco_RunFwd",    Length = 0.7f,  Cycles = 1, Forward = 4.0f, YawRate = 0f,   LegAmp = 38f,  KneeAmp = 55f, ArmAmp = 28f, Bob = 0.045f },
                    new ClipSpec { Name = "Loco_TurnLeft",  Length = 1.15f, Cycles = 1, Forward = 1.2f, YawRate = 75f,  LegAmp = 24f,  KneeAmp = 30f, ArmAmp = 12f, Bob = 0.025f },
                    new ClipSpec { Name = "Loco_TurnRight", Length = 1.15f, Cycles = 1, Forward = 1.2f, YawRate = -75f, LegAmp = 24f,  KneeAmp = 30f, ArmAmp = 12f, Bob = 0.025f },
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

        /// <summary>
        /// Generates the one-shot vault clip used by the demo's motion event: the root rises in an
        /// arc over ~1.1 m while travelling 2.2 m forward, both legs tuck at the apex, the arms swing
        /// forward to plant on the edge and the torso folds. Non-looping; played through
        /// <see cref="MotionEventDefinition"/> with warping, never part of the matching database.
        /// </summary>
        public static AnimationClip GenerateVault(GameObject rigPrefab, string outputFolder)
        {
            GameObject instance = Object.Instantiate(rigPrefab);
            try
            {
                Transform root = instance.transform;
                var bones = new Dictionary<string, BoneRef>();
                foreach (string key in new[] { "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot",
                                               "LeftArm", "LeftForeArm", "RightArm", "RightForeArm", "Hips", "Spine" })
                    bones[key] = FindBone(root, key);

                const float length = 0.9f;
                const float travel = 2.2f;   // total forward root motion
                const float apex = 1.1f;     // peak root height over the obstacle
                int frames = Mathf.RoundToInt(length * Fps);
                float dt = 1f / Fps;

                var posX = new AnimationCurve(); var posY = new AnimationCurve(); var posZ = new AnimationCurve();
                var rootRot = new QuatCurves();
                var upLegL = new QuatCurves(); var legL = new QuatCurves(); var footL = new QuatCurves();
                var upLegR = new QuatCurves(); var legR = new QuatCurves(); var footR = new QuatCurves();
                var armL = new QuatCurves(); var foreArmL = new QuatCurves();
                var armR = new QuatCurves(); var foreArmR = new QuatCurves();
                var spineRot = new QuatCurves();

                for (int f = 0; f <= frames; f++)
                {
                    float u = frames > 0 ? (float)f / frames : 0f;
                    float t = f * dt;

                    posX.AddKey(t, 0f);
                    posZ.AddKey(t, travel * u);
                    // Airborne arc between 15% and 85% of the clip.
                    float a = Mathf.Clamp01((u - 0.15f) / 0.7f);
                    posY.AddKey(t, apex * Mathf.Sin(Mathf.PI * a));
                    rootRot.Add(t, Quaternion.identity);

                    // Both legs tuck hard at the apex.
                    float tuck = Bell(u, 0.5f, 0.22f);
                    AddLegChain(upLegL, legL, footL, bones["LeftUpLeg"], bones["LeftLeg"], bones["LeftFoot"],
                        -55f * tuck, -95f * tuck, t);
                    AddLegChain(upLegR, legR, footR, bones["RightUpLeg"], bones["RightLeg"], bones["RightFoot"],
                        -65f * tuck, -100f * tuck, t);

                    // Arms swing forward-down to plant on the edge just before the apex.
                    float plant = Bell(u, 0.35f, 0.18f);
                    AddArmChain(armL, foreArmL, bones["LeftArm"], bones["LeftForeArm"], 45f * plant, t);
                    AddArmChain(armR, foreArmR, bones["RightArm"], bones["RightForeArm"], 50f * plant, t);

                    // Torso folds over the obstacle.
                    if (bones["Spine"].Valid)
                    {
                        Quaternion spineWorld = Quaternion.AngleAxis(28f * Bell(u, 0.45f, 0.28f), Vector3.right) * bones["Spine"].RestWorld;
                        spineRot.Add(t, Quaternion.Inverse(bones["Spine"].ParentRestWorld) * spineWorld);
                    }
                }

                var clip = new AnimationClip { frameRate = Fps };
                SetCurve(clip, "", "m_LocalPosition.x", posX);
                SetCurve(clip, "", "m_LocalPosition.y", posY);
                SetCurve(clip, "", "m_LocalPosition.z", posZ);
                rootRot.Apply(clip, "");
                upLegL.Apply(clip, bones["LeftUpLeg"].Path); legL.Apply(clip, bones["LeftLeg"].Path); footL.Apply(clip, bones["LeftFoot"].Path);
                upLegR.Apply(clip, bones["RightUpLeg"].Path); legR.Apply(clip, bones["RightLeg"].Path); footR.Apply(clip, bones["RightFoot"].Path);
                armL.Apply(clip, bones["LeftArm"].Path); foreArmL.Apply(clip, bones["LeftForeArm"].Path);
                armR.Apply(clip, bones["RightArm"].Path); foreArmR.Apply(clip, bones["RightForeArm"].Path);
                if (bones["Spine"].Valid) spineRot.Apply(clip, bones["Spine"].Path);

                clip.EnsureQuaternionContinuity();
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                string path = $"{outputFolder}/Event_Vault.anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(clip, existing);
                    AssetDatabase.SaveAssets();
                    return existing;
                }
                AssetDatabase.CreateAsset(clip, path);
                AssetDatabase.SaveAssets();
                return clip;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static float Bell(float u, float center, float width)
        {
            float d = (u - center) / width;
            return Mathf.Exp(-d * d);
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
            var upLegL = new QuatCurves(); var legL = new QuatCurves(); var footL = new QuatCurves();
            var upLegR = new QuatCurves(); var legR = new QuatCurves(); var footR = new QuatCurves();
            var armL = new QuatCurves(); var foreArmL = new QuatCurves();
            var armR = new QuatCurves(); var foreArmR = new QuatCurves();
            var hipsRot = new QuatCurves(); var spineRot = new QuatCurves();
            var hipsPosY = new AnimationCurve();

            float omega = 2f * Mathf.PI * spec.Cycles;
            bool moving = Mathf.Abs(spec.Forward) > 0.1f;
            float lean = moving ? Mathf.Lerp(3f, 9f, Mathf.InverseLerp(1f, 4.5f, Mathf.Abs(spec.Forward))) : 0f;

            for (int f = 0; f <= frames; f++)
            {
                float u = frames > 0 ? (float)f / frames : 0f; // 0..1 across the clip
                float t = f * dt;
                float phaseL = omega * u;
                float phaseR = phaseL + Mathf.PI;

                AddLegChain(upLegL, legL, footL, bones["LeftUpLeg"], bones["LeftLeg"], bones["LeftFoot"],
                    spec.LegAmp * Mathf.Sin(phaseL), -spec.KneeAmp * Mathf.Clamp01(Mathf.Sin(phaseL + 2f)), t);
                AddLegChain(upLegR, legR, footR, bones["RightUpLeg"], bones["RightLeg"], bones["RightFoot"],
                    spec.LegAmp * Mathf.Sin(phaseR), -spec.KneeAmp * Mathf.Clamp01(Mathf.Sin(phaseR + 2f)), t);

                // Arms: lowered from the T-pose to the sides, then swinging opposite to the same-side leg.
                AddArmChain(armL, foreArmL, bones["LeftArm"], bones["LeftForeArm"], spec.ArmAmp * Mathf.Sin(phaseR), t);
                AddArmChain(armR, foreArmR, bones["RightArm"], bones["RightForeArm"], spec.ArmAmp * Mathf.Sin(phaseL), t);

                // Pelvis: vertical bob (two per cycle) plus a slight yaw sway following the stride.
                if (bones["Hips"].Valid)
                {
                    hipsPosY.AddKey(t, bones["Hips"].RestLocalPos.y + spec.Bob * Mathf.Sin(2f * phaseL));
                    float sway = moving ? 5f * Mathf.Sin(phaseL) : 0f;
                    Quaternion hipsWorld = Quaternion.AngleAxis(sway, Vector3.up) * bones["Hips"].RestWorld;
                    hipsRot.Add(t, Quaternion.Inverse(bones["Hips"].ParentRestWorld) * hipsWorld);
                }

                // Torso: constant forward lean scaled by speed, counter-swaying the pelvis a touch.
                if (bones["Spine"].Valid)
                {
                    float counterSway = moving ? -3f * Mathf.Sin(phaseL) : 0f;
                    Quaternion spineWorld = Quaternion.AngleAxis(lean, Vector3.right)
                                          * Quaternion.AngleAxis(counterSway, Vector3.up)
                                          * bones["Spine"].RestWorld;
                    spineRot.Add(t, Quaternion.Inverse(bones["Spine"].ParentRestWorld) * spineWorld);
                }
            }

            upLegL.Apply(clip, bones["LeftUpLeg"].Path); upLegR.Apply(clip, bones["RightUpLeg"].Path);
            legL.Apply(clip, bones["LeftLeg"].Path); legR.Apply(clip, bones["RightLeg"].Path);
            footL.Apply(clip, bones["LeftFoot"].Path); footR.Apply(clip, bones["RightFoot"].Path);
            armL.Apply(clip, bones["LeftArm"].Path); armR.Apply(clip, bones["RightArm"].Path);
            foreArmL.Apply(clip, bones["LeftForeArm"].Path); foreArmR.Apply(clip, bones["RightForeArm"].Path);
            if (bones["Hips"].Valid)
            {
                SetCurve(clip, bones["Hips"].Path, "m_LocalPosition.y", hipsPosY);
                hipsRot.Apply(clip, bones["Hips"].Path);
            }
            if (bones["Spine"].Valid) spineRot.Apply(clip, bones["Spine"].Path);
        }

        /// <summary>
        /// Leg chain with proper composition: the lower leg's local rotation is computed against the
        /// upper leg's NEW world rotation, and the foot counter-rotates to stay roughly level with
        /// the ground instead of following the shin's tilt (kills the toe-dragging look).
        /// </summary>
        private static void AddLegChain(QuatCurves upCurves, QuatCurves lowCurves, QuatCurves footCurves,
            BoneRef up, BoneRef low, BoneRef foot, float hipSwingDeg, float kneeDeg, float time)
        {
            if (!up.Valid || !low.Valid) return;

            Quaternion upWorld = Quaternion.AngleAxis(hipSwingDeg, Vector3.right) * up.RestWorld;
            upCurves.Add(time, Quaternion.Inverse(up.ParentRestWorld) * upWorld);

            Quaternion lowWorld = Quaternion.AngleAxis(hipSwingDeg + kneeDeg, Vector3.right) * low.RestWorld;
            lowCurves.Add(time, Quaternion.Inverse(upWorld) * lowWorld);

            if (!foot.Valid) return;
            // Foot: mostly level, with a light heel-strike/toe-off pitch following the hip swing.
            Quaternion footWorld = Quaternion.AngleAxis(hipSwingDeg * 0.25f, Vector3.right) * foot.RestWorld;
            footCurves.Add(time, Quaternion.Inverse(lowWorld) * footWorld);
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
