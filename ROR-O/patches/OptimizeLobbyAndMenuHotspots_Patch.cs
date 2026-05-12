using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using UnityEngine;
using PlatformID = RoR2.PlatformID;
using static ROR_O.patches.OptimizeGetComponentPatchTools;
using ROR_O.Utilities;

namespace ROR_O.patches
{
    internal static class MenuHotspotThrottleState
    {
        private sealed class ThrottleState
        {
            public bool IsDirty = true;
            public float NextAllowedTime;
        }

        private static readonly FixedConditionalWeakTable<LobbyUserList, ThrottleState> LobbyStates =
            new FixedConditionalWeakTable<LobbyUserList, ThrottleState>();

        private static readonly FixedConditionalWeakTable<SocialUserIconBehavior, ThrottleState> AvatarStates =
            new FixedConditionalWeakTable<SocialUserIconBehavior, ThrottleState>();

        private static readonly FixedConditionalWeakTable<SurvivorIconController, ThrottleState> SurvivorStates =
            new FixedConditionalWeakTable<SurvivorIconController, ThrottleState>();

        public static void MarkLobbyDirty(LobbyUserList? lobbyUserList)
        {
            MarkDirty(lobbyUserList, LobbyStates);
        }

        public static void MarkAvatarDirty(SocialUserIconBehavior? behavior)
        {
            MarkDirty(behavior, AvatarStates);
        }

        public static void MarkSurvivorDirty(SurvivorIconController? controller)
        {
            MarkDirty(controller, SurvivorStates);
        }

        public static bool ShouldRunLobbyRefresh(LobbyUserList? lobbyUserList, float intervalSeconds)
        {
            return ShouldRun(lobbyUserList, intervalSeconds, LobbyStates);
        }

        public static bool ShouldRunAvatarRefresh(SocialUserIconBehavior? behavior, bool shouldForceRefresh, float intervalSeconds)
        {
            return shouldForceRefresh || ShouldRun(behavior, intervalSeconds, AvatarStates);
        }

        public static bool ShouldRunSurvivorAvailabilityRefresh(SurvivorIconController? controller, float intervalSeconds)
        {
            return ShouldRun(controller, intervalSeconds, SurvivorStates);
        }

        private static void MarkDirty<T>(T? instance, FixedConditionalWeakTable<T, ThrottleState> table)
            where T : class
        {
            if (instance == null)
            {
                return;
            }

            ThrottleState state = table.GetOrCreateValue(instance);
            state.IsDirty = true;
            state.NextAllowedTime = 0f;
        }

        private static bool ShouldRun<T>(T? instance, float intervalSeconds, FixedConditionalWeakTable<T, ThrottleState> table)
            where T : class
        {
            if (instance == null)
            {
                return false;
            }

            ThrottleState state = table.GetOrCreateValue(instance);
            float now = Time.unscaledTime;
            if (!state.IsDirty && now < state.NextAllowedTime)
            {
                return false;
            }

            state.IsDirty = false;
            state.NextAllowedTime = now + intervalSeconds;
            return true;
        }
    }

    internal static class SteamAvatarHotspotCache
    {
        private const float PendingRequestTimeoutSeconds = 5f;

        private readonly struct AvatarCacheKey : IEquatable<AvatarCacheKey>
        {
            private readonly ulong steamId;
            private readonly int size;

            public AvatarCacheKey(PlatformID platformId, UserManager.AvatarSize avatarSize)
            {
                steamId = platformId.ID;
                size = (int)avatarSize;
            }

            public bool Equals(AvatarCacheKey other)
            {
                return steamId == other.steamId && size == other.size;
            }

            public override bool Equals(object? obj)
            {
                return obj is AvatarCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)steamId * 397) ^ size;
                }
            }
        }

        private sealed class PendingAvatarRequest
        {
            public readonly List<Action<Texture2D>> WaitingCallbacks = new List<Action<Texture2D>>();
            public float StartedAt;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<AvatarCacheKey, Texture2D> CachedTextures = new Dictionary<AvatarCacheKey, Texture2D>();
        private static readonly Dictionary<AvatarCacheKey, PendingAvatarRequest> PendingRequests = new Dictionary<AvatarCacheKey, PendingAvatarRequest>();

        public static bool ShouldRunOriginalRequest(PlatformID id, UserManager.AvatarSize size, Action<Texture2D>? onReceived)
        {
            AvatarCacheKey key = new AvatarCacheKey(id, size);
            Texture2D? cachedTexture = null;
            bool shouldRunOriginal = false;

            lock (Sync)
            {
                if (CachedTextures.TryGetValue(key, out Texture2D existingTexture))
                {
                    if (existingTexture)
                    {
                        cachedTexture = existingTexture;
                    }
                    else
                    {
                        CachedTextures.Remove(key);
                    }
                }

                if (!cachedTexture)
                {
                    float now = Time.unscaledTime;
                    if (PendingRequests.TryGetValue(key, out PendingAvatarRequest pendingRequest))
                    {
                        if (now - pendingRequest.StartedAt < PendingRequestTimeoutSeconds)
                        {
                            if (onReceived != null)
                            {
                                pendingRequest.WaitingCallbacks.Add(onReceived);
                            }

                            return false;
                        }

                        PendingRequests.Remove(key);
                    }

                    PendingRequests[key] = new PendingAvatarRequest { StartedAt = now };
                    shouldRunOriginal = true;
                }
            }

            if (cachedTexture && onReceived != null)
            {
                onReceived(cachedTexture!);
                return false;
            }

            return shouldRunOriginal;
        }

        public static void CompleteRequest(PlatformID id, UserManager.AvatarSize size, Texture2D? texture)
        {
            AvatarCacheKey key = new AvatarCacheKey(id, size);
            List<Action<Texture2D>>? waitingCallbacks = null;

            lock (Sync)
            {
                if (PendingRequests.TryGetValue(key, out PendingAvatarRequest pendingRequest))
                {
                    waitingCallbacks = pendingRequest.WaitingCallbacks;
                    PendingRequests.Remove(key);
                }

                if (texture)
                {
                    CachedTextures[key] = texture!;
                }
                else
                {
                    CachedTextures.Remove(key);
                }
            }

            if (texture == null)
            {
                return;
            }

            if (waitingCallbacks == null)
            {
                return;
            }

            for (int i = 0; i < waitingCallbacks.Count; i++)
            {
                waitingCallbacks[i](texture);
            }
        }
    }

    [HarmonyPatch(typeof(LobbyUserList), nameof(LobbyUserList.Update))]
    public static class OptimizeLobbyUserListUpdatePatch
    {
        private const float RefreshIntervalSeconds = 0.20f;

        [HarmonyPrefix]
        private static bool Prefix(LobbyUserList __instance)
        {
            return MenuHotspotThrottleState.ShouldRunLobbyRefresh(__instance, RefreshIntervalSeconds);
        }
    }

    [HarmonyPatch(typeof(LobbyUserList), nameof(LobbyUserList.OnEnable))]
    public static class OptimizeLobbyUserListOnEnablePatch
    {
        [HarmonyPostfix]
        private static void Postfix(LobbyUserList __instance)
        {
            MenuHotspotThrottleState.MarkLobbyDirty(__instance);
        }
    }

    [HarmonyPatch(typeof(LobbyUserList), nameof(LobbyUserList.OnLobbyChanged))]
    public static class OptimizeLobbyUserListOnLobbyChangedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(LobbyUserList __instance)
        {
            MenuHotspotThrottleState.MarkLobbyDirty(__instance);
        }
    }

    [HarmonyPatch(typeof(LobbyUserList), nameof(LobbyUserList.OnLobbyMemberDataUpdated))]
    public static class OptimizeLobbyUserListOnLobbyMemberDataUpdatedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(LobbyUserList __instance)
        {
            MenuHotspotThrottleState.MarkLobbyDirty(__instance);
        }
    }

    [HarmonyPatch(typeof(LobbyUserList), nameof(LobbyUserList.OnLobbyStateChanged))]
    public static class OptimizeLobbyUserListOnLobbyStateChangedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(LobbyUserList __instance)
        {
            MenuHotspotThrottleState.MarkLobbyDirty(__instance);
        }
    }

    [HarmonyPatch(typeof(LobbyUserListElement), nameof(LobbyUserListElement.Refresh))]
    public static class OptimizeLobbyUserListElementRefreshPatch
    {
        private static readonly MethodInfo ChildLocatorFindChildMethod =
            AccessTools.Method(typeof(ChildLocator), nameof(ChildLocator.FindChild), new[] { typeof(string) });

        private static readonly MethodInfo CachedFindChildMethod =
            AccessTools.Method(typeof(OptimizedComponentCache), nameof(OptimizedComponentCache.FindChild), new[] { typeof(ChildLocator), typeof(string) });

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ChildLocatorFindChildMethod, CachedFindChildMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"LobbyUserListElement.Refresh optimized: cached ChildLocator.FindChild calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(SocialUserIconBehavior), nameof(SocialUserIconBehavior.Refresh))]
    public static class OptimizeSocialUserIconBehaviorRefreshPatch
    {
        private const float RefreshIntervalSeconds = 0.25f;

        [HarmonyPrefix]
        private static bool Prefix(SocialUserIconBehavior __instance, bool shouldForceRefresh)
        {
            return MenuHotspotThrottleState.ShouldRunAvatarRefresh(__instance, shouldForceRefresh, RefreshIntervalSeconds);
        }
    }

    [HarmonyPatch(typeof(SteamUserManager), nameof(SteamUserManager.GetSteamAvatar))]
    public static class OptimizeSteamUserManagerGetSteamAvatarPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlatformID id, UserManager.AvatarSize size, Action<Texture2D> onRecieved)
        {
            return SteamAvatarHotspotCache.ShouldRunOriginalRequest(id, size, onRecieved);
        }
    }

    [HarmonyPatch(typeof(SocialUserIconBehavior), nameof(SocialUserIconBehavior.HandleNewTexture))]
    public static class OptimizeSocialUserIconBehaviorHandleNewTexturePatch
    {
        [HarmonyPostfix]
        private static void Postfix(SocialUserIconBehavior __instance, Texture2D tex)
        {
            SteamAvatarHotspotCache.CompleteRequest(__instance.userID, __instance.avatarSize, tex);
        }
    }

    [HarmonyPatch(typeof(SocialUserIconBehavior), nameof(SocialUserIconBehavior.OnEnableBehavior))]
    public static class OptimizeSocialUserIconBehaviorOnEnablePatch
    {
        [HarmonyPostfix]
        private static void Postfix(SocialUserIconBehavior __instance)
        {
            MenuHotspotThrottleState.MarkAvatarDirty(__instance);
        }
    }

    [HarmonyPatch(typeof(SocialUserIconBehavior), nameof(SocialUserIconBehavior.RefreshWithUser))]
    public static class OptimizeSocialUserIconBehaviorRefreshWithUserPatch
    {
        [HarmonyPostfix]
        private static void Postfix(SocialUserIconBehavior __instance)
        {
            MenuHotspotThrottleState.MarkAvatarDirty(__instance);
        }
    }

    [HarmonyPatch(typeof(SurvivorIconController), nameof(SurvivorIconController.UpdateAvailability))]
    public static class OptimizeSurvivorIconControllerAvailabilityPatch
    {
        private const float RefreshIntervalSeconds = 0.25f;

        [HarmonyPrefix]
        private static bool Prefix(SurvivorIconController __instance)
        {
            return MenuHotspotThrottleState.ShouldRunSurvivorAvailabilityRefresh(__instance, RefreshIntervalSeconds);
        }
    }

    [HarmonyPatch(typeof(SurvivorIconController), nameof(SurvivorIconController.Awake))]
    public static class OptimizeSurvivorIconControllerAwakePatch
    {
        [HarmonyPostfix]
        private static void Postfix(SurvivorIconController __instance)
        {
            MenuHotspotThrottleState.MarkSurvivorDirty(__instance);
        }
    }
}
