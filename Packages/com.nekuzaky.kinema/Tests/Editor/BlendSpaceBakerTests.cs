using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Kinema.MotionMatching.Editor;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// End-to-end coverage of <see cref="BlendSpaceBaker"/>: builds a rig and two source clips that
    /// differ in a way the maths can be checked against, bakes the grid, and reads the produced
    /// clips' curves back to prove each grid point really is a blend of its neighbours - the claim
    /// that matters, since a grid point exists only to be playable AND matchable.
    ///
    /// Runs against real AnimationClip assets on disk (the baker writes assets by design); everything
    /// is created under a temp folder and deleted in teardown.
    /// </summary>
    public sealed class BlendSpaceBakerTests
    {
        private const string TempFolder = "Assets/__BlendSpaceBakerTests";

        private GameObject _rig;
        private AnimationClip _left;
        private AnimationClip _right;
        private MotionMatchingBlendSpace _blendSpace;

        [SetUp]
        public void SetUp()
        {
            // Rig: root + one bone, so the baker has a bone path to key.
            _rig = new GameObject("TestRig");
            var bone = new GameObject("Bone");
            bone.transform.SetParent(_rig.transform, false);

            // Two sources that differ only in the bone's yaw: 0 deg vs 90 deg, held for 1 s. A blend
            // of the two is then a pure, checkable interpolation of that angle.
            _left = CreateYawClip("Left", 0f);
            _right = CreateYawClip("Right", 90f);

            _blendSpace = ScriptableObject.CreateInstance<MotionMatchingBlendSpace>();
            SetPrivateField(_blendSpace, "_name", "TestSpace");
            SetPrivateField(_blendSpace, "_gridResolution", new Vector2Int(3, 1));
            SetPrivateField(_blendSpace, "_entries", new List<MotionMatchingBlendSpace.Entry>
            {
                new MotionMatchingBlendSpace.Entry { Clip = _left,  Position = new Vector2(-1f, 0f) },
                new MotionMatchingBlendSpace.Entry { Clip = _right, Position = new Vector2( 1f, 0f) }
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (_rig != null) Object.DestroyImmediate(_rig);
            if (_left != null) Object.DestroyImmediate(_left);
            if (_right != null) Object.DestroyImmediate(_right);
            if (_blendSpace != null) Object.DestroyImmediate(_blendSpace);
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
        }

        private static AnimationClip CreateYawClip(string name, float yawDegrees)
        {
            var clip = new AnimationClip { name = name };
            Quaternion q = Quaternion.Euler(0f, yawDegrees, 0f);
            clip.SetCurve("Bone", typeof(Transform), "localRotation.x", AnimationCurve.Constant(0f, 1f, q.x));
            clip.SetCurve("Bone", typeof(Transform), "localRotation.y", AnimationCurve.Constant(0f, 1f, q.y));
            clip.SetCurve("Bone", typeof(Transform), "localRotation.z", AnimationCurve.Constant(0f, 1f, q.z));
            clip.SetCurve("Bone", typeof(Transform), "localRotation.w", AnimationCurve.Constant(0f, 1f, q.w));
            return clip;
        }

        private static void SetPrivateField(Object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, $"expected serialized field '{fieldName}' on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        /// <summary>The Bone's yaw a baked clip holds, read back off its curves at t = 0.5 s.</summary>
        private static float ReadBoneYaw(AnimationClip clip)
        {
            var rotation = new Quaternion(
                Evaluate(clip, "m_LocalRotation.x"),
                Evaluate(clip, "m_LocalRotation.y"),
                Evaluate(clip, "m_LocalRotation.z"),
                Evaluate(clip, "m_LocalRotation.w"));
            rotation.Normalize();

            float yaw = rotation.eulerAngles.y;
            return yaw > 180f ? yaw - 360f : yaw;
        }

        /// <summary>Property names here are the serialized ones ("m_LocalRotation.x"), not the
        /// shorthand SetCurve accepts ("localRotation.x") - SetCurve maps to the former, and
        /// GetEditorCurve only answers to it.</summary>
        private static float Evaluate(AnimationClip clip, string property)
        {
            var binding = EditorCurveBinding.FloatCurve("Bone", typeof(Transform), property);
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            Assert.IsNotNull(curve, $"baked clip is missing the '{property}' curve on Bone");
            return curve.Evaluate(0.5f);
        }

        [Test]
        public void Bake_ProducesOneClipPerGridPoint()
        {
            BlendSpaceBaker.Result result = BlendSpaceBaker.Bake(_blendSpace, _rig, TempFolder);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(3, result.Clips.Count, "a 3x1 grid must produce three clips");
            foreach (AnimationClip clip in result.Clips)
                Assert.IsNotNull(clip);
        }

        [Test]
        public void Bake_GridEndsMatchTheirSourceClips_AndTheMiddleIsTheBlend()
        {
            BlendSpaceBaker.Result result = BlendSpaceBaker.Bake(_blendSpace, _rig, TempFolder);
            Assert.IsTrue(result.Success, result.Error);

            // Grid runs left to right across the entries' bounding box: -1, 0, +1.
            float leftYaw = ReadBoneYaw(result.Clips[0]);
            float middleYaw = ReadBoneYaw(result.Clips[1]);
            float rightYaw = ReadBoneYaw(result.Clips[2]);

            Assert.AreEqual(0f, leftYaw, 1f, "the grid point sitting on the Left entry must reproduce it");
            Assert.AreEqual(90f, rightYaw, 1f, "the grid point sitting on the Right entry must reproduce it");
            Assert.AreEqual(45f, middleYaw, 1.5f,
                "the midpoint must be a real pose blend of the two sources - the whole reason grid clips exist");
        }

        [Test]
        public void Bake_ClipsArePlayable_NotJustData()
        {
            BlendSpaceBaker.Result result = BlendSpaceBaker.Bake(_blendSpace, _rig, TempFolder);
            Assert.IsTrue(result.Success, result.Error);

            // A grid clip must be a real asset with curves and length - if this fails the point is
            // matchable but unplayable, which is exactly the gap this baker was written to close.
            AnimationClip middle = result.Clips[1];
            Assert.IsFalse(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(middle)), "grid clips must be saved assets");
            Assert.Greater(middle.length, 0f, "a grid clip with no length cannot be played");
            Assert.Greater(AnimationUtility.GetCurveBindings(middle).Length, 0, "a grid clip with no curves poses nothing");
        }

        [Test]
        public void Bake_RejectsMissingRig()
        {
            BlendSpaceBaker.Result result = BlendSpaceBaker.Bake(_blendSpace, null, TempFolder);
            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.Error);
        }

        [Test]
        public void Bake_RejectsBlendSpaceWithNoEntries()
        {
            var empty = ScriptableObject.CreateInstance<MotionMatchingBlendSpace>();
            try
            {
                BlendSpaceBaker.Result result = BlendSpaceBaker.Bake(empty, _rig, TempFolder);
                Assert.IsFalse(result.Success);
                Assert.IsNotEmpty(result.Error);
            }
            finally
            {
                Object.DestroyImmediate(empty);
            }
        }
    }
}
