using System;
using System.Runtime.CompilerServices;
using KinematicCharacterController;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ROR_O.patches
{
    internal static class OptimizedComponentCache
    {
        private sealed class CacheBox<T>
            where T : class
        {
            public T? Value;
        }

        private static readonly ConditionalWeakTable<GameObject, CacheBox<CharacterBody>> CharacterBodies =
            new ConditionalWeakTable<GameObject, CacheBox<CharacterBody>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<CharacterMaster>> CharacterMasters =
            new ConditionalWeakTable<GameObject, CacheBox<CharacterMaster>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<EquipmentSlot>> EquipmentSlots =
            new ConditionalWeakTable<GameObject, CacheBox<EquipmentSlot>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<HealthComponent>> HealthComponents =
            new ConditionalWeakTable<GameObject, CacheBox<HealthComponent>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<Rigidbody>> Rigidbodies =
            new ConditionalWeakTable<GameObject, CacheBox<Rigidbody>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<Rigidbody2D>> Rigidbodies2D =
            new ConditionalWeakTable<GameObject, CacheBox<Rigidbody2D>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<IInspectable>> Inspectables =
            new ConditionalWeakTable<GameObject, CacheBox<IInspectable>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<IInteractable>> Interactables =
            new ConditionalWeakTable<GameObject, CacheBox<IInteractable>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<InteractionDriver>> InteractionDrivers =
            new ConditionalWeakTable<GameObject, CacheBox<InteractionDriver>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<InputBankTest>> InputBankTests =
            new ConditionalWeakTable<GameObject, CacheBox<InputBankTest>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<CameraTargetParams>> CameraTargetParamsCache =
            new ConditionalWeakTable<GameObject, CacheBox<CameraTargetParams>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<CharacterMotor>> CharacterMotors =
            new ConditionalWeakTable<GameObject, CacheBox<CharacterMotor>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<RigidbodyMotor>> RigidbodyMotors =
            new ConditionalWeakTable<GameObject, CacheBox<RigidbodyMotor>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<KinematicCharacterMotor>> KinematicCharacterMotors =
            new ConditionalWeakTable<GameObject, CacheBox<KinematicCharacterMotor>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<NetworkIdentity>> NetworkIdentities =
            new ConditionalWeakTable<GameObject, CacheBox<NetworkIdentity>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<NetworkUser>> NetworkUsers =
            new ConditionalWeakTable<GameObject, CacheBox<NetworkUser>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<OilGhostController>> OilGhostControllers =
            new ConditionalWeakTable<GameObject, CacheBox<OilGhostController>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<PlayerCharacterMasterController>> PlayerCharacterMasterControllers =
            new ConditionalWeakTable<GameObject, CacheBox<PlayerCharacterMasterController>>();

        private static readonly ConditionalWeakTable<GameObject, CacheBox<SkillLocator>> SkillLocators =
            new ConditionalWeakTable<GameObject, CacheBox<SkillLocator>>();

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

        public static Rigidbody? GetRigidbody(GameObject gameObject) =>
            GetOrAdd(gameObject, Rigidbodies, target => target.GetComponent<Rigidbody>());

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

        public static OilGhostController? GetOilGhostController(GameObject gameObject) =>
            GetOrAdd(gameObject, OilGhostControllers, target => target.GetComponent<OilGhostController>());

        public static PlayerCharacterMasterController? GetPlayerCharacterMasterController(Component component) =>
            GetOrAdd(component, PlayerCharacterMasterControllers, target => target.GetComponent<PlayerCharacterMasterController>());

        public static SkillLocator? GetSkillLocator(GameObject gameObject) =>
            GetOrAdd(gameObject, SkillLocators, target => target.GetComponent<SkillLocator>());

        public static SkillLocator? GetSkillLocatorFromComponent(Component component) =>
            GetOrAdd(component, SkillLocators, target => target.GetComponent<SkillLocator>());

        private static T? GetOrAdd<T>(
            Component component,
            ConditionalWeakTable<GameObject, CacheBox<T>> cache,
            Func<GameObject, T?> factory)
            where T : class
        {
            return component ? GetOrAdd(component.gameObject, cache, factory) : null;
        }

        private static T? GetOrAdd<T>(
            GameObject gameObject,
            ConditionalWeakTable<GameObject, CacheBox<T>> cache,
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

        private static bool IsMissing<T>(T? value)
            where T : class
        {
            if (value == null)
            {
                return true;
            }

            return value is UnityEngine.Object unityObject && !unityObject;
        }
    }
}
