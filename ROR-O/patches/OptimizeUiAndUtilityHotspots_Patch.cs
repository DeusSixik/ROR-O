using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.PostProcessing;
using RoR2.UI;
using UnityEngine;
using static ROR_O.patches.OptimizeGetComponentPatchTools;
using static ROR_O.patches.OptimizeUiAndUtilityPatchTools;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(ProximityHighlight), nameof(ProximityHighlight.FixedUpdate))]
    public static class OptimizeProximityHighlightPatch
    {
        private static readonly MethodInfo ComponentGetInteractionDriverMethod = MakeComponentGetComponentMethod(typeof(InteractionDriver));
        private static readonly MethodInfo CachedInteractionDriverMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetInteractionDriverFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceInstructions(instructions, ComponentGetInteractionDriverMethod, CachedInteractionDriverMethod,
                "ProximityHighlight.FixedUpdate");
        }
    }

    [HarmonyPatch(typeof(ScreenDamage), nameof(ScreenDamage.Update))]
    public static class OptimizeScreenDamagePatch
    {
        private static readonly MethodInfo GameObjectGetHealthComponentMethod = MakeGameObjectGetComponentMethod(typeof(HealthComponent));
        private static readonly MethodInfo CachedHealthComponentMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetHealthComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceInstructions(instructions, GameObjectGetHealthComponentMethod, CachedHealthComponentMethod,
                "ScreenDamage.Update");
        }
    }

    [HarmonyPatch(typeof(NetworkUser), nameof(NetworkUser.Update))]
    public static class OptimizeNetworkUserPatch
    {
        private static readonly MethodInfo GameObjectGetCharacterMasterMethod = MakeGameObjectGetComponentMethod(typeof(CharacterMaster));
        private static readonly MethodInfo CachedCharacterMasterMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterMaster));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceInstructions(instructions, GameObjectGetCharacterMasterMethod, CachedCharacterMasterMethod,
                "NetworkUser.Update");
        }
    }

    [HarmonyPatch(typeof(DisplayStock), nameof(DisplayStock.Update))]
    public static class OptimizeDisplayStockPatch
    {
        private static readonly MethodInfo ComponentGetSkillLocatorMethod = MakeComponentGetComponentMethod(typeof(SkillLocator));
        private static readonly MethodInfo CachedSkillLocatorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetSkillLocatorFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceInstructions(instructions, ComponentGetSkillLocatorMethod, CachedSkillLocatorMethod,
                "DisplayStock.Update");
        }
    }

    [HarmonyPatch(typeof(SniperScopeChargeIndicatorController), nameof(SniperScopeChargeIndicatorController.FixedUpdate))]
    public static class OptimizeSniperScopeChargeIndicatorPatch
    {
        private static readonly MethodInfo ComponentGetSkillLocatorMethod = MakeComponentGetComponentMethod(typeof(SkillLocator));
        private static readonly MethodInfo CachedSkillLocatorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetSkillLocatorFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceInstructions(instructions, ComponentGetSkillLocatorMethod, CachedSkillLocatorMethod,
                "SniperScopeChargeIndicatorController.FixedUpdate");
        }
    }

    [HarmonyPatch(typeof(FireAuraController), nameof(FireAuraController.FixedUpdate))]
    public static class OptimizeFireAuraControllerPatch
    {
        private static readonly MethodInfo GameObjectGetCharacterBodyMethod = MakeGameObjectGetComponentMethod(typeof(CharacterBody));
        private static readonly MethodInfo CachedCharacterBodyMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterBody));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceInstructions(instructions, GameObjectGetCharacterBodyMethod, CachedCharacterBodyMethod,
                "FireAuraController.FixedUpdate");
        }
    }

    [HarmonyPatch(typeof(GhostGunController), nameof(GhostGunController.FixedUpdate))]
    public static class OptimizeGhostGunControllerPatch
    {
        private static readonly MethodInfo GameObjectGetInputBankTestMethod = MakeGameObjectGetComponentMethod(typeof(InputBankTest));
        private static readonly MethodInfo CachedInputBankTestMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetInputBankTest));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceInstructions(instructions, GameObjectGetInputBankTestMethod, CachedInputBankTestMethod,
                "GhostGunController.FixedUpdate");
        }
    }

    [HarmonyPatch(typeof(OilController), nameof(OilController.Update))]
    public static class OptimizeOilControllerPatch
    {
        private static readonly MethodInfo GameObjectGetOilGhostControllerMethod = MakeGameObjectGetComponentMethod(typeof(OilGhostController));
        private static readonly MethodInfo CachedOilGhostControllerMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetOilGhostController));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceInstructions(instructions, GameObjectGetOilGhostControllerMethod, CachedOilGhostControllerMethod,
                "OilController.Update");
        }
    }

    [HarmonyPatch(typeof(MusicController), nameof(MusicController.LateUpdate))]
    public static class OptimizeMusicControllerPatch
    {
        private static readonly MethodInfo GameObjectGetCharacterBodyMethod = MakeGameObjectGetComponentMethod(typeof(CharacterBody));
        private static readonly MethodInfo CachedCharacterBodyMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterBody));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceInstructions(instructions, GameObjectGetCharacterBodyMethod, CachedCharacterBodyMethod,
                "MusicController.LateUpdate");
        }
    }

    [HarmonyPatch(typeof(BazaarController), nameof(BazaarController.Update))]
    public static class OptimizeBazaarControllerPatch
    {
        private static readonly MethodInfo GameObjectGetInputBankTestMethod = MakeGameObjectGetComponentMethod(typeof(InputBankTest));
        private static readonly MethodInfo GameObjectGetCharacterBodyMethod = MakeGameObjectGetComponentMethod(typeof(CharacterBody));
        private static readonly MethodInfo CachedInputBankTestMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetInputBankTest));
        private static readonly MethodInfo CachedCharacterBodyMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterBody));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, GameObjectGetInputBankTestMethod, CachedInputBankTestMethod)
                    || ReplaceCall(instruction, GameObjectGetCharacterBodyMethod, CachedCharacterBodyMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"BazaarController.Update optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(HelfireController), nameof(HelfireController.LateUpdate))]
    public static class OptimizeHelfireControllerPatch
    {
        private static readonly MethodInfo ComponentGetCameraTargetParamsMethod = MakeComponentGetComponentMethod(typeof(CameraTargetParams));
        private static readonly MethodInfo CachedCameraTargetParamsMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCameraTargetParamsFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceInstructions(instructions, ComponentGetCameraTargetParamsMethod, CachedCameraTargetParamsMethod,
                "HelfireController.LateUpdate");
        }
    }

    [HarmonyPatch(typeof(Nameplate), nameof(Nameplate.LateUpdate))]
    public static class OptimizeNameplatePatch
    {
        private static readonly MethodInfo ComponentGetPlayerCharacterMasterControllerMethod = MakeComponentGetComponentMethod(typeof(PlayerCharacterMasterController));
        private static readonly MethodInfo GameObjectGetNetworkUserMethod = MakeGameObjectGetComponentMethod(typeof(NetworkUser));
        private static readonly MethodInfo CachedPlayerCharacterMasterControllerMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetPlayerCharacterMasterController));
        private static readonly MethodInfo CachedNetworkUserMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetNetworkUser));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetPlayerCharacterMasterControllerMethod, CachedPlayerCharacterMasterControllerMethod)
                    || ReplaceCall(instruction, GameObjectGetNetworkUserMethod, CachedNetworkUserMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"Nameplate.LateUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    internal static class OptimizeUiAndUtilityPatchTools
    {
        internal static IEnumerable<CodeInstruction> ReplaceInstructions(
            IEnumerable<CodeInstruction> instructions,
            MethodInfo source,
            MethodInfo target,
            string label)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, source, target))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"{label} optimized: cached GetComponent calls={replacements}");
        }
    }
}
