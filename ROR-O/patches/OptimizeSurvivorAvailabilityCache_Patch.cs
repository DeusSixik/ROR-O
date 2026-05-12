using System.Collections.Generic;
using HarmonyLib;
using RoR2;

namespace ROR_O.patches
{
    internal static class SurvivorAvailabilityCache
    {
        private sealed class CachedEntry
        {
            public int Version;
            public bool Value;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<SurvivorIndex, CachedEntry> CachedAvailability =
            new Dictionary<SurvivorIndex, CachedEntry>();

        private static int version;

        public static bool TryGet(SurvivorIndex survivorIndex, out bool isUnlocked)
        {
            lock (Sync)
            {
                if (CachedAvailability.TryGetValue(survivorIndex, out CachedEntry cachedEntry)
                    && cachedEntry.Version == version)
                {
                    isUnlocked = cachedEntry.Value;
                    return true;
                }
            }

            isUnlocked = false;
            return false;
        }

        public static void Store(SurvivorIndex survivorIndex, bool isUnlocked)
        {
            lock (Sync)
            {
                CachedAvailability[survivorIndex] = new CachedEntry
                {
                    Version = version,
                    Value = isUnlocked
                };
            }
        }

        public static void Invalidate()
        {
            lock (Sync)
            {
                version++;

                if (CachedAvailability.Count > 0)
                {
                    CachedAvailability.Clear();
                }
            }
        }
    }

    [HarmonyPatch(typeof(SurvivorCatalog), nameof(SurvivorCatalog.SurvivorIsUnlockedOnThisClient))]
    public static class OptimizeSurvivorCatalogAvailabilityPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(SurvivorIndex survivorIndex, ref bool __result)
        {
            if (SurvivorAvailabilityCache.TryGet(survivorIndex, out bool cachedAvailability))
            {
                __result = cachedAvailability;
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(SurvivorIndex survivorIndex, bool __result)
        {
            SurvivorAvailabilityCache.Store(survivorIndex, __result);
        }
    }

    [HarmonyPatch(typeof(UserProfile), nameof(UserProfile.GrantUnlockable))]
    public static class OptimizeSurvivorAvailabilityGrantUnlockablePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SurvivorAvailabilityCache.Invalidate();
        }
    }

    [HarmonyPatch(typeof(UserProfile), nameof(UserProfile.RevokeUnlockable))]
    public static class OptimizeSurvivorAvailabilityRevokeUnlockablePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SurvivorAvailabilityCache.Invalidate();
        }
    }

    [HarmonyPatch(typeof(LocalUserManager), nameof(LocalUserManager.AddMainUser), new[] { typeof(UserProfile) })]
    public static class OptimizeSurvivorAvailabilityAddMainUserPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SurvivorAvailabilityCache.Invalidate();
        }
    }

    [HarmonyPatch(typeof(LocalUserManager), nameof(LocalUserManager.RemoveUser), new[] { typeof(int) })]
    public static class OptimizeSurvivorAvailabilityRemoveUserByIndexPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SurvivorAvailabilityCache.Invalidate();
        }
    }

    [HarmonyPatch(typeof(LocalUser), "set_userProfile")]
    public static class OptimizeSurvivorAvailabilitySetUserProfilePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SurvivorAvailabilityCache.Invalidate();
        }
    }

    [HarmonyPatch(typeof(LocalUserManager), nameof(LocalUserManager.ClearUsers))]
    public static class OptimizeSurvivorAvailabilityClearUsersPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SurvivorAvailabilityCache.Invalidate();
        }
    }

    [HarmonyPatch(typeof(LocalUserManager), nameof(LocalUserManager.SetLocalUsers))]
    public static class OptimizeSurvivorAvailabilitySetLocalUsersPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SurvivorAvailabilityCache.Invalidate();
        }
    }

    [HarmonyPatch(typeof(SurvivorCatalog), nameof(SurvivorCatalog.SetSurvivorDefs))]
    public static class OptimizeSurvivorAvailabilitySetSurvivorDefsPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SurvivorAvailabilityCache.Invalidate();
        }
    }
}
