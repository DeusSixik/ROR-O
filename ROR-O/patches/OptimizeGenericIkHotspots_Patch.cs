using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ROR_O.patches
{
    [HarmonyPatch]
    internal static class OptimizeGenericIkLateUpdatePatch
    {
        private sealed class GenericIkState
        {
            public int LastVisibleFrame = int.MinValue;
        }

        private static readonly Type? InverseKinematicsType = AccessTools.TypeByName("Generics.Dynamics.InverseKinematics");
        private static readonly MethodInfo? InverseKinematicsLateUpdateMethod =
            InverseKinematicsType != null ? AccessTools.DeclaredMethod(InverseKinematicsType, "LateUpdate") : null;

        private static readonly FieldInfo? AnimatorField =
            InverseKinematicsType != null ? AccessTools.Field(InverseKinematicsType, "animator") : null;

        private static readonly ConditionalWeakTable<Component, GenericIkState> StateByIk =
            new ConditionalWeakTable<Component, GenericIkState>();

        private static bool Prepare()
        {
            if (InverseKinematicsLateUpdateMethod == null)
            {
                RORO.GlobalLogger?.LogWarning("Generics.Dynamics.InverseKinematics.LateUpdate not found, skipping GenericIK optimization.");
                return false;
            }

            RORO.GlobalLogger?.LogInfo("Generics.Dynamics.InverseKinematics.LateUpdate optimization enabled.");
            return true;
        }

        private static MethodBase TargetMethod()
        {
            return InverseKinematicsLateUpdateMethod!;
        }

        [HarmonyPrefix]
        private static bool Prefix(Component __instance)
        {
            if (!ROROConfig.EnableGenericIkInvisibleThrottling || !__instance)
            {
                return true;
            }

            if (__instance is Behaviour behaviour && !behaviour.isActiveAndEnabled)
            {
                return true;
            }

            Renderer[]? renderers = ResolveRenderers(__instance);
            if (renderers == null || renderers.Length == 0)
            {
                return true;
            }

            int currentFrame = Time.frameCount;
            GenericIkState state = StateByIk.GetOrCreateValue(__instance);

            if (AnyRendererVisible(renderers))
            {
                state.LastVisibleFrame = currentFrame;
                return true;
            }

            if (currentFrame - state.LastVisibleFrame <= ROROConfig.GenericIkRecentlyVisibleGraceFrames)
            {
                return true;
            }

            int updateInterval = Mathf.Max(2, ROROConfig.GenericIkInvisibleUpdateInterval);
            int staggeredFrame = currentFrame + (__instance.GetInstanceID() & int.MaxValue);
            return staggeredFrame % updateInterval == 0;
        }

        private static Renderer[]? ResolveRenderers(Component inverseKinematics)
        {
            Animator? animator = AnimatorField?.GetValue(inverseKinematics) as Animator;
            if (animator != null)
            {
                Renderer[]? animatorRenderers = OptimizedComponentCache.GetRenderersInChildren(animator.gameObject);
                if (animatorRenderers != null && animatorRenderers.Length > 0)
                {
                    return animatorRenderers;
                }
            }

            return OptimizedComponentCache.GetRenderersInChildren(inverseKinematics.gameObject);
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
