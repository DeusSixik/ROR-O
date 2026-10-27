using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ROR_O.patches
{
    // Runs DynamicBone.UpdateDynamicBones for every bone that updates this frame in one batch after all LateUpdates:
    // transforms are read by a parallel read-only job, the particle simulation runs in parallel per bone, and the
    // main thread only writes one SetPositionAndRotation per particle, parent before child, like vanilla does.
    // Particle positions stay in the native buffer. They are copied from the vanilla Particle objects when a slot is
    // created and after ResetParticlesPosition (enable, weight, coming back from distance disable). Destroyed rigs
    // leave the transform array. Distant rigs stay: the read job returns before touching them, and removing them on
    // every distance change would sort that array more often than a death wave.
    internal static class DynamicBoneJobs
    {
        private const int ChildCountRefreshFrames = 30;
        private const byte RotationChanged = 1;

        // Waking worker threads costs more than it saves for a couple of bones; those run the same code inline.
        private const int MinBonesForWorkerThreads = 4;

        private struct ParticleStatic
        {
            public int ParentIndex;
            public int TransformIndex;
            public float Damping;
            public float Elasticity;
            public float Stiffness;
            public float Inert;
            public float Radius;
            public Vector3 EndOffset;
        }

        private struct ParticleState
        {
            public Vector3 Position;
            public Vector3 PrevPosition;
        }

        private struct BoneFrame
        {
            public int ParticleStart;
            public int ParticleCount;
            public int BoneTransformIndex;
            public int Loops;
            public Vector3 ObjectPrevPosition;
            public Vector3 Gravity;
            public Vector3 Force;
            public Vector3 LocalGravity;
            public float Weight;
            public int FreezeAxis;
            public int ColliderStart;
            public int ColliderCount;
            public Vector3 ObjectPosition;
            public Vector3 ObjectMove;
            public float ObjectScale;
            public byte Invalid;
        }

        private struct ColliderData
        {
            public Vector3 P0;
            public Vector3 P1;
            public float Radius;
            public byte Capsule;
            public byte Inside;
        }

        private sealed class Slot
        {
            public DynamicBone Bone = null!;
            public IList Particles = null!;
            public object[] ParticleObjects = Array.Empty<object>();
            public Transform?[] ParticleTransforms = Array.Empty<Transform?>();
            public int[] TransformIndices = Array.Empty<int>();
            public int ParticleStart;
            public int ParticleCount;
            public int BoneTransformIndex = -1;
            public int NextChildCountRefresh;
            public bool UseVanilla;
            public bool Registered;
            public bool LayoutDirty = true;
            public bool PullState = true;
            public bool NativeStateValid;
        }

        private struct Range
        {
            public int Start;
            public int Count;
        }

        private struct PendingBone
        {
            public Slot Slot;
            public float DeltaTime;
            public float TimeBeforeStep;
        }

        private struct LoopMarker
        {
        }

        private static readonly Type? ParticleType = AccessTools.Inner(typeof(DynamicBone), "Particle");
        private static readonly FieldInfo? ParticlesField = AccessTools.Field(typeof(DynamicBone), "m_Particles");

        private static readonly AccessTools.FieldRef<object, Transform>? ParticleTransformRef = ParticleFieldRef<Transform>("m_Transform");
        private static readonly AccessTools.FieldRef<object, int>? ParticleParentRef = ParticleFieldRef<int>("m_ParentIndex");
        private static readonly AccessTools.FieldRef<object, float>? ParticleDampingRef = ParticleFieldRef<float>("m_Damping");
        private static readonly AccessTools.FieldRef<object, float>? ParticleElasticityRef = ParticleFieldRef<float>("m_Elasticity");
        private static readonly AccessTools.FieldRef<object, float>? ParticleStiffnessRef = ParticleFieldRef<float>("m_Stiffness");
        private static readonly AccessTools.FieldRef<object, float>? ParticleInertRef = ParticleFieldRef<float>("m_Inert");
        private static readonly AccessTools.FieldRef<object, float>? ParticleRadiusRef = ParticleFieldRef<float>("m_Radius");
        private static readonly AccessTools.FieldRef<object, Vector3>? ParticleEndOffsetRef = ParticleFieldRef<Vector3>("m_EndOffset");
        private static readonly AccessTools.FieldRef<object, Vector3>? ParticlePositionRef = ParticleFieldRef<Vector3>("m_Position");
        private static readonly AccessTools.FieldRef<object, Vector3>? ParticlePrevPositionRef = ParticleFieldRef<Vector3>("m_PrevPosition");

        private static readonly AccessTools.FieldRef<DynamicBone, Vector3> LocalGravityRef = AccessTools.FieldRefAccess<DynamicBone, Vector3>("m_LocalGravity");
        private static readonly AccessTools.FieldRef<DynamicBone, Vector3> ObjectMoveRef = AccessTools.FieldRefAccess<DynamicBone, Vector3>("m_ObjectMove");
        private static readonly AccessTools.FieldRef<DynamicBone, Vector3> ObjectPrevPositionRef = AccessTools.FieldRefAccess<DynamicBone, Vector3>("m_ObjectPrevPosition");
        private static readonly AccessTools.FieldRef<DynamicBone, float> ObjectScaleRef = AccessTools.FieldRefAccess<DynamicBone, float>("m_ObjectScale");
        private static readonly AccessTools.FieldRef<DynamicBone, float> TimeRef = AccessTools.FieldRefAccess<DynamicBone, float>("m_Time");
        private static readonly AccessTools.FieldRef<DynamicBone, float> WeightRef = AccessTools.FieldRefAccess<DynamicBone, float>("m_Weight");
        private static readonly AccessTools.FieldRef<DynamicBone, bool> DistantDisabledRef = AccessTools.FieldRefAccess<DynamicBone, bool>("m_DistantDisabled");

        private static readonly Action<DynamicBone> CheckDistance =
            AccessTools.MethodDelegate<Action<DynamicBone>>(AccessTools.Method(typeof(DynamicBone), "CheckDistance"));

        private static readonly Action<DynamicBone, float> UpdateDynamicBones =
            AccessTools.MethodDelegate<Action<DynamicBone, float>>(AccessTools.Method(typeof(DynamicBone), "UpdateDynamicBones"));

        private static readonly List<Slot> Slots = new List<Slot>();
        private static readonly Dictionary<DynamicBone, Slot> SlotByBone = new Dictionary<DynamicBone, Slot>(ReferenceComparer<DynamicBone>.Instance);
        private static readonly List<PendingBone> Pending = new List<PendingBone>();
        private static readonly Dictionary<DynamicBoneCollider, int> ColliderIndexThisFrame =
            new Dictionary<DynamicBoneCollider, int>(ReferenceComparer<DynamicBoneCollider>.Instance);
        private static readonly List<ColliderData> ColliderList = new List<ColliderData>();
        private static readonly List<int> ColliderRefList = new List<int>();

        private static readonly List<Range> FreeParticleRanges = new List<Range>();
        private static int particleHighWater;
        private static Slot?[] ownerByTransform = Array.Empty<Slot?>();
        private static int[] localByTransform = Array.Empty<int>();

        private static bool installed;
        private static bool failed;

        private static TransformAccessArray transforms;
        private static NativeArray<Vector3> readPosition;
        private static NativeArray<Quaternion> readRotation;
        private static NativeArray<Vector3> readLocalPosition;
        private static NativeArray<Quaternion> readLocalRotation;
        private static NativeArray<Matrix4x4> readMatrix;
        private static NativeArray<byte> readValid;
        private static NativeArray<int> readFrame;
        private static NativeArray<ParticleStatic> particleStatic;
        private static NativeArray<ParticleState> particleState;
        private static NativeArray<int> childCounts;
        private static NativeArray<Quaternion> outRotation;
        private static NativeArray<byte> outFlags;
        private static NativeArray<BoneFrame> frames;
        private static NativeArray<ColliderData> colliders;
        private static NativeArray<int> colliderRefs;

        private static ConfigEntry<bool>? enabled;

        public static bool Active => installed && !failed && enabled?.Value != false;

        private static AccessTools.FieldRef<object, T>? ParticleFieldRef<T>(string name)
        {
            return ParticleType == null ? null : AccessTools.FieldRefAccess<T>(ParticleType, name);
        }

        public static void Bind(ConfigEntry<bool> entry)
        {
            enabled = entry;
        }

        public static void Install()
        {
            if (installed)
            {
                return;
            }

            if (ParticleType == null || ParticlesField == null || ParticlePositionRef == null)
            {
                RORO.GlobalLogger?.LogWarning("DynamicBone layout is not the expected one; multithreaded DynamicBone is disabled.");
                return;
            }

            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            if (!InsertAfterLateUpdate(ref root))
            {
                RORO.GlobalLogger?.LogWarning("PreLateUpdate.ScriptRunBehaviourLateUpdate was not found; multithreaded DynamicBone is disabled.");
                return;
            }

            PlayerLoop.SetPlayerLoop(root);
            Application.quitting += DisposeAll;
            installed = true;
            RORO.GlobalLogger?.LogInfo("Multithreaded DynamicBone installed after the LateUpdate stage.");
        }

        private static bool InsertAfterLateUpdate(ref PlayerLoopSystem root)
        {
            PlayerLoopSystem[] stages = root.subSystemList;
            for (int i = 0; i < stages.Length; i++)
            {
                if (stages[i].type != typeof(PreLateUpdate))
                {
                    continue;
                }

                var systems = new List<PlayerLoopSystem>(stages[i].subSystemList);
                if (systems.Exists(system => system.type == typeof(LoopMarker)))
                {
                    return true;
                }

                int lateUpdateIndex = systems.FindIndex(system => system.type == typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate));
                if (lateUpdateIndex < 0)
                {
                    return false;
                }

                systems.Insert(lateUpdateIndex + 1, new PlayerLoopSystem
                {
                    type = typeof(LoopMarker),
                    updateDelegate = RunFrame,
                });
                stages[i].subSystemList = systems.ToArray();
                root.subSystemList = stages;
                return true;
            }

            return false;
        }

        // Called from the DynamicBone.LateUpdate prefix. Returns true when the bone is handled for this frame.
        public static bool TryEnqueue(DynamicBone bone)
        {
            if (bone.m_DistantDisable)
            {
                CheckDistance(bone);
            }

            if (!(WeightRef(bone) > 0f) || (bone.m_DistantDisable && DistantDisabledRef(bone)) || bone.m_Root == null)
            {
                return true;
            }

            if (!SlotByBone.TryGetValue(bone, out Slot slot))
            {
                IList? particles = ParticlesField!.GetValue(bone) as IList;
                if (particles == null)
                {
                    return false;
                }

                slot = new Slot { Bone = bone, Particles = particles };
                SlotByBone.Add(bone, slot);
                Slots.Add(slot);
            }

            if (slot.Particles.Count == 0)
            {
                return false;
            }

            if (!slot.Registered
                || slot.ParticleObjects.Length != slot.Particles.Count
                || !ReferenceEquals(slot.Particles[0], slot.ParticleObjects[0]))
            {
                slot.LayoutDirty = true;
            }

            if (slot.UseVanilla && !slot.LayoutDirty)
            {
                return false;
            }

            float deltaTime = Time.deltaTime;
            float timeBefore = TimeRef(bone);
            Pending.Add(new PendingBone { Slot = slot, DeltaTime = deltaTime, TimeBeforeStep = timeBefore });
            return true;
        }

        private static void RunFrame()
        {
            if (Pending.Count == 0)
            {
                return;
            }

            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (!Active)
                {
                    RunPendingOnMainThread();
                    return;
                }

                RemoveDestroyedSlots();
                Simulate();
            }
            catch (Exception exception)
            {
                failed = true;
                RORO.GlobalLogger?.LogError($"Multithreaded DynamicBone failed, falling back to the vanilla update: {exception}");
                RunPendingOnMainThread();
            }
            finally
            {
                Pending.Clear();
                Stats.BatchTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            }
        }

        // Profiler-free timing of everything DynamicBone costs the main thread in LateUpdate, logged once a minute so
        // the jobs and vanilla paths can be compared by flipping the setting in game.
        internal static class Stats
        {
            private const float ReportIntervalSeconds = 60f;

            public static long LateUpdateTicks;
            public static long BatchTicks;
            public static long LateUpdateCalls;
            public static long Bones;
            public static long Particles;
            public static int InlineFrames;
            public static int WorkerFrames;

            private static int frames;
            private static int lastFrame = -1;
            private static float windowStart = -1f;
            private static bool windowJobs;

            public static void CountFrame()
            {
                int frame = Time.frameCount;
                if (frame == lastFrame)
                {
                    return;
                }

                lastFrame = frame;
                bool jobs = Active;
                float now = Time.unscaledTime;
                if (windowStart < 0f || jobs != windowJobs)
                {
                    Reset(now, jobs);
                    return;
                }

                frames++;
                if (now - windowStart < ReportIntervalSeconds || frames == 0)
                {
                    return;
                }

                double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double lateUpdate = LateUpdateTicks * toMs / frames;
                double batch = BatchTicks * toMs / frames;
                RORO.GlobalLogger?.LogInfo(
                    $"DynamicBone main thread, {(windowJobs ? "jobs" : "vanilla")}: {lateUpdate + batch:0.000} ms/frame " +
                    $"(LateUpdate {lateUpdate:0.000} + batch {batch:0.000}), components {LateUpdateCalls / (double)frames:0.0}/frame, " +
                    $"simulated {Bones / (double)frames:0.0} bones and {Particles / (double)frames:0.0} particles/frame, " +
                    $"inline frames {InlineFrames}, worker frames {WorkerFrames}, over {frames} frames");
                Reset(now, jobs);
            }

            private static void Reset(float now, bool jobs)
            {
                windowStart = now;
                windowJobs = jobs;
                frames = 0;
                LateUpdateTicks = 0;
                BatchTicks = 0;
                LateUpdateCalls = 0;
                Bones = 0;
                Particles = 0;
                InlineFrames = 0;
                WorkerFrames = 0;
            }
        }

        private static void RunPendingOnMainThread()
        {
            for (int i = 0; i < Pending.Count; i++)
            {
                RunVanilla(Pending[i]);
            }
        }

        private static void RunVanilla(PendingBone pending)
        {
            Slot slot = pending.Slot;
            DynamicBone bone = slot.Bone;
            if (bone == null)
            {
                return;
            }

            if (slot.LayoutDirty)
            {
                SyncSlot(slot);
            }

            PushParticleState(slot);
            TimeRef(bone) = pending.TimeBeforeStep;
            UpdateDynamicBones(bone, pending.DeltaTime);
            slot.PullState = true;
        }

        internal static void NoteParticlesReset(DynamicBone bone)
        {
            if (SlotByBone.TryGetValue(bone, out Slot slot))
            {
                slot.PullState = true;
            }
        }

        private static void RemoveDestroyedSlots()
        {
            for (int i = Slots.Count - 1; i >= 0; i--)
            {
                Slot slot = Slots[i];
                if (slot.Bone == null)
                {
                    ReleaseSlot(slot);
                    Slots.RemoveAt(i);
                    SlotByBone.Remove(slot.Bone!);
                }
            }

            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                if (Pending[i].Slot.Bone == null)
                {
                    Pending.RemoveAt(i);
                }
            }
        }

        private static void SyncSlot(Slot slot)
        {
            slot.LayoutDirty = false;
            IList particles = slot.Particles;
            int count = particles.Count;
            if (count == 0 || ParticleTransformRef == null || ParticleParentRef == null)
            {
                ReleaseSlot(slot);
                slot.UseVanilla = true;
                return;
            }

            int transformCount = 1;
            bool missingTransform = false;
            for (int k = 0; k < count; k++)
            {
                if (!ParticleTransformRef(particles[k]))
                {
                    missingTransform = true;
                    break;
                }

                transformCount++;
            }

            bool sameObjects = slot.ParticleObjects.Length == count
                && ReferenceEquals(particles[0], slot.ParticleObjects[0]);
            if (missingTransform)
            {
                if (sameObjects)
                {
                    PushParticleState(slot);
                }

                ReleaseSlot(slot);
                slot.UseVanilla = true;
                return;
            }

            bool sameShape = slot.Registered
                && slot.ParticleCount == count
                && slot.TransformIndices.Length == transformCount;
            if (!sameShape)
            {
                if (sameObjects)
                {
                    PushParticleState(slot);
                }

                ReleaseSlot(slot);
                slot.ParticleObjects = new object[count];
                slot.ParticleTransforms = new Transform?[count];
                slot.ParticleCount = count;
                slot.ParticleStart = AllocateParticleRange(count);
                slot.TransformIndices = new int[transformCount];
                slot.BoneTransformIndex = AddTransform(slot, 0, slot.Bone.transform);
                slot.TransformIndices[0] = slot.BoneTransformIndex;
                slot.Registered = true;
                slot.NativeStateValid = false;
                slot.PullState = true;
                slot.NextChildCountRefresh = 0;
                slot.UseVanilla = false;

                int start = slot.ParticleStart;
                for (int k = 0; k < count; k++)
                {
                    object particle = particles[k];
                    Transform transform = ParticleTransformRef(particle);
                    slot.ParticleObjects[k] = particle;
                    slot.ParticleTransforms[k] = transform;
                    int transformIndex = AddTransform(slot, k + 1, transform);
                    slot.TransformIndices[k + 1] = transformIndex;
                    WriteParticleStatic(start + k, particle, transformIndex);
                }

                return;
            }

            slot.UseVanilla = false;
            if (!sameObjects)
            {
                slot.PullState = true;
                slot.NextChildCountRefresh = 0;
            }

            if (transforms[slot.TransformIndices[0]] != slot.Bone.transform)
            {
                transforms[slot.TransformIndices[0]] = slot.Bone.transform;
            }

            int particleStart = slot.ParticleStart;
            for (int k = 0; k < count; k++)
            {
                object particle = particles[k];
                Transform transform = ParticleTransformRef(particle);
                slot.ParticleObjects[k] = particle;
                slot.ParticleTransforms[k] = transform;
                int transformIndex = slot.TransformIndices[k + 1];
                if (transforms[transformIndex] != transform)
                {
                    transforms[transformIndex] = transform;
                }

                WriteParticleStatic(particleStart + k, particle, transformIndex);
            }
        }

        private static void WriteParticleStatic(int index, object particle, int transformIndex)
        {
            particleStatic[index] = new ParticleStatic
            {
                ParentIndex = ParticleParentRef!(particle),
                TransformIndex = transformIndex,
                Damping = ParticleDampingRef!(particle),
                Elasticity = ParticleElasticityRef!(particle),
                Stiffness = ParticleStiffnessRef!(particle),
                Inert = ParticleInertRef!(particle),
                Radius = ParticleRadiusRef!(particle),
                EndOffset = ParticleEndOffsetRef!(particle),
            };
        }

        private static void ReleaseSlot(Slot slot)
        {
            if (!slot.Registered)
            {
                return;
            }

            RemoveSlotTransforms(slot);
            if (slot.ParticleCount > 0)
            {
                FreeParticleRanges.Add(new Range { Start = slot.ParticleStart, Count = slot.ParticleCount });
            }

            slot.Registered = false;
            slot.NativeStateValid = false;
            slot.ParticleCount = 0;
            slot.ParticleStart = 0;
            slot.BoneTransformIndex = -1;
            slot.TransformIndices = Array.Empty<int>();
        }

        private static void RemoveSlotTransforms(Slot slot)
        {
            int[] pending = slot.TransformIndices;
            int count = pending.Length;
            for (int n = 0; n < count; n++)
            {
                int index = pending[n];
                int last = transforms.length - 1;
                Slot? moved = index == last ? null : ownerByTransform[last];
                int movedLocal = index == last ? -1 : localByTransform[last];
                transforms.RemoveAtSwapBack(index);
                if (index == last)
                {
                    continue;
                }

                for (int p = n + 1; p < count; p++)
                {
                    if (pending[p] == last)
                    {
                        pending[p] = index;
                    }
                }

                if (moved != null && !ReferenceEquals(moved, slot))
                {
                    moved.TransformIndices[movedLocal] = index;
                    RemapTransformIndex(moved, last, index);
                    ownerByTransform[index] = moved;
                    localByTransform[index] = movedLocal;
                }
                else
                {
                    ownerByTransform[index] = null;
                }
            }
        }

        private static void RemapTransformIndex(Slot slot, int from, int to)
        {
            if (slot.BoneTransformIndex == from)
            {
                slot.BoneTransformIndex = to;
            }

            int start = slot.ParticleStart;
            for (int k = 0; k < slot.ParticleCount; k++)
            {
                ParticleStatic particle = particleStatic[start + k];
                if (particle.TransformIndex == from)
                {
                    particle.TransformIndex = to;
                    particleStatic[start + k] = particle;
                }
            }
        }

        private static int AddTransform(Slot slot, int local, Transform transform)
        {
            if (!transforms.isCreated)
            {
                transforms = new TransformAccessArray(16, -1);
            }

            transforms.Add(transform);
            int index = transforms.length - 1;
            EnsureTransformCapacity(transforms.length);
            ownerByTransform[index] = slot;
            localByTransform[index] = local;
            return index;
        }

        private static int AllocateParticleRange(int count)
        {
            for (int i = 0; i < FreeParticleRanges.Count; i++)
            {
                if (FreeParticleRanges[i].Count == count)
                {
                    int start = FreeParticleRanges[i].Start;
                    FreeParticleRanges.RemoveAt(i);
                    return start;
                }
            }

            int allocated = particleHighWater;
            particleHighWater += count;
            GrowArray(ref particleStatic, particleHighWater);
            GrowArray(ref particleState, particleHighWater);
            GrowArray(ref childCounts, particleHighWater);
            GrowArray(ref outRotation, particleHighWater);
            GrowArray(ref outFlags, particleHighWater);
            return allocated;
        }

        private static void EnsureTransformCapacity(int length)
        {
            GrowArray(ref readPosition, length);
            GrowArray(ref readRotation, length);
            GrowArray(ref readLocalPosition, length);
            GrowArray(ref readLocalRotation, length);
            GrowArray(ref readMatrix, length);
            GrowArray(ref readValid, length);
            GrowArray(ref readFrame, length);
            if (ownerByTransform.Length < length)
            {
                int size = Mathf.NextPowerOfTwo(Math.Max(16, length));
                Array.Resize(ref ownerByTransform, size);
                Array.Resize(ref localByTransform, size);
            }
        }

        private static void GrowArray<T>(ref NativeArray<T> array, int min)
            where T : struct
        {
            if (min <= 0 || (array.IsCreated && array.Length >= min))
            {
                return;
            }

            int size = Mathf.NextPowerOfTwo(Math.Max(16, min));
            var next = new NativeArray<T>(size, Allocator.Persistent);
            if (array.IsCreated)
            {
                NativeArray<T>.Copy(array, next, array.Length);
                array.Dispose();
            }

            array = next;
        }

        private static void PullParticleState(Slot slot)
        {
            int start = slot.ParticleStart;
            for (int k = 0; k < slot.ParticleCount; k++)
            {
                object particle = slot.ParticleObjects[k];
                particleState[start + k] = new ParticleState
                {
                    Position = ParticlePositionRef!(particle),
                    PrevPosition = ParticlePrevPositionRef!(particle),
                };
            }

            slot.PullState = false;
            slot.NativeStateValid = true;
        }

        private static void PushParticleState(Slot slot)
        {
            if (!slot.NativeStateValid || slot.PullState || slot.ParticleCount == 0)
            {
                return;
            }

            int start = slot.ParticleStart;
            for (int k = 0; k < slot.ParticleCount; k++)
            {
                object particle = slot.ParticleObjects[k];
                ParticleState state = particleState[start + k];
                ParticlePositionRef!(particle) = state.Position;
                ParticlePrevPositionRef!(particle) = state.PrevPosition;
            }
        }

        private static void Simulate()
        {
            ColliderIndexThisFrame.Clear();
            ColliderList.Clear();
            ColliderRefList.Clear();
            EnsureCapacity(ref frames, Pending.Count);

            for (int i = 0; i < Pending.Count; i++)
            {
                Slot slot = Pending[i].Slot;
                if (slot.Bone != null && slot.LayoutDirty)
                {
                    SyncSlot(slot);
                }
            }

            int frameCount = Time.frameCount;
            int jobCandidates = 0;
            for (int i = 0; i < Pending.Count; i++)
            {
                Slot slot = Pending[i].Slot;
                if (slot.Bone != null && slot.Registered && !slot.UseVanilla && slot.Particles.Count == slot.ParticleObjects.Length)
                {
                    jobCandidates++;
                }
            }

            if (jobCandidates < MinBonesForWorkerThreads)
            {
                RunPendingOnMainThread();
                Stats.InlineFrames++;
                Stats.Bones += jobCandidates;
                return;
            }

            int jobCount = 0;
            for (int i = 0; i < Pending.Count; i++)
            {
                PendingBone pending = Pending[i];
                Slot slot = pending.Slot;
                DynamicBone bone = slot.Bone;
                if (!slot.Registered || slot.UseVanilla || slot.Particles.Count != slot.ParticleObjects.Length)
                {
                    RunVanilla(pending);
                    Pending[i] = new PendingBone { Slot = slot, DeltaTime = -1f };
                    continue;
                }

                if (frameCount >= slot.NextChildCountRefresh)
                {
                    slot.NextChildCountRefresh = frameCount + ChildCountRefreshFrames;
                    for (int k = 0; k < slot.ParticleTransforms.Length; k++)
                    {
                        Transform? transform = slot.ParticleTransforms[k];
                        childCounts[slot.ParticleStart + k] = transform != null ? transform.childCount : 0;
                    }
                }

                int[] transformIndices = slot.TransformIndices;
                for (int t = 0; t < transformIndices.Length; t++)
                {
                    readFrame[transformIndices[t]] = frameCount;
                }

                if (slot.PullState || !slot.NativeStateValid)
                {
                    PullParticleState(slot);
                }

                int colliderStart = ColliderRefList.Count;
                List<DynamicBoneCollider>? boneColliders = bone.m_Colliders;
                if (boneColliders != null)
                {
                    for (int c = 0; c < boneColliders.Count; c++)
                    {
                        DynamicBoneCollider collider = boneColliders[c];
                        if (collider && collider.enabled)
                        {
                            ColliderRefList.Add(GetColliderIndex(collider));
                        }
                    }
                }

                frames[jobCount] = new BoneFrame
                {
                    ParticleStart = slot.ParticleStart,
                    ParticleCount = slot.ParticleObjects.Length,
                    BoneTransformIndex = slot.BoneTransformIndex,
                    Loops = StepCount(bone, pending.DeltaTime),
                    ObjectPrevPosition = ObjectPrevPositionRef(bone),
                    Gravity = bone.m_Gravity,
                    Force = bone.m_Force,
                    LocalGravity = LocalGravityRef(bone),
                    Weight = WeightRef(bone),
                    FreezeAxis = (int)bone.m_FreezeAxis,
                    ColliderStart = colliderStart,
                    ColliderCount = ColliderRefList.Count - colliderStart,
                };
                Pending[i] = new PendingBone { Slot = slot, DeltaTime = pending.DeltaTime, TimeBeforeStep = pending.TimeBeforeStep };
                jobCount++;
            }

            if (jobCount == 0)
            {
                return;
            }

            EnsureCapacity(ref colliders, ColliderList.Count);
            EnsureCapacity(ref colliderRefs, ColliderRefList.Count);
            for (int i = 0; i < ColliderList.Count; i++)
            {
                colliders[i] = ColliderList[i];
            }

            for (int i = 0; i < ColliderRefList.Count; i++)
            {
                colliderRefs[i] = ColliderRefList[i];
            }

            var readJob = new ReadTransformsJob
            {
                CurrentFrame = frameCount,
                Frame = readFrame,
                Position = readPosition,
                Rotation = readRotation,
                LocalPosition = readLocalPosition,
                LocalRotation = readLocalRotation,
                Matrix = readMatrix,
                Valid = readValid,
            };

            var simulateJob = new SimulateJob
            {
                Frames = frames,
                Particles = particleStatic,
                State = particleState,
                ChildCounts = childCounts,
                Colliders = colliders,
                ColliderRefs = colliderRefs,
                Position = readPosition,
                Rotation = readRotation,
                LocalPosition = readLocalPosition,
                LocalRotation = readLocalRotation,
                Matrix = readMatrix,
                Valid = readValid,
                OutRotation = outRotation,
                OutFlags = outFlags,
            };

            JobHandle read = readJob.ScheduleReadOnly(transforms, 16);
            simulateJob.Schedule(jobCount, 1, read).Complete();
            Stats.WorkerFrames++;

            Stats.Bones += jobCount;
            for (int i = 0; i < jobCount; i++)
            {
                Stats.Particles += frames[i].ParticleCount;
            }

            ApplyResults();
        }

        private static void ApplyResults()
        {
            int jobIndex = 0;
            for (int i = 0; i < Pending.Count; i++)
            {
                PendingBone pending = Pending[i];
                if (pending.DeltaTime < 0f)
                {
                    continue;
                }

                BoneFrame frame = frames[jobIndex++];
                Slot slot = pending.Slot;
                DynamicBone bone = slot.Bone;
                if (frame.Invalid != 0)
                {
                    PushParticleState(slot);
                    ReleaseSlot(slot);
                    slot.UseVanilla = true;
                    if (bone != null)
                    {
                        TimeRef(bone) = pending.TimeBeforeStep;
                        UpdateDynamicBones(bone, pending.DeltaTime);
                    }

                    slot.PullState = true;
                    continue;
                }

                int start = slot.ParticleStart;
                for (int k = 0; k < slot.ParticleObjects.Length; k++)
                {
                    ParticleState state = particleState[start + k];
                    Transform? transform = slot.ParticleTransforms[k];
                    if (!transform)
                    {
                        continue;
                    }

                    if (k == 0)
                    {
                        if ((outFlags[start] & RotationChanged) != 0)
                        {
                            transform!.rotation = outRotation[start];
                        }
                    }
                    else
                    {
                        transform!.SetPositionAndRotation(state.Position, outRotation[start + k]);
                    }
                }

                ObjectPrevPositionRef(bone) = frame.ObjectPosition;
                ObjectMoveRef(bone) = frame.ObjectMove;
                ObjectScaleRef(bone) = frame.ObjectScale;
            }
        }

        private static int StepCount(DynamicBone bone, float deltaTime)
        {
            int loops = 1;
            if (bone.m_UpdateRate > 0f)
            {
                float step = 1f / bone.m_UpdateRate;
                ref float time = ref TimeRef(bone);
                time += deltaTime;
                loops = 0;
                while (time >= step)
                {
                    time -= step;
                    if (++loops >= 3)
                    {
                        time = 0f;
                        break;
                    }
                }
            }

            return loops;
        }

        private static int GetColliderIndex(DynamicBoneCollider collider)
        {
            if (ColliderIndexThisFrame.TryGetValue(collider, out int index))
            {
                return index;
            }

            Transform transform = collider.transform;
            float radius = collider.m_Radius * Mathf.Abs(transform.lossyScale.x);
            float halfLength = collider.m_Height * 0.5f - collider.m_Radius;
            var data = new ColliderData
            {
                Radius = radius,
                Inside = (byte)(collider.m_Bound == DynamicBoneCollider.Bound.Inside ? 1 : 0),
            };

            if (halfLength <= 0f)
            {
                data.P0 = transform.TransformPoint(collider.m_Center);
            }
            else
            {
                Vector3 center0 = collider.m_Center;
                Vector3 center1 = collider.m_Center;
                switch (collider.m_Direction)
                {
                    case DynamicBoneCollider.Direction.X:
                        center0.x -= halfLength;
                        center1.x += halfLength;
                        break;
                    case DynamicBoneCollider.Direction.Y:
                        center0.y -= halfLength;
                        center1.y += halfLength;
                        break;
                    case DynamicBoneCollider.Direction.Z:
                        center0.z -= halfLength;
                        center1.z += halfLength;
                        break;
                }

                data.Capsule = 1;
                data.P0 = transform.TransformPoint(center0);
                data.P1 = transform.TransformPoint(center1);
            }

            index = ColliderList.Count;
            ColliderList.Add(data);
            ColliderIndexThisFrame.Add(collider, index);
            return index;
        }

        private static void EnsureCapacity<T>(ref NativeArray<T> array, int count)
            where T : struct
        {
            if (array.IsCreated && array.Length >= count)
            {
                return;
            }

            if (array.IsCreated)
            {
                array.Dispose();
            }

            array = new NativeArray<T>(Math.Max(16, Mathf.NextPowerOfTwo(Math.Max(1, count))), Allocator.Persistent);
        }

        private static void DisposeRegistry()
        {
            if (transforms.isCreated)
            {
                transforms.Dispose();
            }

            DisposeArray(ref readPosition);
            DisposeArray(ref readRotation);
            DisposeArray(ref readLocalPosition);
            DisposeArray(ref readLocalRotation);
            DisposeArray(ref readMatrix);
            DisposeArray(ref readValid);
            DisposeArray(ref readFrame);
            DisposeArray(ref particleStatic);
            DisposeArray(ref particleState);
            DisposeArray(ref childCounts);
            DisposeArray(ref outRotation);
            DisposeArray(ref outFlags);
            FreeParticleRanges.Clear();
            particleHighWater = 0;
            ownerByTransform = Array.Empty<Slot?>();
            localByTransform = Array.Empty<int>();
            for (int i = 0; i < Slots.Count; i++)
            {
                Slot slot = Slots[i];
                slot.Registered = false;
                slot.NativeStateValid = false;
                slot.LayoutDirty = true;
                slot.ParticleCount = 0;
                slot.TransformIndices = Array.Empty<int>();
            }
        }

        private static void DisposeAll()
        {
            DisposeRegistry();
            DisposeArray(ref frames);
            DisposeArray(ref colliders);
            DisposeArray(ref colliderRefs);
        }

        private static void DisposeArray<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }

        private struct ReadTransformsJob : IJobParallelForTransform
        {
            public int CurrentFrame;
            [ReadOnly] public NativeArray<int> Frame;
            [WriteOnly] public NativeArray<Vector3> Position;
            [WriteOnly] public NativeArray<Quaternion> Rotation;
            [WriteOnly] public NativeArray<Vector3> LocalPosition;
            [WriteOnly] public NativeArray<Quaternion> LocalRotation;
            [WriteOnly] public NativeArray<Matrix4x4> Matrix;
            [WriteOnly] public NativeArray<byte> Valid;

            public void Execute(int index, TransformAccess transform)
            {
                if (Frame[index] != CurrentFrame)
                {
                    return;
                }

                if (!transform.isValid)
                {
                    Valid[index] = 0;
                    return;
                }

                Valid[index] = 1;
                transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                transform.GetLocalPositionAndRotation(out Vector3 localPosition, out Quaternion localRotation);
                Position[index] = position;
                Rotation[index] = rotation;
                LocalPosition[index] = localPosition;
                LocalRotation[index] = localRotation;
                Matrix[index] = transform.localToWorldMatrix;
            }
        }

        private struct SimulateJob : IJobParallelFor
        {
            public NativeArray<BoneFrame> Frames;
            [ReadOnly] public NativeArray<ParticleStatic> Particles;
            [NativeDisableParallelForRestriction] public NativeArray<ParticleState> State;
            [ReadOnly] public NativeArray<int> ChildCounts;
            [ReadOnly] public NativeArray<ColliderData> Colliders;
            [ReadOnly] public NativeArray<int> ColliderRefs;
            [ReadOnly] public NativeArray<Vector3> Position;
            [ReadOnly] public NativeArray<Quaternion> Rotation;
            [ReadOnly] public NativeArray<Vector3> LocalPosition;
            [ReadOnly] public NativeArray<Quaternion> LocalRotation;
            [ReadOnly] public NativeArray<Matrix4x4> Matrix;
            [ReadOnly] public NativeArray<byte> Valid;
            [NativeDisableParallelForRestriction] public NativeArray<Quaternion> OutRotation;
            [NativeDisableParallelForRestriction] public NativeArray<byte> OutFlags;

            public void Execute(int index)
            {
                BoneFrame frame = Frames[index];
                int start = frame.ParticleStart;
                int count = frame.ParticleCount;

                if (Valid[frame.BoneTransformIndex] == 0)
                {
                    frame.Invalid = 1;
                    Frames[index] = frame;
                    return;
                }

                for (int k = 0; k < count; k++)
                {
                    int transformIndex = Particles[start + k].TransformIndex;
                    if (transformIndex >= 0 && Valid[transformIndex] == 0)
                    {
                        frame.Invalid = 1;
                        Frames[index] = frame;
                        return;
                    }
                }

                Matrix4x4 boneMatrix = Matrix[frame.BoneTransformIndex];
                Vector3 objectPosition = Position[frame.BoneTransformIndex];
                float objectScale = Mathf.Abs(new Vector3(boneMatrix.m00, boneMatrix.m10, boneMatrix.m20).magnitude);
                Vector3 objectMove = objectPosition - frame.ObjectPrevPosition;

                if (frame.Loops > 0)
                {
                    for (int loop = 0; loop < frame.Loops; loop++)
                    {
                        UpdateParticles1(ref frame, start, count, objectMove, objectScale);
                        UpdateParticles2(ref frame, start, count, objectScale);
                        objectMove = Vector3.zero;
                    }
                }
                else
                {
                    SkipUpdateParticles(ref frame, start, count, objectMove);
                }

                ApplyParticles(start, count);

                frame.ObjectPosition = objectPosition;
                frame.ObjectMove = objectMove;
                frame.ObjectScale = objectScale;
                Frames[index] = frame;
            }

            private void UpdateParticles1(ref BoneFrame frame, int start, int count, Vector3 objectMove, float objectScale)
            {
                Vector3 gravity = frame.Gravity;
                Vector3 gravityDirection = frame.Gravity.normalized;
                Vector3 rootGravity = Rotation[Particles[start].TransformIndex] * frame.LocalGravity;
                Vector3 projected = gravityDirection * Mathf.Max(Vector3.Dot(rootGravity, gravityDirection), 0f);
                gravity -= projected;
                gravity = (gravity + frame.Force) * objectScale;

                for (int k = 0; k < count; k++)
                {
                    ParticleStatic particle = Particles[start + k];
                    ParticleState state = State[start + k];
                    if (particle.ParentIndex >= 0)
                    {
                        Vector3 velocity = state.Position - state.PrevPosition;
                        Vector3 inertia = objectMove * particle.Inert;
                        state.PrevPosition = state.Position + inertia;
                        state.Position += velocity * (1f - particle.Damping) + gravity + inertia;
                    }
                    else
                    {
                        state.PrevPosition = state.Position;
                        state.Position = Position[particle.TransformIndex];
                    }

                    State[start + k] = state;
                }
            }

            private void UpdateParticles2(ref BoneFrame frame, int start, int count, float objectScale)
            {
                Plane plane = default;
                for (int k = 1; k < count; k++)
                {
                    ParticleStatic particle = Particles[start + k];
                    ParticleStatic parent = Particles[start + particle.ParentIndex];
                    if (parent.TransformIndex < 0)
                    {
                        continue;
                    }

                    ParticleState state = State[start + k];
                    Vector3 parentPosition = State[start + particle.ParentIndex].Position;
                    Matrix4x4 parentMatrix = Matrix[parent.TransformIndex];
                    float restLength = RestLength(particle, parent, parentMatrix);
                    float stiffness = Mathf.Lerp(1f, particle.Stiffness, frame.Weight);

                    if (stiffness > 0f || particle.Elasticity > 0f)
                    {
                        Vector3 target = Target(particle, parentMatrix, parentPosition);
                        Vector3 delta = target - state.Position;
                        state.Position += delta * particle.Elasticity;
                        if (stiffness > 0f)
                        {
                            delta = target - state.Position;
                            float magnitude = delta.magnitude;
                            float maxLength = restLength * (1f - stiffness) * 2f;
                            if (magnitude > maxLength)
                            {
                                state.Position += delta * ((magnitude - maxLength) / magnitude);
                            }
                        }
                    }

                    if (frame.ColliderCount > 0)
                    {
                        float particleRadius = particle.Radius * objectScale;
                        for (int c = 0; c < frame.ColliderCount; c++)
                        {
                            Collide(Colliders[ColliderRefs[frame.ColliderStart + c]], ref state.Position, particleRadius);
                        }
                    }

                    if (frame.FreezeAxis != 0)
                    {
                        Quaternion parentRotation = Rotation[parent.TransformIndex];
                        Vector3 normal = frame.FreezeAxis == 1 ? parentRotation * Vector3.right
                            : frame.FreezeAxis == 2 ? parentRotation * Vector3.up
                            : parentRotation * Vector3.forward;
                        plane.SetNormalAndPosition(normal, parentPosition);
                        state.Position -= plane.normal * plane.GetDistanceToPoint(state.Position);
                    }

                    Vector3 toParent = parentPosition - state.Position;
                    float length = toParent.magnitude;
                    if (length > 0f)
                    {
                        state.Position += toParent * ((length - restLength) / length);
                    }

                    State[start + k] = state;
                }
            }

            private void SkipUpdateParticles(ref BoneFrame frame, int start, int count, Vector3 objectMove)
            {
                for (int k = 0; k < count; k++)
                {
                    ParticleStatic particle = Particles[start + k];
                    ParticleState state = State[start + k];
                    if (particle.ParentIndex >= 0)
                    {
                        state.PrevPosition += objectMove;
                        state.Position += objectMove;
                        ParticleStatic parent = Particles[start + particle.ParentIndex];
                        if (parent.TransformIndex >= 0)
                        {
                            Vector3 parentPosition = State[start + particle.ParentIndex].Position;
                            Matrix4x4 parentMatrix = Matrix[parent.TransformIndex];
                            float restLength = RestLength(particle, parent, parentMatrix);
                            float stiffness = Mathf.Lerp(1f, particle.Stiffness, frame.Weight);
                            if (stiffness > 0f)
                            {
                                Vector3 delta = Target(particle, parentMatrix, parentPosition) - state.Position;
                                float magnitude = delta.magnitude;
                                float maxLength = restLength * (1f - stiffness) * 2f;
                                if (magnitude > maxLength)
                                {
                                    state.Position += delta * ((magnitude - maxLength) / magnitude);
                                }
                            }

                            Vector3 toParent = parentPosition - state.Position;
                            float length = toParent.magnitude;
                            if (length > 0f)
                            {
                                state.Position += toParent * ((length - restLength) / length);
                            }
                        }
                    }
                    else if (particle.TransformIndex >= 0)
                    {
                        state.PrevPosition = state.Position;
                        state.Position = Position[particle.TransformIndex];
                    }

                    State[start + k] = state;
                }
            }

            // Mirrors ApplyParticlesToTransforms: a parent with at most one child is turned towards its simulated child,
            // and every particle's world rotation follows its parent's new rotation through its own local rotation.
            private void ApplyParticles(int start, int count)
            {
                for (int k = 0; k < count; k++)
                {
                    int transformIndex = Particles[start + k].TransformIndex;
                    OutFlags[start + k] = 0;
                    if (transformIndex >= 0)
                    {
                        OutRotation[start + k] = Rotation[transformIndex];
                    }
                }

                for (int k = 1; k < count; k++)
                {
                    ParticleStatic particle = Particles[start + k];
                    int parentSlot = start + particle.ParentIndex;
                    ParticleStatic parent = Particles[parentSlot];
                    if (parent.TransformIndex >= 0 && ChildCounts[parentSlot] <= 1)
                    {
                        Vector3 direction = particle.TransformIndex >= 0 ? LocalPosition[particle.TransformIndex] : particle.EndOffset;
                        Quaternion parentRotation = OutRotation[parentSlot];
                        Vector3 toChild = State[start + k].Position - State[parentSlot].Position;
                        OutRotation[parentSlot] = FromToRotation(parentRotation * direction, toChild) * parentRotation;
                        OutFlags[parentSlot] = RotationChanged;
                    }

                    if (particle.TransformIndex >= 0 && OutFlags[parentSlot] != 0)
                    {
                        OutRotation[start + k] = OutRotation[parentSlot] * LocalRotation[particle.TransformIndex];
                        OutFlags[start + k] = RotationChanged;
                    }
                }
            }

            private float RestLength(ParticleStatic particle, ParticleStatic parent, Matrix4x4 parentMatrix)
            {
                return particle.TransformIndex >= 0
                    ? (Position[parent.TransformIndex] - Position[particle.TransformIndex]).magnitude
                    : parentMatrix.MultiplyVector(particle.EndOffset).magnitude;
            }

            private Vector3 Target(ParticleStatic particle, Matrix4x4 parentMatrix, Vector3 parentPosition)
            {
                parentMatrix.SetColumn(3, parentPosition);
                return parentMatrix.MultiplyPoint3x4(particle.TransformIndex >= 0 ? LocalPosition[particle.TransformIndex] : particle.EndOffset);
            }

            private static void Collide(ColliderData collider, ref Vector3 position, float particleRadius)
            {
                if (collider.Capsule == 0)
                {
                    if (collider.Inside == 0)
                    {
                        OutsideSphere(ref position, particleRadius, collider.P0, collider.Radius);
                    }
                    else
                    {
                        InsideSphere(ref position, particleRadius, collider.P0, collider.Radius);
                    }
                }
                else if (collider.Inside == 0)
                {
                    OutsideCapsule(ref position, particleRadius, collider.P0, collider.P1, collider.Radius);
                }
                else
                {
                    InsideCapsule(ref position, particleRadius, collider.P0, collider.P1, collider.Radius);
                }
            }

            private static void OutsideSphere(ref Vector3 position, float particleRadius, Vector3 center, float sphereRadius)
            {
                float radius = sphereRadius + particleRadius;
                Vector3 offset = position - center;
                float sqrMagnitude = offset.sqrMagnitude;
                if (sqrMagnitude > 0f && sqrMagnitude < radius * radius)
                {
                    position = center + offset * (radius / Mathf.Sqrt(sqrMagnitude));
                }
            }

            private static void InsideSphere(ref Vector3 position, float particleRadius, Vector3 center, float sphereRadius)
            {
                float radius = sphereRadius - particleRadius;
                Vector3 offset = position - center;
                float sqrMagnitude = offset.sqrMagnitude;
                if (sqrMagnitude > radius * radius)
                {
                    position = center + offset * (radius / Mathf.Sqrt(sqrMagnitude));
                }
            }

            private static void OutsideCapsule(ref Vector3 position, float particleRadius, Vector3 p0, Vector3 p1, float capsuleRadius)
            {
                float radius = capsuleRadius + particleRadius;
                float radiusSqr = radius * radius;
                Vector3 axis = p1 - p0;
                Vector3 offset = position - p0;
                float projection = Vector3.Dot(offset, axis);
                if (projection <= 0f)
                {
                    float sqrMagnitude = offset.sqrMagnitude;
                    if (sqrMagnitude > 0f && sqrMagnitude < radiusSqr)
                    {
                        position = p0 + offset * (radius / Mathf.Sqrt(sqrMagnitude));
                    }

                    return;
                }

                float axisSqr = axis.sqrMagnitude;
                if (projection >= axisSqr)
                {
                    offset = position - p1;
                    float sqrMagnitude = offset.sqrMagnitude;
                    if (sqrMagnitude > 0f && sqrMagnitude < radiusSqr)
                    {
                        position = p1 + offset * (radius / Mathf.Sqrt(sqrMagnitude));
                    }
                }
                else if (axisSqr > 0f)
                {
                    projection /= axisSqr;
                    offset -= axis * projection;
                    float sqrMagnitude = offset.sqrMagnitude;
                    if (sqrMagnitude > 0f && sqrMagnitude < radiusSqr)
                    {
                        float distance = Mathf.Sqrt(sqrMagnitude);
                        position += offset * ((radius - distance) / distance);
                    }
                }
            }

            private static void InsideCapsule(ref Vector3 position, float particleRadius, Vector3 p0, Vector3 p1, float capsuleRadius)
            {
                float radius = capsuleRadius - particleRadius;
                float radiusSqr = radius * radius;
                Vector3 axis = p1 - p0;
                Vector3 offset = position - p0;
                float projection = Vector3.Dot(offset, axis);
                if (projection <= 0f)
                {
                    float sqrMagnitude = offset.sqrMagnitude;
                    if (sqrMagnitude > radiusSqr)
                    {
                        position = p0 + offset * (radius / Mathf.Sqrt(sqrMagnitude));
                    }

                    return;
                }

                float axisSqr = axis.sqrMagnitude;
                if (projection >= axisSqr)
                {
                    offset = position - p1;
                    float sqrMagnitude = offset.sqrMagnitude;
                    if (sqrMagnitude > radiusSqr)
                    {
                        position = p1 + offset * (radius / Mathf.Sqrt(sqrMagnitude));
                    }
                }
                else if (axisSqr > 0f)
                {
                    projection /= axisSqr;
                    offset -= axis * projection;
                    float sqrMagnitude = offset.sqrMagnitude;
                    if (sqrMagnitude > radiusSqr)
                    {
                        float distance = Mathf.Sqrt(sqrMagnitude);
                        position += offset * ((radius - distance) / distance);
                    }
                }
            }

            // Quaternion.FromToRotation is a native call; this is the same shortest-arc rotation in managed code.
            private static Quaternion FromToRotation(Vector3 from, Vector3 to)
            {
                float fromLength = from.magnitude;
                float toLength = to.magnitude;
                if (fromLength < 1e-6f || toLength < 1e-6f)
                {
                    return Quaternion.identity;
                }

                Vector3 f = from / fromLength;
                Vector3 t = to / toLength;
                float dot = Vector3.Dot(f, t);
                if (dot >= 1f - 1e-6f)
                {
                    return Quaternion.identity;
                }

                if (dot <= -1f + 1e-6f)
                {
                    Vector3 axis = Vector3.Cross(Vector3.right, f);
                    if (axis.sqrMagnitude < 1e-6f)
                    {
                        axis = Vector3.Cross(Vector3.up, f);
                    }

                    axis.Normalize();
                    return new Quaternion(axis.x, axis.y, axis.z, 0f);
                }

                Vector3 cross = Vector3.Cross(f, t);
                float s = Mathf.Sqrt((1f + dot) * 2f);
                float inverse = 1f / s;
                return new Quaternion(cross.x * inverse, cross.y * inverse, cross.z * inverse, s * 0.5f);
            }
        }
    }

    // UnityEngine.Object overrides Equals with a native liveness check, which dictionary lookups would pay on every call.
    internal sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

        public bool Equals(T? x, T? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    // One patch for the method. Separate Harmony classes each rewrite LateUpdate again while plugins load.
    [HarmonyPatch(typeof(DynamicBone), "LateUpdate")]
    public static class DynamicBoneLateUpdatePatch
    {
        private static bool announced;

        private static bool Prepare()
        {
            if (announced)
            {
                return true;
            }

            announced = true;
            if (AccessTools.DeclaredMethod(typeof(DynamicBone), "LateUpdate") == null)
            {
                RORO.GlobalLogger?.LogWarning("DynamicBone.LateUpdate not found, skipping DynamicBone optimization.");
                return false;
            }

            RORO.GlobalLogger?.LogInfo("DynamicBone.LateUpdate optimization enabled.");
            return true;
        }

        private static bool Prefix(DynamicBone __instance, out long __state)
        {
            DynamicBoneJobs.Stats.CountFrame();
            __state = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!OptimizeDynamicBoneLateUpdatePatch.ShouldSimulate(__instance))
            {
                return false;
            }

            return !DynamicBoneJobs.Active || !DynamicBoneJobs.TryEnqueue(__instance);
        }

        private static void Postfix(long __state)
        {
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - __state;
            DynamicBoneJobs.Stats.LateUpdateTicks += elapsed;
            DynamicBoneJobs.Stats.LateUpdateCalls++;
            if (HitchProbes.Armed)
            {
                HitchProbes.Add(HitchProbes.Id.DynBone, elapsed);
            }
        }
    }

    internal static class RegisterDynamicBoneJobsPatch
    {
        // Called from RORO.Awake after PatchAll. A postfix on that Awake cannot see the call that installs it.
        public static void Register(RORO plugin)
        {
            ConfigEntry<bool> entry = plugin.Config.Bind(
                "Dynamic Bones",
                "Multithreaded Simulation",
                true,
                OptionCopy.BoneJobs);
            DynamicBoneJobs.Bind(entry);
            SettingsProfiles.AddLate("Dynamic Bones", entry, true, true, true);
            RiskOfOptionsIntegration.Hold(new RiskOfOptionsCatalog { EnableDynamicBoneJobs = entry });
            DynamicBoneJobs.Install();
        }
    }

    [HarmonyPatch(typeof(DynamicBone), "ResetParticlesPosition")]
    internal static class SyncDynamicBoneParticleResetPatch
    {
        private static void Postfix(DynamicBone __instance)
        {
            DynamicBoneJobs.NoteParticlesReset(__instance);
        }
    }
}
