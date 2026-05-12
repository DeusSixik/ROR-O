using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ROR_O.Utilities;

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

        private static readonly FixedConditionalWeakTable<Component, DynamicBoneState> StateByBone =
            new FixedConditionalWeakTable<Component, DynamicBoneState>();

        private static int cachedCameraFrame = int.MinValue;
        private static Camera? cachedMainCamera;

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

            int invisibleInterval = Mathf.Max(2, ROROConfig.DynamicBoneInvisibleUpdateInterval);
            int visibleMidInterval = Mathf.Max(1, ROROConfig.DynamicBoneVisibleMidUpdateInterval);
            int visibleFarInterval = Mathf.Max(visibleMidInterval, ROROConfig.DynamicBoneVisibleFarUpdateInterval);
            int visibleInterval = 1;
            int currentFrame = Time.frameCount;
            DynamicBoneState state = StateByBone.GetOrCreateValue(__instance);

            if (AnyRendererVisible(renderers))
            {
                state.LastVisibleFrame = currentFrame;

                Camera? camera = GetMainCamera();
                if (camera != null)
                {
                    float distanceSqr = (GetReferencePosition(__instance) - camera.transform.position).sqrMagnitude;
                    float midDistance = Mathf.Max(0f, ROROConfig.DynamicBoneVisibleMidDistance);
                    float farDistance = Mathf.Max(midDistance, ROROConfig.DynamicBoneVisibleFarDistance);
                    float midDistanceSqr = midDistance * midDistance;
                    float farDistanceSqr = farDistance * farDistance;

                    if (distanceSqr >= farDistanceSqr)
                    {
                        visibleInterval = visibleFarInterval;
                    }
                    else if (distanceSqr >= midDistanceSqr)
                    {
                        visibleInterval = visibleMidInterval;
                    }
                }
            }
            else if (currentFrame - state.LastVisibleFrame <= ROROConfig.DynamicBoneRecentlyVisibleGraceFrames)
            {
                return true;
            }
            else
            {
                visibleInterval = invisibleInterval;
            }

            if (visibleInterval <= 1)
            {
                return true;
            }

            int staggeredFrame = currentFrame + (__instance.GetInstanceID() & int.MaxValue);
            return staggeredFrame % visibleInterval == 0;
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

        private static Vector3 GetReferencePosition(Component dynamicBone)
        {
            Transform? root = DynamicBoneRootField?.GetValue(dynamicBone) as Transform;
            return root != null ? root.position : dynamicBone.transform.position;
        }

        private static Camera? GetMainCamera()
        {
            int currentFrame = Time.frameCount;
            if (cachedCameraFrame != currentFrame)
            {
                cachedMainCamera = Camera.main;
                cachedCameraFrame = currentFrame;
            }

            return cachedMainCamera;
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
