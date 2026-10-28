using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using static ROR_O.patches.OptimizeGetComponentPatchTools;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(JailerTetherController), nameof(JailerTetherController.FixedUpdate))]
    public static class OptimizeJailerTetherControllerPatch
    {
        private static readonly MethodInfo GameObjectGetCharacterMotorMethod = MakeGameObjectGetComponentMethod(typeof(CharacterMotor));
        private static readonly MethodInfo GameObjectGetRigidbodyMethod = MakeGameObjectGetComponentMethod(typeof(Rigidbody));
        private static readonly MethodInfo CachedCharacterMotorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterMotor),
            new[] { typeof(GameObject) });
        private static readonly MethodInfo CachedRigidbodyMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetRigidbody));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, GameObjectGetCharacterMotorMethod, CachedCharacterMotorMethod)
                    || ReplaceCall(instruction, GameObjectGetRigidbodyMethod, CachedRigidbodyMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"JailerTetherController.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(TarTetherController), nameof(TarTetherController.FixedUpdate))]
    public static class OptimizeTarTetherControllerPatch
    {
        private static readonly MethodInfo GameObjectGetCharacterMotorMethod = MakeGameObjectGetComponentMethod(typeof(CharacterMotor));
        private static readonly MethodInfo GameObjectGetRigidbodyMethod = MakeGameObjectGetComponentMethod(typeof(Rigidbody));
        private static readonly MethodInfo CachedCharacterMotorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterMotor),
            new[] { typeof(GameObject) });
        private static readonly MethodInfo CachedRigidbodyMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetRigidbody));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, GameObjectGetCharacterMotorMethod, CachedCharacterMotorMethod)
                    || ReplaceCall(instruction, GameObjectGetRigidbodyMethod, CachedRigidbodyMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"TarTetherController.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(ProjectileDamageTrail), nameof(ProjectileDamageTrail.FixedUpdate))]
    public static class OptimizeProjectileDamageTrailPatch
    {
        private static readonly MethodInfo GameObjectGetDamageTrailMethod = MakeGameObjectGetComponentMethod(typeof(DamageTrail));
        private static readonly MethodInfo CachedDamageTrailMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetDamageTrail));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, GameObjectGetDamageTrailMethod, CachedDamageTrailMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"ProjectileDamageTrail.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(CleaverProjectile), nameof(CleaverProjectile.FixedUpdate))]
    public static class OptimizeCleaverProjectilePatch
    {
        private static readonly MethodInfo GameObjectGetChefControllerMethod = MakeGameObjectGetComponentMethod(typeof(ChefController));
        private static readonly MethodInfo CachedChefControllerMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetChefController));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, GameObjectGetChefControllerMethod, CachedChefControllerMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"CleaverProjectile.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(MissileController), nameof(MissileController.FixedUpdate))]
    public static class OptimizeMissileControllerPatch
    {
        private static readonly MethodInfo ComponentGetHealthComponentMethod = MakeComponentGetComponentMethod(typeof(HealthComponent));
        private static readonly MethodInfo CachedHealthComponentMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetHealthComponentFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetHealthComponentMethod, CachedHealthComponentMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"MissileController.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(DaggerController), nameof(DaggerController.FixedUpdate))]
    public static class OptimizeDaggerControllerPatch
    {
        private static readonly MethodInfo ComponentGetHealthComponentMethod = MakeComponentGetComponentMethod(typeof(HealthComponent));
        private static readonly MethodInfo CachedHealthComponentMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetHealthComponentFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetHealthComponentMethod, CachedHealthComponentMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"DaggerController.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }
}
