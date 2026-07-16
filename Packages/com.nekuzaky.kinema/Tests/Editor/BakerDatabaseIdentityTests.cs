using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Kinema.MotionMatching.Editor;

namespace Kinema.MotionMatching.Tests
{
    /// <summary>
    /// Pins the config-to-database pairing: a rebake must update the database the scene already
    /// references, not mint a new asset beside it. Getting this wrong is invisible at the call site
    /// and loud in the project - controllers keep the stale database (so rebaking "does nothing")
    /// while duplicates accumulate.
    /// </summary>
    public sealed class BakerDatabaseIdentityTests
    {
        private const string TempFolder = "Assets/__BakerIdentityTests";

        private MotionMatchingConfig _config;
        private GameObject _rig;
        private AnimationClip _clip;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "__BakerIdentityTests");

            // A real prefab asset, not a scene object: the config is an asset, and an asset cannot
            // serialize a reference to a scene object - it would silently read back as null and the
            // bake would fail with "No rig prefab assigned".
            var scratch = new GameObject("IdentityRig");
            var bone = new GameObject("Foot");
            bone.transform.SetParent(scratch.transform, false);
            scratch.AddComponent<Animator>();
            _rig = PrefabUtility.SaveAsPrefabAsset(scratch, $"{TempFolder}/IdentityRig.prefab");
            Object.DestroyImmediate(scratch);

            _clip = new AnimationClip { name = "IdentityWalk" };
            _clip.SetCurve("Foot", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            AssetDatabase.CreateAsset(_clip, $"{TempFolder}/IdentityWalk.anim");

            _config = ScriptableObject.CreateInstance<MotionMatchingConfig>();
            AssetDatabase.CreateAsset(_config, $"{TempFolder}/Identity.asset");

            var serialized = new SerializedObject(_config);
            serialized.FindProperty("_rigPrefab").objectReferenceValue = _rig;
            SerializedProperty clips = serialized.FindProperty("_clips");
            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = _clip;
            SerializedProperty bones = serialized.FindProperty("_schema").FindPropertyRelative("BoneNames");
            bones.arraySize = 1;
            bones.GetArrayElementAtIndex(0).stringValue = "Foot";
            SerializedProperty weights = serialized.FindProperty("_schema").FindPropertyRelative("BoneWeights");
            weights.arraySize = 1;
            weights.GetArrayElementAtIndex(0).floatValue = 1f;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            // The rig is a prefab asset now - deleting the folder takes it with everything else.
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
        }

        [Test]
        public void FindDatabaseFor_BeforeAnyBake_IsNull()
        {
            Assert.IsNull(MotionMatchingBaker.FindDatabaseFor(_config),
                "a config that has never been baked has no database to find");
        }

        [Test]
        public void FindDatabaseFor_NullConfig_IsNull()
        {
            Assert.IsNull(MotionMatchingBaker.FindDatabaseFor(null));
        }

        [Test]
        public void FindDatabaseFor_AfterBake_ResolvesTheBakedAsset()
        {
            BakeReport report = MotionMatchingBaker.Bake(_config);
            Assert.IsTrue(report.Success, report.Error);

            MotionMatchingDatabase found = MotionMatchingBaker.FindDatabaseFor(_config);

            Assert.IsNotNull(found, "the baker's own naming convention must resolve back to the asset it wrote");
            Assert.AreEqual(AssetDatabase.GetAssetPath(report.Database), AssetDatabase.GetAssetPath(found));
        }

        [Test]
        public void Rebake_ThroughFindDatabaseFor_UpdatesInPlaceInsteadOfDuplicating()
        {
            BakeReport first = MotionMatchingBaker.Bake(_config);
            Assert.IsTrue(first.Success, first.Error);
            string firstPath = first.DatabasePath;

            // Exactly what the config inspector's Bake button does.
            BakeReport second = MotionMatchingBaker.Bake(_config, MotionMatchingBaker.FindDatabaseFor(_config));
            Assert.IsTrue(second.Success, second.Error);

            Assert.AreEqual(firstPath, second.DatabasePath,
                "a rebake must write to the same asset - a new path leaves every controller on the stale database");
            Assert.AreEqual(1, AssetDatabase.FindAssets($"t:{nameof(MotionMatchingDatabase)}", new[] { TempFolder }).Length,
                "rebaking must not leave a duplicate database behind");
        }
    }
}
