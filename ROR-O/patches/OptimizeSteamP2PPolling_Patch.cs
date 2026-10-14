using System;
using System.Collections.Generic;
using System.Diagnostics;
using Facepunch.Steamworks;
using HarmonyLib;
using RoR2;
using RoR2.ConVar;
using RoR2.Networking;
using UnityEngine;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(Networking), nameof(Networking.Update))]
    public static class OptimizeSteamP2PPollingPatch
    {
        // SteamNetworkConnection sends every packet on Steam channel 0, yet vanilla asks Steam about all 8 listen
        // channels on both the client and the game server interface at 60 Hz, and each empty check costs ~25 us.
        private const int GameplayChannel = 0;
        private const double ReadIntervalSeconds = 1.0 / 60.0;
        private const float IdleChannelSweepSeconds = 0.25f;

        private static readonly AccessTools.FieldRef<Networking, List<int>> ListenChannelsRef =
            AccessTools.FieldRefAccess<Networking, List<int>>("ListenChannels");

        private static readonly AccessTools.FieldRef<Networking, Stopwatch> UpdateTimerRef =
            AccessTools.FieldRefAccess<Networking, Stopwatch>("UpdateTimer");

        private static readonly Func<Networking, int, bool> ReadP2PPacket =
            AccessTools.MethodDelegate<Func<Networking, int, bool>>(AccessTools.Method(typeof(Networking), "ReadP2PPacket"));

        private static readonly BoolConVar? UseSteamServerConVar =
            AccessTools.Field(typeof(NetworkManagerSystemSteam), "cvSteamP2PUseSteamServer")?.GetValue(null) as BoolConVar;

        private static float nextClientSweepTime;
        private static float nextServerSweepTime;

        private static bool Prefix(Networking __instance)
        {
            if (__instance.OnP2PData == null)
            {
                return false;
            }

            List<int> listenChannels = ListenChannelsRef(__instance);
            if (SteamP2PWorker.TryDispatch(__instance, listenChannels))
            {
                return false;
            }

            Stopwatch updateTimer = UpdateTimerRef(__instance);
            if (updateTimer.Elapsed.TotalSeconds < ReadIntervalSeconds)
            {
                return false;
            }

            updateTimer.Restart();

            bool isServerInterface = Server.Instance != null && Server.Instance.Networking == __instance;
            bool carriesGameplay = !isServerInterface || (UseSteamServerConVar?.value ?? true);

            float now = Time.unscaledTime;
            ref float nextSweepTime = ref isServerInterface ? ref nextServerSweepTime : ref nextClientSweepTime;
            bool sweepIdleChannels = now >= nextSweepTime;
            if (sweepIdleChannels)
            {
                nextSweepTime = now + IdleChannelSweepSeconds;
            }

            for (int i = 0; i < listenChannels.Count; i++)
            {
                int channel = listenChannels[i];
                if (!sweepIdleChannels && (!carriesGameplay || channel != GameplayChannel))
                {
                    continue;
                }

                while (__instance.OnP2PData != null && ReadP2PPacket(__instance, channel))
                {
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Networking), nameof(Networking.SendP2PPacket))]
    public static class RouteSteamP2PSendPatch
    {
        private static bool Prefix(
            Networking __instance,
            ulong steamid,
            byte[] data,
            int length,
            Networking.SendType eP2PSendType,
            int nChannel,
            ref bool __result)
        {
            if (SteamP2PWorker.DirectCalls)
            {
                return true;
            }

            SteamP2PWorker? worker = SteamP2PWorker.Active;
            if (worker == null || !SteamP2PWorker.IsBoundTo(__instance))
            {
                return true;
            }

            worker.EnqueueSend(steamid, data, length, eP2PSendType, nChannel);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Networking), nameof(Networking.CloseSession))]
    public static class RouteSteamP2PCloseSessionPatch
    {
        private static bool Prefix(Networking __instance, ulong steamId, ref bool __result)
        {
            if (SteamP2PWorker.DirectCalls)
            {
                return true;
            }

            SteamP2PWorker? worker = SteamP2PWorker.Active;
            if (worker == null || !SteamP2PWorker.IsBoundTo(__instance))
            {
                return true;
            }

            worker.EnqueueClose(steamId);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Networking), nameof(Networking.CloseP2PSessionWithUser))]
    public static class RouteSteamP2PCloseSessionWithUserPatch
    {
        private static bool Prefix(Networking __instance, ulong steamId)
        {
            if (SteamP2PWorker.DirectCalls)
            {
                return true;
            }

            SteamP2PWorker? worker = SteamP2PWorker.Active;
            if (worker == null || !SteamP2PWorker.IsBoundTo(__instance))
            {
                return true;
            }

            worker.EnqueueClose(steamId);
            return false;
        }
    }

    [HarmonyPatch(typeof(Networking), "onP2PConnectionRequest")]
    public static class RouteSteamP2PSessionRequestPatch
    {
        // Joining creates the connection inside this callback. That constructor closes any stale session and sends
        // the first reply, and Facepunch accepts only after OnIncomingConnection returns. Running those calls later
        // on the I/O thread lets the peer connect twice, so the second AddPlayer hits a connection that is already ready.
        private static void Prefix(Networking __instance, ref bool __state)
        {
            __state = SteamP2PWorker.Active != null && SteamP2PWorker.IsBoundTo(__instance);
            if (__state)
            {
                SteamP2PWorker.BeginDirectCalls();
            }
        }

        private static void Postfix(bool __state)
        {
            if (__state)
            {
                SteamP2PWorker.EndDirectCalls();
            }
        }
    }

    [HarmonyPatch(typeof(Networking), nameof(Networking.Dispose))]
    public static class StopSteamP2PWorkerOnDisposePatch
    {
        private static void Prefix(Networking __instance)
        {
            if (SteamP2PWorker.IsBoundTo(__instance))
            {
                SteamP2PWorker.ReleaseBinding(dispatchRemaining: false);
            }
        }
    }

    internal static class SteamSessionCheckThrottle
    {
        public const float IntervalSeconds = 0.25f;

        public static bool ShouldRun(ref float nextRunTime)
        {
            float now = Time.unscaledTime;
            if (now < nextRunTime)
            {
                return false;
            }

            nextRunTime = now + IntervalSeconds;
            return true;
        }
    }

    [HarmonyPatch(typeof(NetworkManagerSystemSteam), "UpdateServer")]
    public static class ThrottleSteamServerSessionChecksPatch
    {
        private static float nextRunTime;

        private static bool Prefix()
        {
            return SteamSessionCheckThrottle.ShouldRun(ref nextRunTime);
        }
    }

    [HarmonyPatch(typeof(NetworkManagerSystemSteam), "UpdateCheckInactiveConnections")]
    public static class ThrottleSteamClientSessionChecksPatch
    {
        private static float nextRunTime;

        private static bool Prefix()
        {
            return SteamSessionCheckThrottle.ShouldRun(ref nextRunTime);
        }
    }

    [HarmonyPatch(typeof(SteamworksLobbyManager), "StaticUpdate")]
    public static class ThrottleSteamLobbyOwnerCheckPatch
    {
        private static float nextRunTime;

        private static bool Prefix()
        {
            return SteamSessionCheckThrottle.ShouldRun(ref nextRunTime);
        }
    }
}
