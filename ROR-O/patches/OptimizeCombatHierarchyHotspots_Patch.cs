using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using static ROR_O.patches.OptimizeGetComponentPatchTools;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(TitanRockController), nameof(TitanRockController.FixedUpdate))]
    public static class OptimizeTitanRockControllerPatch
    {
        private static readonly MethodInfo ComponentGetParticleSystemsInChildrenMethod = MakeComponentGetComponentsInChildrenMethod(typeof(ParticleSystem));
        private static readonly MethodInfo ComponentGetLightsInChildrenMethod = MakeComponentGetComponentsInChildrenMethod(typeof(Light));
        private static readonly MethodInfo ComponentGetParticleSystemMethod = MakeComponentGetComponentMethod(typeof(ParticleSystem));
        private static readonly MethodInfo TransformFindMethod = AccessTools.Method(typeof(Transform), nameof(Transform.Find), new[] { typeof(string) });

        private static readonly MethodInfo CachedParticleSystemsInChildrenMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetParticleSystemsInChildren),
            new[] { typeof(Component) });

        private static readonly MethodInfo CachedLightsInChildrenMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetLightsInChildren));

        private static readonly MethodInfo CachedParticleSystemMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetParticleSystem));

        private static readonly MethodInfo CachedTransformFindMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.FindTransform));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetParticleSystemsInChildrenMethod, CachedParticleSystemsInChildrenMethod)
                    || ReplaceCall(instruction, ComponentGetLightsInChildrenMethod, CachedLightsInChildrenMethod)
                    || ReplaceCall(instruction, ComponentGetParticleSystemMethod, CachedParticleSystemMethod)
                    || ReplaceCall(instruction, TransformFindMethod, CachedTransformFindMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"TitanRockController.FixedUpdate optimized: cached hierarchy calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(TalismanAnimator), nameof(TalismanAnimator.FixedUpdate))]
    public static class OptimizeTalismanAnimatorPatch
    {
        private static readonly MethodInfo GameObjectGetParticleSystemsInChildrenMethod = MakeGameObjectGetComponentsInChildrenMethod(typeof(ParticleSystem));
        private static readonly MethodInfo CachedParticleSystemsInChildrenMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetParticleSystemsInChildren),
            new[] { typeof(GameObject) });

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, GameObjectGetParticleSystemsInChildrenMethod, CachedParticleSystemsInChildrenMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"TalismanAnimator.FixedUpdate optimized: cached hierarchy calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(GravCubeController), nameof(GravCubeController.Update))]
    public static class OptimizeGravCubeControllerPatch
    {
        private static readonly MethodInfo GameObjectGetAnimatorInChildrenMethod = MakeGameObjectGetComponentInChildrenMethod(typeof(Animator));

        private static readonly MethodInfo CachedAnimatorInChildrenMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetAnimatorInChildren));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, GameObjectGetAnimatorInChildrenMethod, CachedAnimatorInChildrenMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"GravCubeController.Update optimized: cached hierarchy calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.VagrantMonster.Weapon.JellyBarrage), nameof(EntityStates.VagrantMonster.Weapon.JellyBarrage.FixedUpdate))]
    public static class OptimizeJellyBarragePatch
    {
        private static readonly MethodInfo ChildLocatorFindChildMethod = AccessTools.Method(
            typeof(ChildLocator),
            nameof(ChildLocator.FindChild),
            new[] { typeof(string) });
        private static readonly MethodInfo CachedChildLocatorFindMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.FindChild),
            new[] { typeof(ChildLocator), typeof(string) });

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ChildLocatorFindChildMethod, CachedChildLocatorFindMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"JellyBarrage.FixedUpdate optimized: cached hierarchy calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.VagrantMonster.Weapon.JellyStorm), nameof(EntityStates.VagrantMonster.Weapon.JellyStorm.FixedUpdate))]
    public static class OptimizeJellyStormPatch
    {
        private static readonly MethodInfo ChildLocatorFindChildMethod = AccessTools.Method(
            typeof(ChildLocator),
            nameof(ChildLocator.FindChild),
            new[] { typeof(string) });
        private static readonly MethodInfo CachedChildLocatorFindMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.FindChild),
            new[] { typeof(ChildLocator), typeof(string) });

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ChildLocatorFindChildMethod, CachedChildLocatorFindMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"JellyStorm.FixedUpdate optimized: cached hierarchy calls={replacements}");
        }
    }
}
