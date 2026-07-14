using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Kinema.MotionMatching
{
    /// <summary>
    /// Inertialization node: instead of crossfading two clips, the graph hard-switches to the new
    /// clip and this job carries the pose discontinuity as a per-bone offset (position + rotation,
    /// with initial velocity) that decays to zero over the blend time using a cubic Hermite curve
    /// (value and velocity both reach zero). This is the post-transition inertialization popularized
    /// by Gears of War 4 (Bollo, GDC 2018) and standard in AAA motion matching.
    ///
    /// The job keeps a two-frame history of its own output, so the offset is captured against what
    /// was actually on screen - including any still-decaying previous transition.
    /// </summary>
    public sealed class PoseInertializer : System.IDisposable
    {
        #region Private and Protected

        private const int StateElapsed = 0;
        private const int StateDuration = 1;
        private const int StateRequest = 2;
        private const int StateHistory = 3;
        private const int StateSize = 4;

        private NativeArray<TransformStreamHandle> _handles;
        private NativeArray<Vector3> _prevPos, _prevPrevPos, _offsetPos, _offsetPosVel;
        private NativeArray<Quaternion> _prevRot, _prevPrevRot;
        private NativeArray<Vector3> _offsetRot, _offsetRotVel; // axis * angle (radians)
        private NativeArray<float> _state;
        private AnimationScriptPlayable _playable;
        private bool _created;

        #endregion

        #region Public

        public AnimationScriptPlayable Playable => _playable;
        public bool IsCreated => _created;

        #endregion

        #region Main API

        /// <summary>Creates the script playable and binds every transform under the animator root.</summary>
        public AnimationScriptPlayable Create(PlayableGraph graph, Animator animator)
        {
            Transform root = animator.transform;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            int count = all.Length - 1; // exclude the root itself; root motion is handled by the animator.

            _handles = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent);
            _prevPos = new NativeArray<Vector3>(count, Allocator.Persistent);
            _prevPrevPos = new NativeArray<Vector3>(count, Allocator.Persistent);
            _prevRot = new NativeArray<Quaternion>(count, Allocator.Persistent);
            _prevPrevRot = new NativeArray<Quaternion>(count, Allocator.Persistent);
            _offsetPos = new NativeArray<Vector3>(count, Allocator.Persistent);
            _offsetPosVel = new NativeArray<Vector3>(count, Allocator.Persistent);
            _offsetRot = new NativeArray<Vector3>(count, Allocator.Persistent);
            _offsetRotVel = new NativeArray<Vector3>(count, Allocator.Persistent);
            _state = new NativeArray<float>(StateSize, Allocator.Persistent);
            _state[StateDuration] = 1f; // avoid divide-by-zero before the first transition.

            int h = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == root) continue;
                _handles[h++] = animator.BindStreamTransform(all[i]);
            }

            var job = new InertializationJob
            {
                Handles = _handles,
                PrevPos = _prevPos,
                PrevPrevPos = _prevPrevPos,
                PrevRot = _prevRot,
                PrevPrevRot = _prevPrevRot,
                OffsetPos = _offsetPos,
                OffsetPosVel = _offsetPosVel,
                OffsetRot = _offsetRot,
                OffsetRotVel = _offsetRotVel,
                State = _state
            };

            _playable = AnimationScriptPlayable.Create(graph, job, 1);
            _playable.SetProcessInputs(true);
            _created = true;
            return _playable;
        }

        /// <summary>Captures the current discontinuity on the next evaluation and decays it over <paramref name="duration"/>.</summary>
        public void RequestTransition(float duration)
        {
            if (!_created) return;
            _state[StateDuration] = Mathf.Max(0.01f, duration);
            _state[StateRequest] = 1f;
        }

        public void Dispose()
        {
            if (!_created) return;
            _created = false;
            _handles.Dispose();
            _prevPos.Dispose(); _prevPrevPos.Dispose();
            _prevRot.Dispose(); _prevPrevRot.Dispose();
            _offsetPos.Dispose(); _offsetPosVel.Dispose();
            _offsetRot.Dispose(); _offsetRotVel.Dispose();
            _state.Dispose();
        }

        #endregion

        #region Tools and Utilities

        [BurstCompile]
        private struct InertializationJob : IAnimationJob
        {
            public NativeArray<TransformStreamHandle> Handles;
            public NativeArray<Vector3> PrevPos, PrevPrevPos;
            public NativeArray<Quaternion> PrevRot, PrevPrevRot;
            public NativeArray<Vector3> OffsetPos, OffsetPosVel;
            public NativeArray<Vector3> OffsetRot, OffsetRotVel;
            public NativeArray<float> State;

            public void ProcessRootMotion(AnimationStream stream) { }

            public void ProcessAnimation(AnimationStream stream)
            {
                float dt = Mathf.Max(stream.deltaTime, 1e-5f);
                bool capture = State[StateRequest] > 0.5f && State[StateHistory] >= 2f;
                if (State[StateRequest] > 0.5f)
                {
                    State[StateRequest] = 0f;
                    State[StateElapsed] = 0f;
                    if (!capture)
                        State[StateElapsed] = State[StateDuration]; // no history: hard cut, no offset.
                }

                float elapsed = State[StateElapsed];
                float duration = State[StateDuration];
                bool active = elapsed < duration;
                float s = active ? Mathf.Clamp01(elapsed / duration) : 1f;

                // Cubic Hermite from (x0, v0) to (0, 0): value and velocity both land at zero.
                float s2 = s * s, s3 = s2 * s;
                float h00 = 2f * s3 - 3f * s2 + 1f;
                float h10 = s3 - 2f * s2 + s;

                for (int i = 0; i < Handles.Length; i++)
                {
                    TransformStreamHandle handle = Handles[i];
                    Vector3 inPos = handle.GetLocalPosition(stream);
                    Quaternion inRot = handle.GetLocalRotation(stream);

                    if (capture)
                    {
                        OffsetPos[i] = PrevPos[i] - inPos;
                        OffsetPosVel[i] = (PrevPos[i] - PrevPrevPos[i]) / dt;

                        OffsetRot[i] = ToAxisAngle(PrevRot[i] * Quaternion.Inverse(inRot));
                        OffsetRotVel[i] = ToAxisAngle(PrevRot[i] * Quaternion.Inverse(PrevPrevRot[i])) / dt;
                    }

                    Vector3 outPos = inPos;
                    Quaternion outRot = inRot;

                    if (active || capture)
                    {
                        Vector3 pOff = h00 * OffsetPos[i] + h10 * duration * OffsetPosVel[i];
                        Vector3 rOff = h00 * OffsetRot[i] + h10 * duration * OffsetRotVel[i];
                        outPos = inPos + pOff;
                        outRot = FromAxisAngle(rOff) * inRot;

                        handle.SetLocalPosition(stream, outPos);
                        handle.SetLocalRotation(stream, outRot);
                    }

                    PrevPrevPos[i] = PrevPos[i];
                    PrevPrevRot[i] = PrevRot[i];
                    PrevPos[i] = outPos;
                    PrevRot[i] = outRot;
                }

                State[StateElapsed] = elapsed + dt;
                if (State[StateHistory] < 2f) State[StateHistory] += 1f;
            }

            private static Vector3 ToAxisAngle(Quaternion q)
            {
                if (q.w < 0f) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
                float w = Mathf.Clamp(q.w, -1f, 1f);
                float angle = 2f * Mathf.Acos(w);
                float s = Mathf.Sqrt(Mathf.Max(1f - w * w, 0f));
                if (s < 1e-6f) return Vector3.zero;
                return new Vector3(q.x / s, q.y / s, q.z / s) * angle;
            }

            private static Quaternion FromAxisAngle(Vector3 axisAngle)
            {
                float angle = axisAngle.magnitude;
                if (angle < 1e-6f) return Quaternion.identity;
                Vector3 axis = axisAngle / angle;
                float half = angle * 0.5f;
                float sin = Mathf.Sin(half);
                return new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, Mathf.Cos(half));
            }
        }

        #endregion
    }
}
