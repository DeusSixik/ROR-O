using System;
using System.Collections.Generic;
using KinematicCharacterController;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using ROR_O.Utilities;

namespace ROR_O.patches
{
    internal static class OptimizedComponentCache
    {
        private sealed class CacheBox<T>
            where T : class
        {
            public T? Value;
        }

        private sealed class NamedTransformCache
        {
            public readonly Dictionary<string, Transform?> Values = new Dictionary<string, Transform?>();
        }

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<CharacterBody>> CharacterBodies =
            new FixedConditionalWeakTable<GameObject, CacheBox<CharacterBody>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<CharacterMaster>> CharacterMasters =
            new FixedConditionalWeakTable<GameObject, CacheBox<CharacterMaster>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<EquipmentSlot>> EquipmentSlots =
            new FixedConditionalWeakTable<GameObject, CacheBox<EquipmentSlot>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<HealthComponent>> HealthComponents =
            new FixedConditionalWeakTable<GameObject, CacheBox<HealthComponent>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<HuntressTracker>> HuntressTrackers =
            new FixedConditionalWeakTable<GameObject, CacheBox<HuntressTracker>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<DamageTrail>> DamageTrails =
            new FixedConditionalWeakTable<GameObject, CacheBox<DamageTrail>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<Rigidbody>> Rigidbodies =
            new FixedConditionalWeakTable<GameObject, CacheBox<Rigidbody>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<Rigidbody2D>> Rigidbodies2D =
            new FixedConditionalWeakTable<GameObject, CacheBox<Rigidbody2D>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<IInspectable>> Inspectables =
            new FixedConditionalWeakTable<GameObject, CacheBox<IInspectable>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<IInteractable>> Interactables =
            new FixedConditionalWeakTable<GameObject, CacheBox<IInteractable>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<InteractionDriver>> InteractionDrivers =
            new FixedConditionalWeakTable<GameObject, CacheBox<InteractionDriver>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<InputBankTest>> InputBankTests =
            new FixedConditionalWeakTable<GameObject, CacheBox<InputBankTest>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<CameraTargetParams>> CameraTargetParamsCache =
            new FixedConditionalWeakTable<GameObject, CacheBox<CameraTargetParams>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<ChefController>> ChefControllers =
            new FixedConditionalWeakTable<GameObject, CacheBox<ChefController>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<CharacterMotor>> CharacterMotors =
            new FixedConditionalWeakTable<GameObject, CacheBox<CharacterMotor>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<RigidbodyMotor>> RigidbodyMotors =
            new FixedConditionalWeakTable<GameObject, CacheBox<RigidbodyMotor>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<KinematicCharacterMotor>> KinematicCharacterMotors =
            new FixedConditionalWeakTable<GameObject, CacheBox<KinematicCharacterMotor>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<NetworkIdentity>> NetworkIdentities =
            new FixedConditionalWeakTable<GameObject, CacheBox<NetworkIdentity>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<NetworkUser>> NetworkUsers =
            new FixedConditionalWeakTable<GameObject, CacheBox<NetworkUser>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<FalseSonBossController>> FalseSonBossControllers =
            new FixedConditionalWeakTable<GameObject, CacheBox<FalseSonBossController>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<OilGhostController>> OilGhostControllers =
            new FixedConditionalWeakTable<GameObject, CacheBox<OilGhostController>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<PlayerCharacterMasterController>> PlayerCharacterMasterControllers =
            new FixedConditionalWeakTable<GameObject, CacheBox<PlayerCharacterMasterController>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<SkillLocator>> SkillLocators =
            new FixedConditionalWeakTable<GameObject, CacheBox<SkillLocator>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<TeamFilter>> TeamFilters =
            new FixedConditionalWeakTable<GameObject, CacheBox<TeamFilter>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<ProjectileController>> ProjectileControllers =
            new FixedConditionalWeakTable<GameObject, CacheBox<ProjectileController>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<ProjectileDamage>> ProjectileDamages =
            new FixedConditionalWeakTable<GameObject, CacheBox<ProjectileDamage>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<ChildLocator>> ChildLocators =
            new FixedConditionalWeakTable<GameObject, CacheBox<ChildLocator>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<ScaleParticleSystemDuration>> ScaleParticleSystemDurations =
            new FixedConditionalWeakTable<GameObject, CacheBox<ScaleParticleSystemDuration>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<ObjectScaleCurve>> ObjectScaleCurves =
            new FixedConditionalWeakTable<GameObject, CacheBox<ObjectScaleCurve>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<CharacterDirection>> CharacterDirections =
            new FixedConditionalWeakTable<GameObject, CacheBox<CharacterDirection>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<CharacterModel>> CharacterModels =
            new FixedConditionalWeakTable<GameObject, CacheBox<CharacterModel>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<HurtBoxGroup>> HurtBoxGroups =
            new FixedConditionalWeakTable<GameObject, CacheBox<HurtBoxGroup>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<HurtBox>> HurtBoxes =
            new FixedConditionalWeakTable<GameObject, CacheBox<HurtBox>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<Animator>> AnimatorsInChildren =
            new FixedConditionalWeakTable<GameObject, CacheBox<Animator>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<ParticleSystem>> ParticleSystems =
            new FixedConditionalWeakTable<GameObject, CacheBox<ParticleSystem>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<ParticleSystem[]>> ParticleSystemsInChildren =
            new FixedConditionalWeakTable<GameObject, CacheBox<ParticleSystem[]>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<Light[]>> LightsInChildren =
            new FixedConditionalWeakTable<GameObject, CacheBox<Light[]>>();

        private static readonly FixedConditionalWeakTable<GameObject, CacheBox<Renderer[]>> RenderersInChildren =
            new FixedConditionalWeakTable<GameObject, CacheBox<Renderer[]>>();

        private static readonly FixedConditionalWeakTable<Transform, NamedTransformCache> TransformFindCache =
            new FixedConditionalWeakTable<Transform, NamedTransformCache>();

        private static readonly FixedConditionalWeakTable<ChildLocator, NamedTransformCache> ChildLocatorFindCache =
            new FixedConditionalWeakTable<ChildLocator, NamedTransformCache>();

        public static CharacterBody? GetCharacterBody(GameObject gameObject) =>
            GetOrAdd(gameObject, CharacterBodies, target => target.GetComponent<CharacterBody>());

        public static CharacterBody? GetCharacterBodyFromComponent(Component component) =>
            GetOrAdd(component, CharacterBodies, target => target.GetComponent<CharacterBody>());

        public static CharacterMaster? GetCharacterMaster(GameObject gameObject) =>
            GetOrAdd(gameObject, CharacterMasters, target => target.GetComponent<CharacterMaster>());

        public static EquipmentSlot? GetEquipmentSlot(GameObject gameObject) =>
            GetOrAdd(gameObject, EquipmentSlots, target => target.GetComponent<EquipmentSlot>());

        public static HealthComponent? GetHealthComponent(GameObject gameObject) =>
            GetOrAdd(gameObject, HealthComponents, target => target.GetComponent<HealthComponent>());

        public static HealthComponent? GetHealthComponentFromComponent(Component component) =>
            GetOrAdd(component, HealthComponents, target => target.GetComponent<HealthComponent>());

        public static HuntressTracker? GetHuntressTracker(GameObject gameObject) =>
            GetOrAdd(gameObject, HuntressTrackers, target => target.GetComponent<HuntressTracker>());

        public static DamageTrail? GetDamageTrail(GameObject gameObject) =>
            GetOrAdd(gameObject, DamageTrails, target => target.GetComponent<DamageTrail>());

        public static Rigidbody? GetRigidbody(GameObject gameObject) =>
            GetOrAdd(gameObject, Rigidbodies, target => target.GetComponent<Rigidbody>());

        public static Rigidbody? GetRigidbodyFromComponent(Component component) =>
            GetOrAdd(component, Rigidbodies, target => target.GetComponent<Rigidbody>());

        public static Rigidbody2D? GetRigidbody2D(GameObject gameObject) =>
            GetOrAdd(gameObject, Rigidbodies2D, target => target.GetComponent<Rigidbody2D>());

        public static IInspectable? GetInspectable(GameObject gameObject) =>
            GetOrAdd(gameObject, Inspectables, target => target.GetComponent<IInspectable>());

        public static IInteractable? GetInteractable(GameObject gameObject) =>
            GetOrAdd(gameObject, Interactables, target => target.GetComponent<IInteractable>());

        public static InteractionDriver? GetInteractionDriver(GameObject gameObject) =>
            GetOrAdd(gameObject, InteractionDrivers, target => target.GetComponent<InteractionDriver>());

        public static InteractionDriver? GetInteractionDriverFromComponent(Component component) =>
            GetOrAdd(component, InteractionDrivers, target => target.GetComponent<InteractionDriver>());

        public static InputBankTest? GetInputBankTest(GameObject gameObject) =>
            GetOrAdd(gameObject, InputBankTests, target => target.GetComponent<InputBankTest>());

        public static CameraTargetParams? GetCameraTargetParamsFromComponent(Component component) =>
            GetOrAdd(component, CameraTargetParamsCache, target => target.GetComponent<CameraTargetParams>());

        public static ChefController? GetChefController(GameObject gameObject) =>
            GetOrAdd(gameObject, ChefControllers, target => target.GetComponent<ChefController>());

        public static CharacterMotor? GetCharacterMotor(GameObject gameObject) =>
            GetOrAdd(gameObject, CharacterMotors, target => target.GetComponent<CharacterMotor>());

        public static CharacterMotor? GetCharacterMotor(Component component) =>
            GetOrAdd(component, CharacterMotors, target => target.GetComponent<CharacterMotor>());

        public static RigidbodyMotor? GetRigidbodyMotor(Component component) =>
            GetOrAdd(component, RigidbodyMotors, target => target.GetComponent<RigidbodyMotor>());

        public static KinematicCharacterMotor? GetKinematicCharacterMotor(Component component) =>
            GetOrAdd(component, KinematicCharacterMotors, target => target.GetComponent<KinematicCharacterMotor>());

        public static NetworkIdentity? GetNetworkIdentity(Component component) =>
            GetOrAdd(component, NetworkIdentities, target => target.GetComponent<NetworkIdentity>());

        public static NetworkUser? GetNetworkUser(GameObject gameObject) =>
            GetOrAdd(gameObject, NetworkUsers, target => target.GetComponent<NetworkUser>());

        public static FalseSonBossController? GetFalseSonBossControllerFromComponent(Component component) =>
            GetOrAdd(component, FalseSonBossControllers, target => target.GetComponent<FalseSonBossController>());

        public static OilGhostController? GetOilGhostController(GameObject gameObject) =>
            GetOrAdd(gameObject, OilGhostControllers, target => target.GetComponent<OilGhostController>());

        public static PlayerCharacterMasterController? GetPlayerCharacterMasterController(Component component) =>
            GetOrAdd(component, PlayerCharacterMasterControllers, target => target.GetComponent<PlayerCharacterMasterController>());

        public static SkillLocator? GetSkillLocator(GameObject gameObject) =>
            GetOrAdd(gameObject, SkillLocators, target => target.GetComponent<SkillLocator>());

        public static SkillLocator? GetSkillLocatorFromComponent(Component component) =>
            GetOrAdd(component, SkillLocators, target => target.GetComponent<SkillLocator>());

        public static TeamFilter? GetTeamFilter(GameObject gameObject) =>
            GetOrAdd(gameObject, TeamFilters, target => target.GetComponent<TeamFilter>());

        public static ProjectileController? GetProjectileController(GameObject gameObject) =>
            GetOrAdd(gameObject, ProjectileControllers, target => target.GetComponent<ProjectileController>());

        public static ProjectileDamage? GetProjectileDamage(GameObject gameObject) =>
            GetOrAdd(gameObject, ProjectileDamages, target => target.GetComponent<ProjectileDamage>());

        public static ChildLocator? GetChildLocatorFromComponent(Component component) =>
            GetOrAdd(component, ChildLocators, target => target.GetComponent<ChildLocator>());

        public static ChildLocator? GetChildLocator(GameObject gameObject) =>
            GetOrAdd(gameObject, ChildLocators, target => target.GetComponent<ChildLocator>());

        public static ScaleParticleSystemDuration? GetScaleParticleSystemDuration(GameObject gameObject) =>
            GetOrAdd(gameObject, ScaleParticleSystemDurations, target => target.GetComponent<ScaleParticleSystemDuration>());

        public static ScaleParticleSystemDuration? GetScaleParticleSystemDurationFromComponent(Component component) =>
            GetOrAdd(component, ScaleParticleSystemDurations, target => target.GetComponent<ScaleParticleSystemDuration>());

        public static ObjectScaleCurve? GetObjectScaleCurve(GameObject gameObject) =>
            GetOrAdd(gameObject, ObjectScaleCurves, target => target.GetComponent<ObjectScaleCurve>());

        public static CharacterDirection? GetCharacterDirectionFromComponent(Component component) =>
            GetOrAdd(component, CharacterDirections, target => target.GetComponent<CharacterDirection>());

        public static CharacterModel? GetCharacterModelFromComponent(Component component) =>
            GetOrAdd(component, CharacterModels, target => target.GetComponent<CharacterModel>());

        public static HurtBoxGroup? GetHurtBoxGroupFromComponent(Component component) =>
            GetOrAdd(component, HurtBoxGroups, target => target.GetComponent<HurtBoxGroup>());

        public static HurtBox? GetHurtBoxFromComponent(Component component) =>
            GetOrAdd(component, HurtBoxes, target => target.GetComponent<HurtBox>());

        public static Animator? GetAnimatorInChildren(GameObject gameObject) =>
            GetOrAdd(gameObject, AnimatorsInChildren, target => target.GetComponentInChildren<Animator>());

        public static ParticleSystem? GetParticleSystem(Component component) =>
            GetOrAdd(component, ParticleSystems, target => target.GetComponent<ParticleSystem>());

        public static ParticleSystem[]? GetParticleSystemsInChildren(Component component) =>
            GetOrAddArray(component, ParticleSystemsInChildren, target => target.GetComponentsInChildren<ParticleSystem>());

        public static ParticleSystem[]? GetParticleSystemsInChildren(GameObject gameObject) =>
            GetOrAddArray(gameObject, ParticleSystemsInChildren, target => target.GetComponentsInChildren<ParticleSystem>());

        public static Light[]? GetLightsInChildren(Component component) =>
            GetOrAddArray(component, LightsInChildren, target => target.GetComponentsInChildren<Light>());

        public static Renderer[]? GetRenderersInChildren(Component component) =>
            GetOrAddArray(component, RenderersInChildren, target => target.GetComponentsInChildren<Renderer>());

        public static Renderer[]? GetRenderersInChildren(GameObject gameObject) =>
            GetOrAddArray(gameObject, RenderersInChildren, target => target.GetComponentsInChildren<Renderer>());

        public static Transform? FindTransform(Transform transform, string childName) =>
            GetOrFindTransform(transform, childName, target => target.Find(childName), TransformFindCache);

        public static Transform? FindChild(ChildLocator childLocator, string childName) =>
            GetOrFindTransform(childLocator, childName, target => target.FindChild(childName), ChildLocatorFindCache);

        private static T? GetOrAdd<T>(
            Component component,
            FixedConditionalWeakTable<GameObject, CacheBox<T>> cache,
            Func<GameObject, T?> factory)
            where T : class
        {
            return component ? GetOrAdd(component.gameObject, cache, factory) : null;
        }

        private static T? GetOrAdd<T>(
            GameObject gameObject,
            FixedConditionalWeakTable<GameObject, CacheBox<T>> cache,
            Func<GameObject, T?> factory)
            where T : class
        {
            if (!gameObject)
            {
                return null;
            }

            CacheBox<T> box = cache.GetOrCreateValue(gameObject);
            if (IsMissing(box.Value))
            {
                box.Value = factory(gameObject);
            }

            return box.Value;
        }

        private static T[]? GetOrAddArray<T>(
            Component component,
            FixedConditionalWeakTable<GameObject, CacheBox<T[]>> cache,
            Func<GameObject, T[]?> factory)
            where T : UnityEngine.Object
        {
            return component ? GetOrAddArray(component.gameObject, cache, factory) : null;
        }

        private static T[]? GetOrAddArray<T>(
            GameObject gameObject,
            FixedConditionalWeakTable<GameObject, CacheBox<T[]>> cache,
            Func<GameObject, T[]?> factory)
            where T : UnityEngine.Object
        {
            if (!gameObject)
            {
                return null;
            }

            CacheBox<T[]> box = cache.GetOrCreateValue(gameObject);
            if (IsMissingArray(box.Value))
            {
                box.Value = factory(gameObject);
            }

            return box.Value;
        }

        private static Transform? GetOrFindTransform<TSource>(
            TSource source,
            string childName,
            Func<TSource, Transform?> finder,
            FixedConditionalWeakTable<TSource, NamedTransformCache> cache)
            where TSource : class
        {
            if (source == null)
            {
                return null;
            }

            if (source is UnityEngine.Object unityObject && !unityObject)
            {
                return null;
            }

            NamedTransformCache namedCache = cache.GetOrCreateValue(source);
            if (namedCache.Values.TryGetValue(childName, out Transform? cachedTransform) && cachedTransform)
            {
                return cachedTransform;
            }

            Transform? foundTransform = finder(source);
            if (foundTransform)
            {
                namedCache.Values[childName] = foundTransform;
            }
            else
            {
                namedCache.Values.Remove(childName);
            }

            return foundTransform;
        }

        private static bool IsMissing<T>(T? value)
            where T : class
        {
            if (value == null)
            {
                return true;
            }

            return value is UnityEngine.Object unityObject && !unityObject;
        }

        private static bool IsMissingArray<T>(T[]? value)
            where T : UnityEngine.Object
        {
            if (value == null)
            {
                return true;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (!value[i])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
