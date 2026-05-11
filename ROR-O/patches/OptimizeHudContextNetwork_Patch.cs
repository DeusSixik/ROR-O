using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using static ROR_O.patches.OptimizeGetComponentPatchTools;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(ContextManager), nameof(ContextManager.Update))]
    public static class OptimizeContextManagerPatch
    {
        private static readonly MethodInfo GameObjectGetInteractionDriverMethod = MakeGameObjectGetComponentMethod(typeof(InteractionDriver));
        private static readonly MethodInfo GameObjectGetInteractableMethod = MakeGameObjectGetComponentMethod(typeof(IInteractable));
        private static readonly MethodInfo GameObjectGetInspectableMethod = MakeGameObjectGetComponentMethod(typeof(IInspectable));
        private static readonly MethodInfo ComponentGetPlayerCharacterMasterControllerMethod = MakeComponentGetComponentMethod(typeof(PlayerCharacterMasterController));

        private static readonly MethodInfo CachedInteractionDriverMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetInteractionDriver));

        private static readonly MethodInfo CachedInteractableMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetInteractable));

        private static readonly MethodInfo CachedInspectableMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetInspectable));

        private static readonly MethodInfo CachedPlayerCharacterMasterControllerMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetPlayerCharacterMasterController));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, GameObjectGetInteractionDriverMethod, CachedInteractionDriverMethod)
                    || ReplaceCall(instruction, GameObjectGetInteractableMethod, CachedInteractableMethod)
                    || ReplaceCall(instruction, GameObjectGetInspectableMethod, CachedInspectableMethod)
                    || ReplaceCall(instruction, ComponentGetPlayerCharacterMasterControllerMethod, CachedPlayerCharacterMasterControllerMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"ContextManager.Update optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(HUD), nameof(HUD.Update))]
    public static class OptimizeHUDPatch
    {
        private static readonly MethodInfo GameObjectGetCharacterBodyMethod = MakeGameObjectGetComponentMethod(typeof(CharacterBody));
        private static readonly MethodInfo GameObjectGetEquipmentSlotMethod = MakeGameObjectGetComponentMethod(typeof(EquipmentSlot));
        private static readonly MethodInfo GameObjectGetHealthComponentMethod = MakeGameObjectGetComponentMethod(typeof(HealthComponent));
        private static readonly MethodInfo GameObjectGetSkillLocatorMethod = MakeGameObjectGetComponentMethod(typeof(SkillLocator));
        private static readonly MethodInfo ComponentGetPlayerCharacterMasterControllerMethod = MakeComponentGetComponentMethod(typeof(PlayerCharacterMasterController));

        private static readonly MethodInfo CachedCharacterBodyMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterBody));

        private static readonly MethodInfo CachedEquipmentSlotMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetEquipmentSlot));

        private static readonly MethodInfo CachedHealthComponentMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetHealthComponent));

        private static readonly MethodInfo CachedPlayerCharacterMasterControllerMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetPlayerCharacterMasterController));

        private static readonly MethodInfo CachedSkillLocatorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetSkillLocator));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, GameObjectGetCharacterBodyMethod, CachedCharacterBodyMethod)
                    || ReplaceCall(instruction, GameObjectGetEquipmentSlotMethod, CachedEquipmentSlotMethod)
                    || ReplaceCall(instruction, GameObjectGetHealthComponentMethod, CachedHealthComponentMethod)
                    || ReplaceCall(instruction, GameObjectGetSkillLocatorMethod, CachedSkillLocatorMethod)
                    || ReplaceCall(instruction, ComponentGetPlayerCharacterMasterControllerMethod, CachedPlayerCharacterMasterControllerMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"HUD.Update optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(NetworkProximityChecker), "Update")]
    public static class OptimizeNetworkProximityCheckerPatch
    {
        private static readonly MethodInfo ComponentGetNetworkIdentityMethod = MakeComponentGetComponentMethod(typeof(NetworkIdentity));
        private static readonly MethodInfo CachedNetworkIdentityMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetNetworkIdentity));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetNetworkIdentityMethod, CachedNetworkIdentityMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"NetworkProximityChecker.Update optimized: cached GetComponent calls={replacements}");
        }
    }
}

namespace ROR_O.patches
{
    internal static class OptimizeGetComponentPatchTools
    {
        private static readonly MethodInfo ComponentGetComponentDefinition = typeof(Component)
            .GetMethod(nameof(Component.GetComponent), Type.EmptyTypes)!
            .GetGenericMethodDefinition();

        private static readonly MethodInfo GameObjectGetComponentDefinition = typeof(GameObject)
            .GetMethod(nameof(GameObject.GetComponent), Type.EmptyTypes)!
            .GetGenericMethodDefinition();

        public static MethodInfo MakeComponentGetComponentMethod(Type componentType) =>
            ComponentGetComponentDefinition.MakeGenericMethod(componentType);

        public static MethodInfo MakeGameObjectGetComponentMethod(Type componentType) =>
            GameObjectGetComponentDefinition.MakeGenericMethod(componentType);

        public static bool ReplaceCall(CodeInstruction instruction, MethodInfo source, MethodInfo target)
        {
            if (!instruction.Calls(source))
            {
                return false;
            }

            instruction.opcode = OpCodes.Call;
            instruction.operand = target;
            return true;
        }
    }
}
