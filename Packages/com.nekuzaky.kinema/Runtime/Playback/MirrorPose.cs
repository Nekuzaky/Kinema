using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Experimental runtime pose mirroring node. Swaps Left/Right transform pairs (matched by name)
    /// and reflects local rotations/positions across the character's X plane, so a database frame
    /// flagged <see cref="MotionFrameInfo.IsMirrored"/> can play from the original clip. Assumes a
    /// left/right symmetric rest pose (true for Mixamo-style rigs). Placed before the inertializer
    /// so the mirror flip is absorbed by the transition smoothing.
    /// </summary>
    public sealed class MirrorPose : System.IDisposable
    {
        #region Private and Protected

        private NativeArray<TransformStreamHandle> _handles;
        private NativeArray<int> _pairIndex;      // index of the mirrored counterpart (self when unpaired)
        private NativeArray<Vector3> _scratchPos;
        private NativeArray<Quaternion> _scratchRot;
        private NativeArray<float> _state;        // [0] = mirror weight (0 or 1)
        private AnimationScriptPlayable _playable;
        private bool _created;

        #endregion

        #region Public

        public AnimationScriptPlayable Playable => _playable;
        public bool IsCreated => _created;

        /// <summary>1 = mirrored playback, 0 = passthrough. Switch is instant; smoothing is the inertializer's job.</summary>
        public void SetMirrored(bool mirrored)
        {
            if (_created) _state[0] = mirrored ? 1f : 0f;
        }

        #endregion

        #region Main API

        public AnimationScriptPlayable Create(PlayableGraph graph, Animator animator)
        {
            Transform root = animator.transform;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);

            var list = new List<Transform>(all.Length);
            foreach (Transform t in all)
                if (t != root) list.Add(t);

            var nameToIndex = new Dictionary<string, int>(list.Count);
            for (int i = 0; i < list.Count; i++) nameToIndex[list[i].name] = i;

            _handles = new NativeArray<TransformStreamHandle>(list.Count, Allocator.Persistent);
            _pairIndex = new NativeArray<int>(list.Count, Allocator.Persistent);
            _scratchPos = new NativeArray<Vector3>(list.Count, Allocator.Persistent);
            _scratchRot = new NativeArray<Quaternion>(list.Count, Allocator.Persistent);
            _state = new NativeArray<float>(1, Allocator.Persistent);

            for (int i = 0; i < list.Count; i++)
            {
                _handles[i] = animator.BindStreamTransform(list[i]);
                string mirrored = MirrorName(list[i].name);
                _pairIndex[i] = mirrored != null && nameToIndex.TryGetValue(mirrored, out int j) ? j : i;
            }

            var job = new MirrorJob
            {
                Handles = _handles,
                PairIndex = _pairIndex,
                Pos = _scratchPos,
                Rot = _scratchRot,
                State = _state
            };

            _playable = AnimationScriptPlayable.Create(graph, job, 1);
            _playable.SetProcessInputs(true);
            _created = true;
            return _playable;
        }

        public void Dispose()
        {
            if (!_created) return;
            _created = false;
            _handles.Dispose();
            _pairIndex.Dispose();
            _scratchPos.Dispose();
            _scratchRot.Dispose();
            _state.Dispose();
        }

        #endregion

        #region Tools and Utilities

        /// <summary>LeftFoot -> RightFoot, mixamorig:LeftArm -> mixamorig:RightArm, etc. Null when unpaired.</summary>
        private static string MirrorName(string name)
        {
            if (name.Contains("Left")) return name.Replace("Left", "Right");
            if (name.Contains("Right")) return name.Replace("Right", "Left");
            if (name.Contains("left")) return name.Replace("left", "right");
            if (name.Contains("right")) return name.Replace("right", "left");
            return null;
        }

        [BurstCompile]
        private struct MirrorJob : IAnimationJob
        {
            public NativeArray<TransformStreamHandle> Handles;
            [ReadOnly] public NativeArray<int> PairIndex;
            public NativeArray<Vector3> Pos;
            public NativeArray<Quaternion> Rot;
            public NativeArray<float> State;

            public void ProcessRootMotion(AnimationStream stream) { }

            public void ProcessAnimation(AnimationStream stream)
            {
                if (State[0] < 0.5f) return; // passthrough

                // Pass 1: read the full input pose.
                for (int i = 0; i < Handles.Length; i++)
                {
                    Pos[i] = Handles[i].GetLocalPosition(stream);
                    Rot[i] = Handles[i].GetLocalRotation(stream);
                }

                // Pass 2: write each bone from its mirrored counterpart, reflected across X.
                for (int i = 0; i < Handles.Length; i++)
                {
                    int src = PairIndex[i];
                    Vector3 p = Pos[src];
                    Quaternion q = Rot[src];

                    Handles[i].SetLocalPosition(stream, new Vector3(-p.x, p.y, p.z));
                    Handles[i].SetLocalRotation(stream, new Quaternion(q.x, -q.y, -q.z, q.w));
                }
            }
        }

        #endregion
    }
}
