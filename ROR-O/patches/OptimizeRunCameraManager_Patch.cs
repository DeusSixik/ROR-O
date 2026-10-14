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
        private const string CharacterSelectUIMainPath = "RoR2/Base/UI/CharacterSelectUIMain.prefab";
        private const string CharacterSelectUILocalPath = "RoR2/Base/UI/CharacterSelectUILocal.prefab";

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

        private static readonly MethodInfo InstantiateGameObjectMethod = typeof(Object)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(method => method.Name == nameof(Object.Instantiate)
                              && method.IsGenericMethodDefinition
                              && method.GetParameters().Length == 1)
            .MakeGenericMethod(typeof(GameObject));

        private static readonly MethodInfo CachedFindMethod = AccessTools.Method(
            typeof(OptimizeRunCameraManagerPatch),
            nameof(GetCachedPopOutContainer));

        private static readonly MethodInfo CachedRectTransformMethod = AccessTools.Method(
            typeof(OptimizeRunCameraManagerPatch),
            nameof(GetCachedRectTransform));

        private static readonly MethodInfo EnsureMainUiMethod = AccessTools.Method(
            typeof(OptimizeRunCameraManagerPatch),
            nameof(EnsureCharacterSelectUIMain));

        private static readonly MethodInfo EnsureLocalUiMethod = AccessTools.Method(
            typeof(OptimizeRunCameraManagerPatch),
            nameof(EnsureCharacterSelectUILocal));

        private static readonly MethodInfo InstantiateIfPresentMethod = AccessTools.Method(
            typeof(OptimizeRunCameraManagerPatch),
            nameof(InstantiateIfPresent));

        private static GameObject? cachedPopOutContainer;
        private static RectTransform? cachedPopOutRectTransform;
        private static float nextMainUiLoadTime;
        private static float nextLocalUiLoadTime;
        private static bool loggedMissingMainUi;
        private static bool loggedMissingLocalUi;

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int findReplacements = 0;
            int rectTransformReplacements = 0;
            int uiReplacements = 0;
            int instantiateReplacements = 0;
            CodeInstruction? previous = null;

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
                else if (instruction.opcode == OpCodes.Ldfld && IsCharacterSelectField(instruction.operand, out bool localUi))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = localUi ? EnsureLocalUiMethod : EnsureMainUiMethod;
                    uiReplacements++;
                }
                else if (instruction.Calls(InstantiateGameObjectMethod)
                         && previous != null
                         && (ReferenceEquals(previous.operand, EnsureMainUiMethod)
                             || ReferenceEquals(previous.operand, EnsureLocalUiMethod)))
                {
                    instruction.operand = InstantiateIfPresentMethod;
                    instantiateReplacements++;
                }

                previous = instruction;
                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo(
                $"RunCameraManager.Update optimized: GameObject.Find={findReplacements}, RectTransform.GetComponent={rectTransformReplacements}, character select UI={uiReplacements}, guarded instantiate={instantiateReplacements}");
        }

        private static bool IsCharacterSelectField(object? operand, out bool localUi)
        {
            localUi = false;
            string? name = (operand as FieldInfo)?.Name;
            if (name == null && operand != null)
            {
                name = operand.ToString();
            }

            if (name == null)
            {
                return false;
            }

            if (name == nameof(RunCameraManager.CharacterSelectUIMain) || name.EndsWith("CharacterSelectUIMain"))
            {
                return true;
            }

            if (name == nameof(RunCameraManager.CharacterSelectUILocal) || name.EndsWith("CharacterSelectUILocal"))
            {
                localUi = true;
                return true;
            }

            return false;
        }

        // After a run the lobby manager can come back with an unloaded prefab reference.
        // Instantiate then throws every frame and the character select canvas is never stored.
        private static GameObject? EnsureCharacterSelectUIMain(RunCameraManager manager)
        {
            if (manager.CharacterSelectUIMain)
            {
                return manager.CharacterSelectUIMain;
            }

            if (Time.unscaledTime < nextMainUiLoadTime)
            {
                return null;
            }

            nextMainUiLoadTime = Time.unscaledTime + 1f;
            GameObject loaded = LegacyResourcesAPI.Load<GameObject>(CharacterSelectUIMainPath);
            if (!loaded)
            {
                if (!loggedMissingMainUi)
                {
                    loggedMissingMainUi = true;
                    RORO.GlobalLogger?.LogError($"Character select UI prefab could not be loaded: {CharacterSelectUIMainPath}");
                }

                return null;
            }

            manager.CharacterSelectUIMain = loaded;
            RORO.GlobalLogger?.LogInfo($"Reloaded missing character select UI prefab: {CharacterSelectUIMainPath}");
            return loaded;
        }

        private static GameObject? EnsureCharacterSelectUILocal(RunCameraManager manager)
        {
            if (manager.CharacterSelectUILocal)
            {
                return manager.CharacterSelectUILocal;
            }

            if (Time.unscaledTime < nextLocalUiLoadTime)
            {
                return null;
            }

            nextLocalUiLoadTime = Time.unscaledTime + 1f;
            GameObject loaded = LegacyResourcesAPI.Load<GameObject>(CharacterSelectUILocalPath);
            if (!loaded)
            {
                if (!loggedMissingLocalUi)
                {
                    loggedMissingLocalUi = true;
                    RORO.GlobalLogger?.LogError($"Character select UI prefab could not be loaded: {CharacterSelectUILocalPath}");
                }

                return null;
            }

            manager.CharacterSelectUILocal = loaded;
            RORO.GlobalLogger?.LogInfo($"Reloaded missing character select UI prefab: {CharacterSelectUILocalPath}");
            return loaded;
        }

        private static GameObject? InstantiateIfPresent(GameObject? prefab)
        {
            if (!prefab)
            {
                return null;
            }

            return Object.Instantiate(prefab);
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
