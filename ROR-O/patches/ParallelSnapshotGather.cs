using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Configuration;
using HarmonyLib;
using KinematicCharacterController;
using RoR2;
using RoR2.Networking;
using RoR2.Projectile;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.LowLevel;
using UnityEngine.Networking;
using UnityEngine.PlayerLoop;

namespace ROR_O.patches
{
    // The stock projectile and character sends stay on the main thread. This type only moves
    // the pose read. Overlap, motor pose, and simulated pose are separate switches so a run
    // can turn on one change at a time. They default off.
    internal static class ParallelSnapshotGather
    {
        private const int MinPosesForWorkers = 32;
        private const int TransformBatch = 32;
        private const int OverlapReportSamples = 256;

        private static ConfigEntry<bool>? entry;
        private static ConfigEntry<bool>? overlapEntry;
        private static ConfigEntry<bool>? motorPoseEntry;
        private static ConfigEntry<bool>? simulatedPoseEntry;
        private static bool installed;
        private static bool physicsJoinReady;
        private static bool loggedWorkers;
        private static bool loggedAnchor;
        private static bool loggedPhysicsJoin;
        private static bool overlapReported;
        private static bool inFlush;

        private static readonly List<ProjectileNetworkTransform> Projectiles = new List<ProjectileNetworkTransform>(64);
        private static readonly List<PendingCharacter> Characters = new List<PendingCharacter>(16);
        private static readonly List<CharacterNetworkTransformManager> Managers = new List<CharacterNetworkTransformManager>(2);

        private static Transform[] sources = Array.Empty<Transform>();
        private static TransformAccessArray transforms;
        private static NativeArray<Vector3> positions;
        private static NativeArray<Quaternion> rotations;
        private static NativeArray<byte> valid;

        private static bool hasPending;
        private static JobHandle pending;
        private static int pendingProjectiles;
        private static int pendingCharacters;
        private static int pendingJobCount;
        private static bool pendingMotorCharacters;
        private static int overlapSamples;
        private static int overlapFinishedBeforeJoin;
        private static long overlapWaitTicks;

        private delegate void ProcessQueueDelegate(CharacterNetworkTransformManager instance);

        private static readonly ProcessQueueDelegate ProcessQueue =
            AccessTools.MethodDelegate<ProcessQueueDelegate>(
                AccessTools.Method(typeof(CharacterNetworkTransformManager), "ProcessQueue"));

        private static readonly AccessTools.FieldRef<CharacterNetworkTransformManager, Queue<CharacterNetworkTransformManager.NetSnapshot>> SnapshotQueueRef =
            AccessTools.FieldRefAccess<CharacterNetworkTransformManager, Queue<CharacterNetworkTransformManager.NetSnapshot>>("snapshotQueue");

        internal static bool Enabled => entry != null && entry.Value;

        internal static bool UsesMotorPosition =>
            (motorPoseEntry != null && motorPoseEntry.Value) || (simulatedPoseEntry != null && simulatedPoseEntry.Value);

        private static bool OverlapEnabled => overlapEntry != null && overlapEntry.Value;

        private static bool SimulatedPoseEnabled => simulatedPoseEntry != null && simulatedPoseEntry.Value;

        internal static ConfigEntry<bool>? Entry => entry;

        internal static void Register(RORO plugin)
        {
            entry = plugin.Config.Bind(
                "Network",
                "Experimental Parallel Snapshots",
                false,
                "Experimental. Poses due for a snapshot are read at the end of the fixed-update scripts, before PhysicsFixedUpdate. "
                    + "At 32 or more, worker threads read the transforms and this thread waits for that read before it writes the same sync fields as the single-threaded path. "
                    + "Fewer than 32 stay on this thread. Off by default.");
            overlapEntry = plugin.Config.Bind(
                "Network",
                "Experimental Snapshot Overlap",
                false,
                "Experimental. Used only while Parallel Snapshots is on and a step has at least 32 poses. "
                    + "The read is started at the end of the fixed-update scripts, and this thread waits at the player-loop entry immediately before PhysicsFixedUpdate. "
                    + "The first 256 such steps log once how many reads had already finished by that wait, and the average time spent inside the wait. Off by default.");
            motorPoseEntry = plugin.Config.Bind(
                "Network",
                "Experimental Character Motor Pose",
                false,
                "Experimental. A character position is taken from the kinematic motor field that the motor has just copied back onto the transform: the previous tick while its interpolation is on, otherwise the simulated position. "
                    + "A body with no motor, or a motor that is not simulating, still uses the transform. A transform write after the kinematic system in the same step is not seen. "
                    + "With Parallel Snapshots, those bodies are left out of the transform read. The message layout is unchanged. Off by default.");
            simulatedPoseEntry = plugin.Config.Bind(
                "Network",
                "Experimental Character Simulated Pose",
                false,
                "Experimental. A character position is the kinematic motor position just simulated, instead of the transform the motor restores for interpolation. "
                    + "That sample is one physics step newer than the stock snapshot. Clients interpolate the newer sample. Host hits still use the motor. "
                    + "Overrides Character Motor Pose. Bodies without a simulating motor still use the transform. Off by default.");
            RiskOfOptionsIntegration.Hold(new RiskOfOptionsCatalog
            {
                EnableExperimentalParallelSnapshots = entry,
                EnableExperimentalSnapshotOverlap = overlapEntry,
                EnableExperimentalCharacterMotorPose = motorPoseEntry,
                EnableExperimentalCharacterSimulatedPose = simulatedPoseEntry,
            });
            Install();
        }

        internal static void QueueProjectile(ProjectileNetworkTransform projectile)
        {
            Projectiles.Add(projectile);
        }

        internal static void QueueCharacter(CharacterNetworkTransformManager manager, CharacterNetworkTransform character)
        {
            Characters.Add(new PendingCharacter
            {
                Manager = manager,
                Character = character,
            });
        }

        internal static CharacterNetworkTransform.Snapshot BuildCharacterSnapshot(CharacterNetworkTransform character)
        {
            Quaternion rotation = character.characterDirection ? Quaternion.identity : character.transform.rotation;
            if (!TryMotorPosition(character, out Vector3 position))
            {
                character.transform.GetPositionAndRotation(out position, out rotation);
            }

            return SnapshotFromPose(character, position, rotation);
        }

        private static void Install()
        {
            if (installed)
            {
                return;
            }

            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] stages = root.subSystemList;
            if (stages == null)
            {
                return;
            }

            for (int i = 0; i < stages.Length; i++)
            {
                if (stages[i].type != typeof(FixedUpdate))
                {
                    continue;
                }

                stages[i] = InsertFixedSystems(stages[i]);
            }

            root.subSystemList = stages;
            PlayerLoop.SetPlayerLoop(root);
            installed = true;
        }

        private static PlayerLoopSystem InsertFixedSystems(PlayerLoopSystem stage)
        {
            var systems = new List<PlayerLoopSystem>(stage.subSystemList ?? Array.Empty<PlayerLoopSystem>());
            bool hasTail = systems.Exists(system => system.type == typeof(ParallelSnapshotFixedTail));
            bool hasJoin = systems.Exists(system => system.type == typeof(ParallelSnapshotBeforePhysics));
            int scripts = systems.FindIndex(system => system.type == typeof(FixedUpdate.ScriptRunBehaviourFixedUpdate));
            int physics = systems.FindIndex(system => system.type == typeof(FixedUpdate.PhysicsFixedUpdate));
            if (!hasJoin && physics >= 0)
            {
                systems.Insert(physics, new PlayerLoopSystem
                {
                    type = typeof(ParallelSnapshotBeforePhysics),
                    updateDelegate = JoinBeforePhysics,
                });
                physicsJoinReady = true;
            }
            else if (hasJoin)
            {
                physicsJoinReady = true;
            }
            else if (!loggedPhysicsJoin)
            {
                loggedPhysicsJoin = true;
                RORO.GlobalLogger?.LogWarning("Snapshot overlap has no join point. PhysicsFixedUpdate was not found, so a parallel read waits in the fixed-script tail.");
            }

            if (!hasTail)
            {
                var added = new PlayerLoopSystem
                {
                    type = typeof(ParallelSnapshotFixedTail),
                    updateDelegate = Flush,
                };
                if (scripts < 0)
                {
                    systems.Add(added);
                    if (!loggedAnchor)
                    {
                        loggedAnchor = true;
                        RORO.GlobalLogger?.LogWarning("Parallel snapshot flush was appended to FixedUpdate. ScriptRunBehaviourFixedUpdate was not found.");
                    }
                }
                else
                {
                    systems.Insert(scripts + 1, added);
                }
            }

            stage.subSystemList = systems.ToArray();
            return stage;
        }

        internal static void Flush()
        {
            if (hasPending)
            {
                JoinPending();
            }

            if (inFlush || (Projectiles.Count == 0 && Characters.Count == 0))
            {
                return;
            }

            inFlush = true;
            int projectileCount = Projectiles.Count;
            int characterCount = Characters.Count;
            bool defer = false;
            try
            {
                bool motorCharacters = UsesMotorPosition;
                int jobCount = projectileCount;
                if (!motorCharacters)
                {
                    jobCount += characterCount;
                }

                if (jobCount > 0)
                {
                    EnsureSources(jobCount);
                    for (int i = 0; i < projectileCount; i++)
                    {
                        ProjectileNetworkTransform projectile = Projectiles[i];
                        sources[i] = projectile ? projectile.transform : null!;
                    }

                    if (!motorCharacters)
                    {
                        for (int i = 0; i < characterCount; i++)
                        {
                            CharacterNetworkTransform character = Characters[i].Character;
                            sources[projectileCount + i] = character ? character.transform : null!;
                        }
                    }
                }

                JobHandle handle = default;
                bool scheduled = jobCount >= MinPosesForWorkers && TrySchedule(jobCount, out handle);
                bool overlap = scheduled && OverlapEnabled && physicsJoinReady;
                if (overlap)
                {
                    pending = handle;
                    pendingProjectiles = projectileCount;
                    pendingCharacters = characterCount;
                    pendingJobCount = jobCount;
                    pendingMotorCharacters = motorCharacters;
                    hasPending = true;
                    defer = true;
                    // The batch has to be kicked here. Waiting later does not start a job that is still queued.
                    JobHandle.ScheduleBatchedJobs();
                    return;
                }

                if (scheduled)
                {
                    handle.Complete();
                    if (!loggedWorkers)
                    {
                        loggedWorkers = true;
                        RORO.GlobalLogger?.LogInfo($"Experimental parallel snapshots read {jobCount} poses on worker threads.");
                    }
                }
                else if (jobCount > 0)
                {
                    ReadOnMainThread(jobCount);
                }

                Commit(projectileCount, characterCount, jobCount, motorCharacters);
            }
            finally
            {
                if (!defer)
                {
                    DropProcessed(projectileCount, characterCount);
                }

                inFlush = false;
            }
        }

        private static void JoinBeforePhysics()
        {
            if (hasPending)
            {
                JoinPending();
            }
        }

        private static void JoinPending()
        {
            if (!hasPending || inFlush)
            {
                return;
            }

            hasPending = false;
            int projectileCount = pendingProjectiles;
            int characterCount = pendingCharacters;
            int jobCount = pendingJobCount;
            bool motorCharacters = pendingMotorCharacters;
            JobHandle handle = pending;
            pending = default;
            inFlush = true;
            try
            {
                if (!overlapReported)
                {
                    if (handle.IsCompleted)
                    {
                        overlapFinishedBeforeJoin++;
                    }

                    long started = Stopwatch.GetTimestamp();
                    handle.Complete();
                    overlapWaitTicks += Stopwatch.GetTimestamp() - started;
                    overlapSamples++;
                    if (overlapSamples >= OverlapReportSamples)
                    {
                        overlapReported = true;
                        double averageMs = overlapWaitTicks * 1000.0 / Stopwatch.Frequency / overlapSamples;
                        RORO.GlobalLogger?.LogInfo(
                            $"Experimental snapshot overlap over {overlapSamples} physics steps: average wait inside Complete {averageMs:0.000} ms, already finished before the join on {overlapFinishedBeforeJoin} steps.");
                    }
                }
                else
                {
                    handle.Complete();
                }

                Commit(projectileCount, characterCount, jobCount, motorCharacters);
            }
            finally
            {
                DropProcessed(projectileCount, characterCount);
                inFlush = false;
            }
        }

        private static void Commit(int projectileCount, int characterCount, int jobCount, bool motorCharacters)
        {
            for (int i = 0; i < projectileCount; i++)
            {
                ProjectileNetworkTransform projectile = Projectiles[i];
                if (!projectile || i >= jobCount || valid[i] == 0)
                {
                    continue;
                }

                SampleProjectileSyncAtSendIntervalPatch.CommitProjectile(projectile, positions[i], rotations[i]);
            }

            Managers.Clear();
            for (int i = 0; i < characterCount; i++)
            {
                PendingCharacter pendingCharacter = Characters[i];
                CharacterNetworkTransform character = pendingCharacter.Character;
                CharacterNetworkTransformManager? manager = pendingCharacter.Manager;
                if (!character || manager == null)
                {
                    continue;
                }

                if (!TryReadCharacterPose(character, projectileCount + i, jobCount, motorCharacters, out Vector3 position, out Quaternion rotation))
                {
                    continue;
                }

                character.newestNetSnapshot = SnapshotFromPose(character, position, rotation);
                SnapshotQueueRef(manager).Enqueue(new CharacterNetworkTransformManager.NetSnapshot
                {
                    gameObject = character.gameObject,
                    snapshot = character.newestNetSnapshot,
                });
                if (!Managers.Contains(manager))
                {
                    Managers.Add(manager);
                }
            }

            for (int i = 0; i < Managers.Count; i++)
            {
                CharacterNetworkTransformManager manager = Managers[i];
                Queue<CharacterNetworkTransformManager.NetSnapshot> queue = SnapshotQueueRef(manager);
                while (queue.Count > 0)
                {
                    ProcessQueue(manager);
                }
            }
        }

        private static bool TryReadCharacterPose(
            CharacterNetworkTransform character,
            int pose,
            int jobCount,
            bool motorCharacters,
            out Vector3 position,
            out Quaternion rotation)
        {
            if (motorCharacters && TryMotorPosition(character, out position))
            {
                rotation = character.characterDirection ? Quaternion.identity : character.transform.rotation;
                return true;
            }

            if (!motorCharacters)
            {
                if (pose >= jobCount || valid[pose] == 0)
                {
                    position = default;
                    rotation = default;
                    return false;
                }

                position = positions[pose];
                rotation = rotations[pose];
                return true;
            }

            Transform transform = character.transform;
            if (!transform)
            {
                position = default;
                rotation = default;
                return false;
            }

            transform.GetPositionAndRotation(out position, out rotation);
            return true;
        }

        internal static bool TryMotorPosition(CharacterNetworkTransform character, out Vector3 position)
        {
            position = default;
            if (!UsesMotorPosition)
            {
                return false;
            }

            CharacterMotor body = character.characterMotor;
            KinematicCharacterMotor? motor = body != null ? body.Motor : null;
            if (motor == null || !motor.enabled)
            {
                return false;
            }

            KCCSettings? settings = KinematicCharacterSystem.Settings;
            if (settings == null || !settings.AutoSimulation || Time.deltaTime == 0f)
            {
                return false;
            }

            if (SimulatedPoseEnabled || !settings.Interpolate)
            {
                position = motor.TransientPosition;
            }
            else
            {
                position = motor.InitialTickPosition;
            }

            return true;
        }

        private static CharacterNetworkTransform.Snapshot SnapshotFromPose(CharacterNetworkTransform character, Vector3 position, Quaternion rotation)
        {
            InputBankTest bank = character.inputBank;
            CharacterMotor motor = character.characterMotor;
            CharacterDirection direction = character.characterDirection;
            return new CharacterNetworkTransform.Snapshot
            {
                serverTime = PlatformSystems.networkManager.serverFixedTime,
                position = position,
                moveVector = bank ? bank.moveVector : Vector3.zero,
                aimDirection = bank ? bank.aimDirection : Vector3.zero,
                rotation = direction ? Quaternion.Euler(direction.pitch, direction.yaw, 0f) : rotation,
                isGrounded = motor && motor.isGrounded,
                groundNormal = motor ? motor.estimatedGroundNormal : Vector3.zero,
            };
        }

        private static bool TrySchedule(int count, out JobHandle handle)
        {
            handle = default;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (!sources[i])
                    {
                        return false;
                    }
                }

                FitTransforms(count);
                EnsureArrays(count);
                var job = new ReadPoseJob
                {
                    Position = positions,
                    Rotation = rotations,
                    Valid = valid,
                };
                handle = job.ScheduleReadOnly(transforms, TransformBatch);
                return true;
            }
            catch (Exception exception)
            {
                if (!loggedWorkers)
                {
                    loggedWorkers = true;
                    RORO.GlobalLogger?.LogWarning($"Experimental parallel snapshots fell back to the main thread: {exception.Message}");
                }

                return false;
            }
        }

        private static void ReadOnMainThread(int count)
        {
            EnsureArrays(count);
            for (int i = 0; i < count; i++)
            {
                Transform transform = sources[i];
                if (!transform)
                {
                    valid[i] = 0;
                    continue;
                }

                valid[i] = 1;
                transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                positions[i] = position;
                rotations[i] = rotation;
            }
        }

        private static void DropProcessed(int projectileCount, int characterCount)
        {
            if (Projectiles.Count <= projectileCount)
            {
                Projectiles.Clear();
            }
            else
            {
                Projectiles.RemoveRange(0, projectileCount);
            }

            if (Characters.Count <= characterCount)
            {
                Characters.Clear();
            }
            else
            {
                Characters.RemoveRange(0, characterCount);
            }

            Managers.Clear();
        }

        private static void FitTransforms(int count)
        {
            if (!transforms.isCreated)
            {
                transforms = new TransformAccessArray(Mathf.Max(count, MinPosesForWorkers));
            }

            while (transforms.length > count)
            {
                transforms.RemoveAtSwapBack(transforms.length - 1);
            }

            for (int i = 0; i < count; i++)
            {
                if (i == transforms.length)
                {
                    transforms.Add(sources[i]);
                }
                else
                {
                    transforms[i] = sources[i];
                }
            }
        }

        private static void EnsureSources(int count)
        {
            if (sources.Length >= count)
            {
                return;
            }

            int capacity = sources.Length == 0 ? 64 : sources.Length;
            while (capacity < count)
            {
                capacity *= 2;
            }

            Array.Resize(ref sources, capacity);
        }

        private static void EnsureArrays(int count)
        {
            if (positions.IsCreated && positions.Length >= count)
            {
                return;
            }

            int capacity = !positions.IsCreated ? 64 : positions.Length;
            while (capacity < count)
            {
                capacity *= 2;
            }

            DisposeArrays();
            positions = new NativeArray<Vector3>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            rotations = new NativeArray<Quaternion>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            valid = new NativeArray<byte>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void DisposeArrays()
        {
            if (positions.IsCreated)
            {
                positions.Dispose();
            }

            if (rotations.IsCreated)
            {
                rotations.Dispose();
            }

            if (valid.IsCreated)
            {
                valid.Dispose();
            }
        }

        private struct PendingCharacter
        {
            public CharacterNetworkTransformManager Manager;
            public CharacterNetworkTransform Character;
        }

        private struct ParallelSnapshotFixedTail
        {
        }

        private struct ParallelSnapshotBeforePhysics
        {
        }

        private struct ReadPoseJob : IJobParallelForTransform
        {
            [WriteOnly] public NativeArray<Vector3> Position;
            [WriteOnly] public NativeArray<Quaternion> Rotation;
            [WriteOnly] public NativeArray<byte> Valid;

            public void Execute(int index, TransformAccess transform)
            {
                if (!transform.isValid)
                {
                    Valid[index] = 0;
                    return;
                }

                Valid[index] = 1;
                transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                Position[index] = position;
                Rotation[index] = rotation;
            }
        }
    }

    // Backup if the fixed-script tail did not run. Empty when that tail already finished.
    // Runs before NetworkCallbacks serializes projectile sync fields.
    [HarmonyPatch(typeof(NetworkCallbacks), "LateUpdate")]
    internal static class FlushParallelSnapshotsBeforeSendPatch
    {
        private static void Prefix()
        {
            ParallelSnapshotGather.Flush();
        }
    }
}
