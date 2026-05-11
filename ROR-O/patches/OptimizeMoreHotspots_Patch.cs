using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KinematicCharacterController;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using static ROR_O.patches.OptimizeGetComponentPatchTools;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(NetworkTransformVisualizer), "FixedUpdate")]
    public static class OptimizeNetworkTransformVisualizerPatch
    {
        private static readonly MethodInfo GameObjectGetRigidbodyMethod = MakeGameObjectGetComponentMethod(typeof(Rigidbody));
        private static readonly MethodInfo GameObjectGetRigidbody2DMethod = MakeGameObjectGetComponentMethod(typeof(Rigidbody2D));

        private static readonly MethodInfo CachedRigidbodyMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetRigidbody));

        private static readonly MethodInfo CachedRigidbody2DMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetRigidbody2D));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, GameObjectGetRigidbodyMethod, CachedRigidbodyMethod)
                    || ReplaceCall(instruction, GameObjectGetRigidbody2DMethod, CachedRigidbody2DMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"NetworkTransformVisualizer.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(AllPlayersTrigger), nameof(AllPlayersTrigger.FixedUpdate))]
    public static class OptimizeAllPlayersTriggerPatch
    {
        private static readonly MethodInfo ComponentGetCharacterBodyMethod = MakeComponentGetComponentMethod(typeof(CharacterBody));
        private static readonly MethodInfo CachedCharacterBodyMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterBodyFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetCharacterBodyMethod, CachedCharacterBodyMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"AllPlayersTrigger.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(MultiBodyTrigger), nameof(MultiBodyTrigger.FixedUpdate))]
    public static class OptimizeMultiBodyTriggerPatch
    {
        private static readonly MethodInfo ComponentGetCharacterBodyMethod = MakeComponentGetComponentMethod(typeof(CharacterBody));
        private static readonly MethodInfo CachedCharacterBodyMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterBodyFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetCharacterBodyMethod, CachedCharacterBodyMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"MultiBodyTrigger.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(GroundZoneController), nameof(GroundZoneController.FixedUpdate))]
    public static class OptimizeGroundZoneControllerPatch
    {
        private static readonly MethodInfo ComponentGetRigidbodyMotorMethod = MakeComponentGetComponentMethod(typeof(RigidbodyMotor));
        private static readonly MethodInfo ComponentGetKinematicCharacterMotorMethod = MakeComponentGetComponentMethod(typeof(KinematicCharacterMotor));

        private static readonly MethodInfo CachedRigidbodyMotorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetRigidbodyMotor));

        private static readonly MethodInfo CachedKinematicCharacterMotorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetKinematicCharacterMotor));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetRigidbodyMotorMethod, CachedRigidbodyMotorMethod)
                    || ReplaceCall(instruction, ComponentGetKinematicCharacterMotorMethod, CachedKinematicCharacterMotorMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"GroundZoneController.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(VerticalLift), nameof(VerticalLift.FixedUpdate))]
    public static class OptimizeVerticalLiftPatch
    {
        private static readonly MethodInfo ComponentGetCharacterMotorMethod = MakeComponentGetComponentMethod(typeof(CharacterMotor));
        private static readonly MethodInfo CachedCharacterMotorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterMotor));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetCharacterMotorMethod, CachedCharacterMotorMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"VerticalLift.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }
}
