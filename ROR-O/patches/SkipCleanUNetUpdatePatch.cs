using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine.Networking;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.UNetUpdate))]
    [HarmonyPriority(Priority.Last)]
    internal static class SkipCleanUNetUpdatePatch
    {
        private static readonly AccessTools.FieldRef<NetworkIdentity, NetworkBehaviour[]> Behaviours =
            AccessTools.FieldRefAccess<NetworkIdentity, NetworkBehaviour[]>("m_NetworkBehaviours");

        private static readonly AccessTools.FieldRef<NetworkBehaviour, uint> SyncVarDirtyBits =
            AccessTools.FieldRefAccess<NetworkBehaviour, uint>("m_SyncVarDirtyBits");

        private static ConfigEntry<bool>? entry;

        internal static void Register(RORO plugin)
        {
            entry = plugin.Config.Bind(
                "Network",
                "Experimental Skip Clean UNet Update",
                false,
                "Experimental. NetworkIdentity.UNetUpdate is in PreLateUpdate, the late window. "
                    + "On, an identity whose sync bits are all clear skips that method. "
                    + "A missing behaviour, or any set bit, still runs the vanilla method. "
                    + "Off runs the vanilla method every time. Off by default.");
            RiskOfOptionsIntegration.Hold(new RiskOfOptionsCatalog
            {
                EnableExperimentalSkipCleanUNetUpdate = entry,
            });
        }

        private static bool Prefix(NetworkIdentity __instance)
        {
            if (entry == null || !entry.Value)
            {
                return true;
            }

            NetworkBehaviour[] behaviours = Behaviours(__instance);
            if (behaviours == null)
            {
                return true;
            }

            for (int i = 0; i < behaviours.Length; i++)
            {
                NetworkBehaviour behaviour = behaviours[i];
                if (behaviour == null || SyncVarDirtyBits(behaviour) != 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
