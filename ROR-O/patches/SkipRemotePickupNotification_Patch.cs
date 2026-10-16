using HarmonyLib;
using RoR2;

namespace ROR_O.patches
{
    [HarmonyPatch(
        typeof(CharacterMasterNotificationQueue),
        nameof(CharacterMasterNotificationQueue.PushPickupNotification),
        typeof(CharacterMaster),
        typeof(UniquePickup))]
    public static class SkipRemotePickupNotificationPatch
    {
        // For a master owned by another player vanilla does nothing except write an error with a stack trace to every log listener.
        private static bool Prefix(CharacterMaster characterMaster)
        {
            return !characterMaster || characterMaster.hasAuthority;
        }
    }
}
