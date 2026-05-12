using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ROR_O.patches
{
    [HarmonyPatch]
    internal static class OptimizeDynamicBoneLateUpdatePatch
    {
        private sealed class DynamicBoneState
        {
            public int LastVisibleFrame = int.MinValue;
        }

        private static readonly Type? DynamicBoneType = AccessTools.TypeByName("DynamicBone");
        private static readonly MethodInfo? DynamicBoneLateUpdateMethod =
            DynamicBoneType != null ? AccessTools.DeclaredMethod(DynamicBoneType, "LateUpdate") : null;

        private static readonly FieldInfo? DynamicBoneRootField =
            DynamicBoneType != null ? AccessTools.Field(DynamicBoneType, "m_Root") : null;

        private static readonly FieldInfo? DynamicBoneNeverOptimizeField =
            DynamicBoneType != null ? AccessTools.Field(DynamicBoneType, "neverOptimize") : null;

        private static readonly ConditionalWeakTable<Component, DynamicBoneState> StateByBone =
            new ConditionalWeakTable<Component, DynamicBoneState>();

        private static bool Prepare()
        {
            if (DynamicBoneLateUpdateMethod == null)
            {
                RORO.GlobalLogger?.LogWarning("DynamicBone.LateUpdate not found, skipping DynamicBone optimization.");
                return false;
            }

            RORO.GlobalLogger?.LogInfo("DynamicBone.LateUpdate optimization enabled.");
            return true;
        }

        private static MethodBase TargetMethod()
        {
            return DynamicBoneLateUpdateMethod!;
        }

        [HarmonyPrefix]
        private static bool Prefix(Component __instance)
        {
            if (!__instance)
            {
                return true;
            }

            if (__instance is Behaviour behaviour && !behaviour.isActiveAndEnabled)
            {
                return true;
            }

            if (DynamicBoneNeverOptimizeField != null
                && DynamicBoneNeverOptimizeField.GetValue(__instance) is bool neverOptimize
                && neverOptimize)
            {
                return true;
            }

            Renderer[]? renderers = ResolveRenderers(__instance);
            if (!ROROConfig.EnableDynamicBoneInvisibleThrottling || renderers == null || renderers.Length == 0)
            {
                return true;
            }

            int currentFrame = Time.frameCount;
            DynamicBoneState state = StateByBone.GetOrCreateValue(__instance);

            if (AnyRendererVisible(renderers))
            {
                state.LastVisibleFrame = currentFrame;
                return true;
            }

            if (currentFrame - state.LastVisibleFrame <= ROROConfig.DynamicBoneRecentlyVisibleGraceFrames)
            {
                return true;
            }

            int updateInterval = Mathf.Max(2, ROROConfig.DynamicBoneInvisibleUpdateInterval);
            int staggeredFrame = currentFrame + (__instance.GetInstanceID() & int.MaxValue);
            return staggeredFrame % updateInterval == 0;
        }

        private static Renderer[]? ResolveRenderers(Component dynamicBone)
        {
            Transform? root = DynamicBoneRootField?.GetValue(dynamicBone) as Transform;
            Renderer[]? renderers = root != null
                ? OptimizedComponentCache.GetRenderersInChildren(root.gameObject)
                : null;

            if (renderers == null || renderers.Length == 0)
            {
                renderers = OptimizedComponentCache.GetRenderersInChildren(dynamicBone.gameObject);
            }

            return renderers;
        }

        private static bool AnyRendererVisible(Renderer[] renderers)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer && renderer.enabled && renderer.isVisible)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
