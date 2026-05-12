using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using static ROR_O.patches.OptimizeGetComponentPatchTools;

namespace ROR_O.patches
{
    internal static class OptimizeStateVfxPatchTools
    {
        public static readonly MethodInfo GameObjectGetChildLocatorMethod = MakeGameObjectGetComponentMethod(typeof(ChildLocator));
        public static readonly MethodInfo ComponentGetChildLocatorMethod = MakeComponentGetComponentMethod(typeof(ChildLocator));
        public static readonly MethodInfo GameObjectGetObjectScaleCurveMethod = MakeGameObjectGetComponentMethod(typeof(ObjectScaleCurve));
        public static readonly MethodInfo GameObjectGetScaleParticleSystemDurationMethod = MakeGameObjectGetComponentMethod(typeof(ScaleParticleSystemDuration));
        public static readonly MethodInfo ComponentGetScaleParticleSystemDurationMethod = MakeComponentGetComponentMethod(typeof(ScaleParticleSystemDuration));
        public static readonly MethodInfo GameObjectGetAnimatorInChildrenMethod = MakeGameObjectGetComponentInChildrenMethod(typeof(Animator));

        public static readonly MethodInfo CachedChildLocatorFromComponentMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetChildLocatorFromComponent));

        public static readonly MethodInfo CachedChildLocatorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetChildLocator));

        public static readonly MethodInfo CachedObjectScaleCurveMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetObjectScaleCurve));

        public static readonly MethodInfo CachedScaleParticleSystemDurationMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetScaleParticleSystemDuration));

        public static readonly MethodInfo CachedScaleParticleSystemDurationFromComponentMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetScaleParticleSystemDurationFromComponent));

        public static readonly MethodInfo CachedAnimatorInChildrenMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetAnimatorInChildren));

        public static IEnumerable<CodeInstruction> ReplaceInstructions(
            IEnumerable<CodeInstruction> instructions,
            string logName,
            bool replaceChildLocatorFromGameObject = false,
            bool replaceChildLocator = false,
            bool replaceObjectScaleCurve = false,
            bool replaceScaleDurationFromGameObject = false,
            bool replaceScaleDurationFromComponent = false,
            bool replaceAnimatorInChildrenFromGameObject = false)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if ((replaceChildLocatorFromGameObject && ReplaceCall(instruction, GameObjectGetChildLocatorMethod, CachedChildLocatorMethod))
                    || (replaceChildLocator && ReplaceCall(instruction, ComponentGetChildLocatorMethod, CachedChildLocatorFromComponentMethod))
                    || (replaceObjectScaleCurve && ReplaceCall(instruction, GameObjectGetObjectScaleCurveMethod, CachedObjectScaleCurveMethod))
                    || (replaceScaleDurationFromGameObject && ReplaceCall(instruction, GameObjectGetScaleParticleSystemDurationMethod, CachedScaleParticleSystemDurationMethod))
                    || (replaceScaleDurationFromComponent && ReplaceCall(instruction, ComponentGetScaleParticleSystemDurationMethod, CachedScaleParticleSystemDurationFromComponentMethod))
                    || (replaceAnimatorInChildrenFromGameObject && ReplaceCall(instruction, GameObjectGetAnimatorInChildrenMethod, CachedAnimatorInChildrenMethod)))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"{logName} optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.GrandParentBoss.PortalJump), nameof(EntityStates.GrandParentBoss.PortalJump.FixedUpdate))]
    public static class OptimizePortalJumpPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "PortalJump.FixedUpdate",
                replaceChildLocator: true,
                replaceObjectScaleCurve: true);
    }

    [HarmonyPatch(typeof(EntityStates.GrandParentBoss.GroundSwipe), nameof(EntityStates.GrandParentBoss.GroundSwipe.FixedUpdate))]
    public static class OptimizeGroundSwipePatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "GroundSwipe.FixedUpdate",
                replaceObjectScaleCurve: true,
                replaceScaleDurationFromGameObject: true);
    }

    [HarmonyPatch(typeof(EntityStates.LunarWisp.SeekingBomb), nameof(EntityStates.LunarWisp.SeekingBomb.FixedUpdate))]
    public static class OptimizeSeekingBombStatePatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "SeekingBomb.FixedUpdate",
                replaceChildLocator: true,
                replaceScaleDurationFromGameObject: true);
    }

    [HarmonyPatch(typeof(EntityStates.Loader.BaseChargeFist), nameof(EntityStates.Loader.BaseChargeFist.FixedUpdate))]
    public static class OptimizeBaseChargeFistPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "BaseChargeFist.FixedUpdate",
                replaceScaleDurationFromComponent: true);
    }

    [HarmonyPatch(typeof(EntityStates.LemurianBruiserMonster.Flamebreath), nameof(EntityStates.LemurianBruiserMonster.Flamebreath.FixedUpdate))]
    public static class OptimizeFlamebreathPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "Flamebreath.FixedUpdate",
                replaceScaleDurationFromComponent: true);
    }

    [HarmonyPatch(typeof(EntityStates.Tanker.Ignite), nameof(EntityStates.Tanker.Ignite.FixedUpdate))]
    public static class OptimizeIgnitePatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "Ignite.FixedUpdate",
                replaceScaleDurationFromComponent: true);
    }

    [HarmonyPatch(typeof(EntityStates.Huntress.BlinkState), nameof(EntityStates.Huntress.BlinkState.FixedUpdate))]
    public static class OptimizeBlinkStatePatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "BlinkState.FixedUpdate",
                replaceScaleDurationFromGameObject: true,
                replaceScaleDurationFromComponent: true);
    }

    [HarmonyPatch(typeof(EntityStates.Drone.DroneWeapon.Flamethrower), nameof(EntityStates.Drone.DroneWeapon.Flamethrower.OnEnter))]
    public static class OptimizeDroneFlamethrowerPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "DroneFlamethrower.OnEnter",
                replaceScaleDurationFromComponent: true);
    }

    [HarmonyPatch(typeof(EntityStates.FalseSon.ClubSwing3), nameof(EntityStates.FalseSon.ClubSwing3.FixedUpdate))]
    public static class OptimizeClubSwing3Patch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "ClubSwing3.FixedUpdate",
                replaceScaleDurationFromComponent: true);
    }

    [HarmonyPatch(typeof(EntityStates.FalseSonBoss.ClubForsakenBoss), nameof(EntityStates.FalseSonBoss.ClubForsakenBoss.FixedUpdate))]
    public static class OptimizeClubForsakenBossPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "ClubForsakenBoss.FixedUpdate",
                replaceScaleDurationFromComponent: true);
    }

    [HarmonyPatch(typeof(EntityStates.FalseSonBoss.SwatAwayPlayersWindup), nameof(EntityStates.FalseSonBoss.SwatAwayPlayersWindup.FixedUpdate))]
    public static class OptimizeSwatAwayPlayersWindupPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "SwatAwayPlayersWindup.FixedUpdate",
                replaceScaleDurationFromComponent: true);
    }

    [HarmonyPatch(typeof(EntityStates.Mage.Weapon.Flamethrower), nameof(EntityStates.Mage.Weapon.Flamethrower.FixedUpdate))]
    public static class OptimizeMageFlamethrowerPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "MageFlamethrower.FixedUpdate",
                replaceScaleDurationFromComponent: true);
    }

    [HarmonyPatch(typeof(EntityStates.LunarWisp.ChargeLunarGuns), nameof(EntityStates.LunarWisp.ChargeLunarGuns.FixedUpdate))]
    public static class OptimizeChargeLunarGunsPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "ChargeLunarGuns.FixedUpdate",
                replaceScaleDurationFromGameObject: true);
    }

    [HarmonyPatch(typeof(EntityStates.SolusAmalgamator.Beam), nameof(EntityStates.SolusAmalgamator.Beam.FixedUpdate))]
    public static class OptimizeBeamPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "Beam.FixedUpdate",
                replaceChildLocatorFromGameObject: true);
    }

    [HarmonyPatch(typeof(EntityStates.SolusAmalgamator.FlamethrowerCannon), nameof(EntityStates.SolusAmalgamator.FlamethrowerCannon.FixedUpdate))]
    public static class OptimizeFlamethrowerCannonPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "FlamethrowerCannon.FixedUpdate",
                replaceScaleDurationFromComponent: true);
    }

    [HarmonyPatch(typeof(EntityStates.SolusAmalgamator.FlamethrowerTurret), nameof(EntityStates.SolusAmalgamator.FlamethrowerTurret.FixedUpdate))]
    public static class OptimizeFlamethrowerTurretPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "FlamethrowerTurret.FixedUpdate",
                replaceScaleDurationFromComponent: true);
    }

    [HarmonyPatch(typeof(EntityStates.Seeker.Meditate), nameof(EntityStates.Seeker.Meditate.Update))]
    public static class OptimizeMeditatePatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OptimizeStateVfxPatchTools.ReplaceInstructions(
                instructions,
                "Meditate.Update",
                replaceChildLocatorFromGameObject: true,
                replaceChildLocator: true,
                replaceAnimatorInChildrenFromGameObject: true);
    }
}
