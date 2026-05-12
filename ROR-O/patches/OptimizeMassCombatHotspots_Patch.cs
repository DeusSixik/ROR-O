using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using static ROR_O.patches.OptimizeGetComponentPatchTools;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(EntityStates.FriendUnit.KineticAura), nameof(EntityStates.FriendUnit.KineticAura.FixedUpdate))]
    public static class OptimizeKineticAuraPatch
    {
        private static readonly MethodInfo ComponentGetCharacterMotorMethod = MakeComponentGetComponentMethod(typeof(CharacterMotor));
        private static readonly MethodInfo ComponentGetRigidbodyMethod = MakeComponentGetComponentMethod(typeof(Rigidbody));

        private static readonly MethodInfo CachedCharacterMotorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterMotor),
            new[] { typeof(Component) });

        private static readonly MethodInfo CachedRigidbodyMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetRigidbodyFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetCharacterMotorMethod, CachedCharacterMotorMethod)
                    || ReplaceCall(instruction, ComponentGetRigidbodyMethod, CachedRigidbodyMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"KineticAura.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.Toolbot.ToolbotDash), nameof(EntityStates.Toolbot.ToolbotDash.FixedUpdate))]
    public static class OptimizeToolbotDashPatch
    {
        private static readonly MethodInfo ComponentGetCharacterMotorMethod = MakeComponentGetComponentMethod(typeof(CharacterMotor));
        private static readonly MethodInfo ComponentGetRigidbodyMethod = MakeComponentGetComponentMethod(typeof(Rigidbody));

        private static readonly MethodInfo CachedCharacterMotorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterMotor),
            new[] { typeof(Component) });

        private static readonly MethodInfo CachedRigidbodyMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetRigidbodyFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetCharacterMotorMethod, CachedCharacterMotorMethod)
                    || ReplaceCall(instruction, ComponentGetRigidbodyMethod, CachedRigidbodyMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"ToolbotDash.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(SoulSearchController), nameof(SoulSearchController.FixedUpdate))]
    public static class OptimizeSoulSearchControllerPatch
    {
        private static readonly MethodInfo GameObjectGetHuntressTrackerMethod = MakeGameObjectGetComponentMethod(typeof(HuntressTracker));
        private static readonly MethodInfo ComponentGetHealthComponentMethod = MakeComponentGetComponentMethod(typeof(HealthComponent));

        private static readonly MethodInfo CachedHuntressTrackerMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetHuntressTracker));

        private static readonly MethodInfo CachedHealthComponentMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetHealthComponentFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, GameObjectGetHuntressTrackerMethod, CachedHuntressTrackerMethod)
                    || ReplaceCall(instruction, ComponentGetHealthComponentMethod, CachedHealthComponentMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"SoulSearchController.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.FalseSonBoss.OverloadSpike), nameof(EntityStates.FalseSonBoss.OverloadSpike.FixedUpdate))]
    public static class OptimizeOverloadSpikePatch
    {
        private static readonly MethodInfo ComponentGetFalseSonBossControllerMethod = MakeComponentGetComponentMethod(typeof(FalseSonBossController));
        private static readonly MethodInfo CachedFalseSonBossControllerMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetFalseSonBossControllerFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetFalseSonBossControllerMethod, CachedFalseSonBossControllerMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"OverloadSpike.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }
}
