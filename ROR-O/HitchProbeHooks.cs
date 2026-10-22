using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.CharacterAI;
using RoR2.Navigation;

namespace ROR_O
{
    // Times the methods whose totals the Mono profiler either hides inside native code
    // or cannot place on a single frame. Each name is the type and method to open.
    internal static class HitchProbeHooks
    {
        private static bool installed;

        internal static void Install(Harmony harmony)
        {
            if (installed)
            {
                return;
            }

            installed = true;
            Try(harmony, typeof(SphereSearch), nameof(SphereSearch.RefreshCandidates), "Sphere");
            Try(harmony, typeof(SphereSearch), nameof(SphereSearch.OrderCandidatesByDistance), "Sort");
            Try(harmony, typeof(SphereSearch), nameof(SphereSearch.FilterCandidatesByDistinctHurtBoxEntities), "Distinct");
            Try(harmony, typeof(SphereSearch), nameof(SphereSearch.FilterCandidatesByHurtBoxTeam), "Team");
            Try(harmony, typeof(RadialForce), "SearchForTargets", "Radial");
            Try(harmony, typeof(RadialForce), "ApplyPullToHurtBox", "Pull");
            Try(harmony, typeof(EquipmentSlot), "FireTeamWarCry", "WarCry");
            Try(harmony, typeof(EffectManager), nameof(EffectManager.SpawnEffect), "Effect", typeof(EffectIndex), typeof(EffectData), typeof(bool));
            Try(harmony, typeof(BaseAI), nameof(BaseAI.ManagedFixedUpdate), "Ai");
            Try(harmony, typeof(NodeGraphNavigationSystem), "UpdateAgent", "Path");
        }

        internal static void Uninstall(Harmony harmony)
        {
            if (!installed)
            {
                return;
            }

            harmony.UnpatchSelf();
            installed = false;
        }

        private static void Try(Harmony harmony, Type type, string method, string probe, params Type[] args)
        {
            MethodInfo target = args.Length == 0
                ? AccessTools.Method(type, method)
                : AccessTools.Method(type, method, args);
            if (target == null)
            {
                RORO.GlobalLogger?.LogWarning($"Hitch probe skipped {type.Name}.{method}: method was not found.");
                return;
            }

            MethodInfo prefix = AccessTools.Method(typeof(Probe), probe + "Prefix");
            MethodInfo finalizer = AccessTools.Method(typeof(Probe), probe + "Finalizer");
            harmony.Patch(target, new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
        }

        private static class Probe
        {
            internal static void SpherePrefix(out long __state) => Begin(out __state);

            internal static Exception SphereFinalizer(long __state, Exception __exception) => End(HitchProbes.Id.SphereRefresh, __state, __exception);

            internal static void SortPrefix(out long __state) => Begin(out __state);

            internal static Exception SortFinalizer(long __state, Exception __exception) => End(HitchProbes.Id.SphereSort, __state, __exception);

            internal static void DistinctPrefix(out long __state) => Begin(out __state);

            internal static Exception DistinctFinalizer(long __state, Exception __exception) => End(HitchProbes.Id.SphereDistinct, __state, __exception);

            internal static void TeamPrefix(out long __state) => Begin(out __state);

            internal static Exception TeamFinalizer(long __state, Exception __exception) => End(HitchProbes.Id.SphereTeam, __state, __exception);

            internal static void RadialPrefix(out long __state) => Begin(out __state);

            internal static Exception RadialFinalizer(long __state, Exception __exception) => End(HitchProbes.Id.RadialSearch, __state, __exception);

            internal static void PullPrefix(out long __state) => Begin(out __state);

            internal static Exception PullFinalizer(long __state, Exception __exception) => End(HitchProbes.Id.RadialPull, __state, __exception);

            internal static void WarCryPrefix(out long __state) => Begin(out __state);

            internal static Exception WarCryFinalizer(long __state, Exception __exception) => End(HitchProbes.Id.WarCry, __state, __exception);

            internal static void EffectPrefix(out long __state) => Begin(out __state);

            internal static Exception EffectFinalizer(long __state, Exception __exception) => End(HitchProbes.Id.SpawnEffect, __state, __exception);

            internal static void AiPrefix(out long __state) => Begin(out __state);

            internal static Exception AiFinalizer(long __state, Exception __exception) => End(HitchProbes.Id.AiTick, __state, __exception);

            internal static void PathPrefix(out long __state) => Begin(out __state);

            internal static Exception PathFinalizer(long __state, Exception __exception) => End(HitchProbes.Id.PathAgent, __state, __exception);

            private static void Begin(out long state)
            {
                state = HitchProbes.Armed ? Stopwatch.GetTimestamp() : 0L;
            }

            private static Exception End(HitchProbes.Id id, long state, Exception exception)
            {
                if (state != 0L)
                {
                    HitchProbes.Add(id, Stopwatch.GetTimestamp() - state);
                }

                return exception;
            }
        }
    }
}
