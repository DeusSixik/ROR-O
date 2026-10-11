using HarmonyLib;
using UnityEngine.Networking;

namespace ROR_O.patches
{
    // NetworkServer.UpdateServerObjects calls UNetUpdate on every spawned object each frame, and UNetUpdate asks every
    // NetworkBehaviour for GetDirtyChannel (Time.time plus a virtual send interval) even though GetDirtyChannel can only
    // return a channel when the behaviour has dirty bits. Roughly 9 of 10 objects have none in a typical frame.
    [HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.UNetUpdate))]
    public static class SkipCleanNetworkIdentityUpdatePatch
    {
        private static readonly AccessTools.FieldRef<NetworkIdentity, NetworkBehaviour[]> BehavioursRef =
            AccessTools.FieldRefAccess<NetworkIdentity, NetworkBehaviour[]>("m_NetworkBehaviours");

        private static readonly AccessTools.FieldRef<NetworkBehaviour, uint> DirtyBitsRef =
            AccessTools.FieldRefAccess<NetworkBehaviour, uint>("m_SyncVarDirtyBits");

        private static bool Prefix(NetworkIdentity __instance)
        {
            NetworkBehaviour[] behaviours = BehavioursRef(__instance);
            if (behaviours == null)
            {
                return true;
            }

            for (int i = 0; i < behaviours.Length; i++)
            {
                NetworkBehaviour behaviour = behaviours[i];
                if (ReferenceEquals(behaviour, null) || DirtyBitsRef(behaviour) != 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
