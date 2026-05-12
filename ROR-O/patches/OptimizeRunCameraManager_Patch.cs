using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(RunCameraManager), nameof(RunCameraManager.Update))]
    public static class OptimizeRunCameraManagerPatch
    {
        private const string PopOutPanelContainerName = "PopoutPanelContainer";

        private static readonly MethodInfo GameObjectFindMethod = AccessTools.Method(
            typeof(GameObject),
            nameof(GameObject.Find),
            new[] { typeof(string) });

        private static readonly MethodInfo GetRectTransformMethod = typeof(GameObject)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == nameof(GameObject.GetComponent)
                              && method.IsGenericMethodDefinition
                              && method.GetGenericArguments().Length == 1
                              && method.GetParameters().Length == 0)
            .MakeGenericMethod(typeof(RectTransform));

        private static readonly MethodInfo CachedFindMethod = AccessTools.Method(
            typeof(OptimizeRunCameraManagerPatch),
            nameof(GetCachedPopOutContainer));

        private static readonly MethodInfo CachedRectTransformMethod = AccessTools.Method(
            typeof(OptimizeRunCameraManagerPatch),
            nameof(GetCachedRectTransform));

        private static GameObject? cachedPopOutContainer;
        private static RectTransform? cachedPopOutRectTransform;

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int findReplacements = 0;
            int rectTransformReplacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(GameObjectFindMethod))
                {
                    instruction.operand = CachedFindMethod;
                    findReplacements++;
                }
                else if (instruction.Calls(GetRectTransformMethod))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = CachedRectTransformMethod;
                    rectTransformReplacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo(
                $"RunCameraManager.Update optimized: GameObject.Find={findReplacements}, RectTransform.GetComponent={rectTransformReplacements}");
        }

        private static GameObject? GetCachedPopOutContainer(string objectName)
        {
            if (!string.Equals(objectName, PopOutPanelContainerName))
            {
                return GameObject.Find(objectName);
            }

            if (!cachedPopOutContainer)
            {
                cachedPopOutContainer = GameObject.Find(PopOutPanelContainerName);
                cachedPopOutRectTransform = cachedPopOutContainer
                    ? cachedPopOutContainer.GetComponent<RectTransform>()
                    : null;
            }

            return cachedPopOutContainer;
        }

        private static RectTransform? GetCachedRectTransform(GameObject gameObject)
        {
            if (!gameObject)
            {
                cachedPopOutContainer = null;
                cachedPopOutRectTransform = null;
                return null;
            }

            if (cachedPopOutContainer != gameObject || !cachedPopOutRectTransform)
            {
                cachedPopOutContainer = gameObject;
                cachedPopOutRectTransform = gameObject.GetComponent<RectTransform>();
            }

            return cachedPopOutRectTransform;
        }
    }
}
