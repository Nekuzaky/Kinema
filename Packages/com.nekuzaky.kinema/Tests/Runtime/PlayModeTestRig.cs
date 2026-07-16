using UnityEngine;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// Builds the synthetic setup the PlayMode tests drive - a controller on a bare rig, fed by a
    /// database wrapped around a procedurally-authored clip - and steps it at a fixed timestep.
    ///
    /// Controllers are created in <see cref="MotionMatchingController.TickMode.Manual"/> so the tests
    /// own the clock: no `yield return null`, no coroutines, no dependence on batchmode's frame
    /// pacing. Every test is a plain [Test] whose timeline is exactly the Step calls it makes.
    /// </summary>
    internal sealed class PlayModeTestRig
    {
        public const int Fps = 10;
        public const int FrameCount = 30;
        public const float Dt = 1f / 30f;

        public GameObject GameObject { get; private set; }
        public MotionMatchingController Controller { get; private set; }
        public MotionMatchingDatabase Database { get; private set; }
        public AnimationClip Clip { get; private set; }

        /// <summary>
        /// A locomotion clip for the synthetic database. It must NOT animate the ROOT transform: any
        /// root curve on a clip connected to the mixer keeps that property graph-owned even at weight
        /// 0, so every Evaluate stomps transform writes made by event root-warping. Real mocap
        /// carries root motion through the Animator instead; here the curve drives the child bone.
        /// </summary>
        public static AnimationClip CreateLocomotionClip()
        {
            var clip = new AnimationClip { name = "SyntheticWalk", wrapMode = WrapMode.Loop };
            clip.SetCurve("Foot", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 3f, 1f));
            return clip;
        }

        /// <summary>Database over <paramref name="clip"/> whose frames spread along the trajectory
        /// axis, so different queries prefer different frames. Trivial normalization (mean 0, std 1).</summary>
        public static MotionMatchingDatabase CreateDatabase(AnimationClip clip)
        {
            var schema = new FeatureSchema
            {
                TrajectoryTimes = new[] { 0.2f },
                BoneNames = new[] { "Foot" },
                BoneWeights = new[] { 1f }
            };
            int dim = schema.Dimension;

            var mean = new float[dim];
            var std = new float[dim];
            for (int i = 0; i < dim; i++) std[i] = 1f;

            var features = new float[FrameCount * dim];
            var frames = new MotionFrameInfo[FrameCount];
            for (int f = 0; f < FrameCount; f++)
            {
                frames[f] = new MotionFrameInfo(0, f / (float)Fps);
                features[f * dim + schema.TrajectoryPositionOffset] = f * 0.1f;
            }

            var clips = new[]
            {
                new MotionClipEntry
                {
                    Clip = clip,
                    Name = clip != null ? clip.name : "NoClip",
                    StartFrame = 0,
                    FrameCount = FrameCount,
                    Length = clip != null ? clip.length : FrameCount / (float)Fps,
                    IsLooping = true
                }
            };

            var db = ScriptableObject.CreateInstance<MotionMatchingDatabase>();
            db.SetBakedData(schema, features, mean, std, frames, clips,
                FeatureWeights.Default, bakeFrameRate: Fps, bakeDateUtc: "playmode-test",
                totalDuration: FrameCount / (float)Fps);
            return db;
        }

        /// <summary>Character with an Animator and the schema's "Foot" bone (so the live pose query
        /// runs rather than its bone-missing fallback), a manual-tick controller, and the database
        /// assigned. AddComponent runs Awake/OnEnable synchronously in play mode, so the controller
        /// is live the moment this returns.</summary>
        public static PlayModeTestRig Create(string name = "PlayModeTestCharacter", AnimationClip clip = null)
        {
            var rig = new PlayModeTestRig();
            rig.Clip = clip != null ? clip : CreateLocomotionClip();
            rig.Database = CreateDatabase(rig.Clip);

            rig.GameObject = new GameObject(name);
            rig.GameObject.AddComponent<Animator>();
            var foot = new GameObject("Foot");
            foot.transform.SetParent(rig.GameObject.transform, false);

            rig.Controller = rig.GameObject.AddComponent<MotionMatchingController>();
            rig.Controller.Ticking = MotionMatchingController.TickMode.Manual;
            rig.Controller.SwitchDatabase(rig.Database);
            return rig;
        }

        /// <summary>Character only - shares an externally owned database (several characters, one
        /// database instance each in the batch tests).</summary>
        public static MotionMatchingController CreateCharacter(string name, MotionMatchingDatabase database)
        {
            var go = new GameObject(name);
            go.AddComponent<Animator>();
            var foot = new GameObject("Foot");
            foot.transform.SetParent(go.transform, false);

            var controller = go.AddComponent<MotionMatchingController>();
            controller.Ticking = MotionMatchingController.TickMode.Manual;
            controller.SwitchDatabase(database);
            return controller;
        }

        public void Step(int steps = 1)
        {
            for (int i = 0; i < steps; i++) Controller.Step(Dt);
        }

        public void Dispose()
        {
            if (GameObject != null) Object.DestroyImmediate(GameObject);
            if (Database != null) Object.DestroyImmediate(Database);
            if (Clip != null) Object.DestroyImmediate(Clip);
        }
    }
}
