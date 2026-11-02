using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HarmonyLib;
using RoR2;
using RoR2.Networking;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace ROR_O.patches
{
    // Projectile sync is one buffer per object, sent from NetworkCallbacks.LateUpdate.
    // GetDirtyChannel allows that send only after GetNetworkSendInterval (1/30s by default)
    // and only when a dirty bit is set. Vanilla still writes both syncvars from every
    // physics step, and the setter calls OnSync before SetSyncVar notices the value
    // did not change. The listen host never reads that interpolation buffer: the server
    // branch of MyFixedUpdate keeps the rigidbody pose. Receiving clients still enter
    // OnSync from OnDeserialize when the packet arrives.
    [HarmonyPatch(typeof(ProjectileNetworkTransform), "CheckForUpdateAndPushUpdatesIfNeeded")]
    public static class SampleProjectileSyncAtSendIntervalPatch
    {
        private const uint PositionDirtyBit = 1u;
        private const uint RotationDirtyBit = 2u;

        private static readonly AccessTools.FieldRef<NetworkBehaviour, float> LastSendTimeRef =
            AccessTools.FieldRefAccess<NetworkBehaviour, float>("m_LastSendTime");

        private static readonly AccessTools.FieldRef<ProjectileNetworkTransform, bool> NeedsUpdateFlagRef =
            AccessTools.FieldRefAccess<ProjectileNetworkTransform, bool>("needsUpdate");

        private static readonly AccessTools.FieldRef<ProjectileNetworkTransform, Transform> TransformRef =
            AccessTools.FieldRefAccess<ProjectileNetworkTransform, Transform>("transform");

        private static readonly Func<ProjectileNetworkTransform, bool> NeedsUpdate =
            AccessTools.MethodDelegate<Func<ProjectileNetworkTransform, bool>>(
                AccessTools.Method(typeof(ProjectileNetworkTransform), "NeedsUpdate"));

        private static bool Prefix(ProjectileNetworkTransform __instance)
        {
            if (Time.time - LastSendTimeRef(__instance) <= __instance.GetNetworkSendInterval())
            {
                return false;
            }

            if (!NeedsUpdate(__instance))
            {
                return false;
            }

            Transform transform = TransformRef(__instance);
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            if (!position.Equals(__instance.serverPosition))
            {
                __instance.serverPosition = position;
                __instance.SetDirtyBit(PositionDirtyBit);
            }

            if (!rotation.Equals(__instance.serverRotation))
            {
                __instance.serverRotation = rotation;
                __instance.SetDirtyBit(RotationDirtyBit);
            }

            NeedsUpdateFlagRef(__instance) = false;
            return false;
        }
    }

    // The broadcast copies newestNetSnapshot only after positionTransmitInterval (0.1s).
    // Authority FixedUpdate was rebuilding that struct on every physics step and throwing
    // the previous one away. Non-authority bodies still apply the received buffer here.
    [HarmonyPatch(typeof(CharacterNetworkTransform), "FixedUpdate")]
    public static class SkipUnsentCharacterSnapshotPatch
    {
        private static readonly AccessTools.FieldRef<CharacterNetworkTransform, float> DebugPositionTimerRef =
            AccessTools.FieldRefAccess<CharacterNetworkTransform, float>("debugPositionTimer");

        private static readonly AccessTools.FieldRef<CharacterNetworkTransform, float> DebugColorTimerRef =
            AccessTools.FieldRefAccess<CharacterNetworkTransform, float>("debugColorTimer");

        private static bool Prefix(CharacterNetworkTransform __instance)
        {
            if (!__instance.hasEffectiveAuthority)
            {
                return true;
            }

            if (DebugPositionTimerRef(__instance) > 0f || DebugColorTimerRef(__instance) > 0f)
            {
                return true;
            }

            return false;
        }
    }

    // Same loop as CharacterNetworkTransformManager.FixedUpdate. The snapshot is read
    // from the live body at the moment it is queued, then ProcessQueue sends that one
    // buffer to every ready connection. Relayed owner snapshots already sitting in the
    // queue are forwarded unchanged.
    [HarmonyPatch(typeof(CharacterNetworkTransformManager), "FixedUpdate")]
    public static class BuildCharacterSnapshotAtSendPatch
    {
        private delegate CharacterNetworkTransform.Snapshot BuildSnapshotDelegate(CharacterNetworkTransform instance);

        private delegate void ProcessQueueDelegate(CharacterNetworkTransformManager instance);

        private static readonly BuildSnapshotDelegate BuildSnapshot =
            AccessTools.MethodDelegate<BuildSnapshotDelegate>(
                AccessTools.Method(typeof(CharacterNetworkTransform), "BuildSnapshot"));

        private static readonly ProcessQueueDelegate ProcessQueue =
            AccessTools.MethodDelegate<ProcessQueueDelegate>(
                AccessTools.Method(typeof(CharacterNetworkTransformManager), "ProcessQueue"));

        private static readonly AccessTools.FieldRef<CharacterNetworkTransformManager, Queue<CharacterNetworkTransformManager.NetSnapshot>> SnapshotQueueRef =
            AccessTools.FieldRefAccess<CharacterNetworkTransformManager, Queue<CharacterNetworkTransformManager.NetSnapshot>>("snapshotQueue");

        private static bool Prefix(CharacterNetworkTransformManager __instance)
        {
            if (!NetworkManager.singleton || RoR2Application.isInSinglePlayer)
            {
                return false;
            }

            ReadOnlyCollection<CharacterNetworkTransform> characters = CharacterNetworkTransform.readOnlyInstancesList;
            float fixedTime = Time.fixedTime;
            Queue<CharacterNetworkTransformManager.NetSnapshot> queue = SnapshotQueueRef(__instance);
            for (int i = 0; i < characters.Count; i++)
            {
                CharacterNetworkTransform character = characters[i];
                if (!character.hasEffectiveAuthority || fixedTime - character.lastPositionTransmitTime <= character.positionTransmitInterval)
                {
                    continue;
                }

                character.lastPositionTransmitTime = fixedTime;
                character.newestNetSnapshot = BuildSnapshot(character);
                queue.Enqueue(new CharacterNetworkTransformManager.NetSnapshot
                {
                    gameObject = character.gameObject,
                    snapshot = character.newestNetSnapshot
                });
            }

            while (queue.Count > 0)
            {
                ProcessQueue(__instance);
            }

            return false;
        }
    }
}
