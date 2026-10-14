using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(ProjectileDirectionalTargetFinder), nameof(ProjectileDirectionalTargetFinder.FixedUpdate))]
    public static class OptimizeProjectileDirectionalTargetFinderPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ProjectileDirectionalTargetFinder __instance)
        {
            __instance.searchTimer -= Time.fixedDeltaTime;
            if (__instance.searchTimer > 0f)
            {
                return false;
            }

            __instance.searchTimer += __instance.targetSearchInterval;

            Transform? currentTarget = __instance.targetComponent.target;
            if (__instance.allowTargetLoss && currentTarget && __instance.lastFoundTransform == currentTarget && !__instance.PassesFilters(__instance.lastFoundHurtBox))
            {
                __instance.SetTarget(null);
                currentTarget = null;
            }

            if (!__instance.onlySearchIfNoTarget || !currentTarget)
            {
                __instance.SearchForTarget();
                currentTarget = __instance.targetComponent.target;
            }

            __instance.hasTarget = currentTarget != null;
            if (__instance.hadTargetLastUpdate != __instance.hasTarget)
            {
                if (__instance.hasTarget)
                {
                    __instance.onNewTargetFound?.Invoke();
                }
                else
                {
                    __instance.onTargetLost?.Invoke();
                }
            }

            __instance.hadTargetLastUpdate = __instance.hasTarget;
            return false;
        }
    }

    [HarmonyPatch(typeof(ProjectileSphereTargetFinder), nameof(ProjectileSphereTargetFinder.FixedUpdate))]
    public static class OptimizeProjectileSphereTargetFinderPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ProjectileSphereTargetFinder __instance)
        {
            __instance.searchTimer -= Time.fixedDeltaTime;
            if (__instance.searchTimer > 0f)
            {
                return false;
            }

            __instance.searchTimer += __instance.targetSearchInterval;

            Transform? currentTarget = __instance.targetComponent.target;
            if (__instance.allowTargetLoss && currentTarget && __instance.lastFoundTransform == currentTarget && !__instance.PassesFilters(__instance.lastFoundHurtBox))
            {
                __instance.SetTarget(null);
                currentTarget = null;
            }

            if (!__instance.onlySearchIfNoTarget || !currentTarget)
            {
                __instance.SearchForTarget();
                currentTarget = __instance.targetComponent.target;
            }

            __instance.hasTarget = currentTarget != null;
            if (__instance.hadTargetLastUpdate != __instance.hasTarget)
            {
                if (__instance.hasTarget)
                {
                    __instance.onNewTargetFound?.Invoke();
                }
                else
                {
                    __instance.onTargetLost?.Invoke();
                }
            }

            __instance.hadTargetLastUpdate = __instance.hasTarget;
            return false;
        }
    }

    [HarmonyPatch(typeof(ProjectileStopOnTargetLoss), nameof(ProjectileStopOnTargetLoss.FixedUpdate))]
    public static class OptimizeProjectileStopOnTargetLossPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ProjectileStopOnTargetLoss __instance)
        {
            Transform? currentTarget = __instance.projectileTargetComponent.target;
            if (currentTarget != __instance.targetTransform)
            {
                __instance.SetTarget(currentTarget);
            }

            HurtBox? targetHurtbox = __instance.targetHurtbox;
            bool shouldMove = targetHurtbox && targetHurtbox.healthComponent && targetHurtbox.healthComponent.alive;
            if (__instance.projectileSimple.enabled != shouldMove)
            {
                __instance.projectileSimple.enabled = shouldMove;
            }

            return false;
        }
    }
}
