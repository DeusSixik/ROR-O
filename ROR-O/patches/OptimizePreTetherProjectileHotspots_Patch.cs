using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
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
        private delegate Transform FindTargetDelegate(MissileController instance);

        private static readonly AccessTools.FieldRef<MissileController, float> TimerRef =
            AccessTools.FieldRefAccess<MissileController, float>("timer");

        private static readonly AccessTools.FieldRef<MissileController, Rigidbody> RigidbodyRef =
            AccessTools.FieldRefAccess<MissileController, Rigidbody>("rigidbody");

        private static readonly AccessTools.FieldRef<MissileController, Transform> TransformRef =
            AccessTools.FieldRefAccess<MissileController, Transform>("transform");

        private static readonly AccessTools.FieldRef<MissileController, ProjectileTargetComponent> TargetComponentRef =
            AccessTools.FieldRefAccess<MissileController, ProjectileTargetComponent>("targetComponent");

        private static readonly AccessTools.FieldRef<MissileController, QuaternionPID> TorquePidRef =
            AccessTools.FieldRefAccess<MissileController, QuaternionPID>("torquePID");

        private static readonly FindTargetDelegate FindTarget = AccessTools.MethodDelegate<FindTargetDelegate>(
            AccessTools.Method(typeof(MissileController), "FindTarget"));

        [HarmonyPrefix]
        private static bool Prefix(MissileController __instance)
        {
            float timer = TimerRef(__instance) + Time.deltaTime;
            TimerRef(__instance) = timer;

            Rigidbody rigidbody = RigidbodyRef(__instance);
            Transform transform = TransformRef(__instance);
            ProjectileTargetComponent targetComponent = TargetComponentRef(__instance);

            if (timer < __instance.giveupTimer)
            {
                rigidbody.velocity = transform.forward * __instance.maxVelocity;

                Transform target = targetComponent.target;
                if (target && timer >= __instance.delayTimer)
                {
                    rigidbody.velocity = transform.forward * (__instance.maxVelocity + timer * __instance.acceleration);

                    Vector3 targetVector = target.position + Random.insideUnitSphere * __instance.turbulence - transform.position;
                    if (targetVector != Vector3.zero)
                    {
                        Quaternion currentRotation = transform.rotation;
                        Quaternion desiredRotation = Util.QuaternionSafeLookRotation(targetVector);
                        QuaternionPID torquePid = TorquePidRef(__instance);
                        torquePid.inputQuat = currentRotation;
                        torquePid.targetQuat = desiredRotation;
                        rigidbody.angularVelocity = torquePid.UpdatePID();
                    }
                }
            }

            Transform currentTarget = targetComponent.target;
            if (!currentTarget)
            {
                targetComponent.target = FindTarget(__instance);
            }
            else
            {
                HealthComponent? healthComponent = OptimizedComponentCache.GetHealthComponentFromComponent(currentTarget);
                if (healthComponent != null && !healthComponent.alive)
                {
                    targetComponent.target = FindTarget(__instance);
                }
            }

            if (timer > __instance.deathTimer)
            {
                Object.Destroy(__instance.gameObject);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(DaggerController), nameof(DaggerController.FixedUpdate))]
    public static class OptimizeDaggerControllerPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(DaggerController __instance)
        {
            float timer = __instance.timer + Time.fixedDeltaTime;
            __instance.timer = timer;

            if (timer < __instance.giveupTimer)
            {
                Transform? target = __instance.target;
                if (target)
                {
                    Vector3 targetVector = target.position - __instance.transform.position;
                    if (targetVector != Vector3.zero)
                    {
                        __instance.transform.rotation = Util.QuaternionSafeLookRotation(targetVector);
                    }

                    if (timer >= __instance.delayTimer)
                    {
                        __instance.rigidbody.AddForce(__instance.transform.forward * __instance.acceleration);
                        if (!__instance.hasPlayedSound)
                        {
                            Util.PlaySound("Play_item_proc_dagger_fly", __instance.gameObject);
                            __instance.hasPlayedSound = true;
                        }
                    }
                }
            }
            else
            {
                __instance.rigidbody.useGravity = true;
            }

            Transform? currentTarget = __instance.target;
            if (!currentTarget)
            {
                __instance.target = __instance.FindTarget();
            }
            else
            {
                HealthComponent? healthComponent = OptimizedComponentCache.GetHealthComponentFromComponent(currentTarget);
                if (healthComponent != null && !healthComponent.alive)
                {
                    __instance.target = __instance.FindTarget();
                }
            }

            if (timer > __instance.deathTimer)
            {
                Object.Destroy(__instance.gameObject);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ProjectileFireChildren), nameof(ProjectileFireChildren.Update))]
    public static class OptimizeProjectileFireChildrenPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ProjectileFireChildren __instance)
        {
            __instance.timer += Time.deltaTime;
            __instance.nextSpawnTimer += Time.deltaTime;

            if (__instance.timer >= __instance.duration)
            {
                Object.Destroy(__instance.gameObject);
            }

            float firingDuration = __instance.useSeparateDurationForFiring
                ? __instance.customFiringDuration
                : __instance.duration;

            float spawnInterval = firingDuration / __instance.count;
            if (__instance.nextSpawnTimer < spawnInterval || !NetworkServer.active || __instance.spawned >= __instance.count)
            {
                return false;
            }

            __instance.spawned++;
            __instance.nextSpawnTimer -= spawnInterval;

            Transform transform = __instance.transform;
            GameObject child = Object.Instantiate(
                __instance.childProjectilePrefab,
                transform.position,
                Util.QuaternionSafeLookRotation(transform.forward));

            ProjectileController? childProjectileController = OptimizedComponentCache.GetProjectileController(child);
            if (childProjectileController != null)
            {
                childProjectileController.procChainMask = __instance.projectileController.procChainMask;
                childProjectileController.procCoefficient = __instance.projectileController.procCoefficient * __instance.childProcCoefficient;
                childProjectileController.Networkowner = __instance.projectileController.owner;
            }

            TeamFilter? childTeamFilter = OptimizedComponentCache.GetTeamFilter(child);
            TeamFilter? ownerTeamFilter = OptimizedComponentCache.GetTeamFilter(__instance.gameObject);
            if (childTeamFilter != null && ownerTeamFilter != null)
            {
                childTeamFilter.teamIndex = ownerTeamFilter.teamIndex;
            }

            ProjectileDamage? childProjectileDamage = OptimizedComponentCache.GetProjectileDamage(child);
            if (childProjectileDamage != null)
            {
                childProjectileDamage.damage = __instance.projectileDamage.damage * __instance.childDamageCoefficient;
                childProjectileDamage.crit = __instance.projectileDamage.crit;
                childProjectileDamage.force = __instance.projectileDamage.force;
                childProjectileDamage.damageColorIndex = __instance.projectileDamage.damageColorIndex;
            }

            NetworkServer.Spawn(child);
            return false;
        }
    }
}
