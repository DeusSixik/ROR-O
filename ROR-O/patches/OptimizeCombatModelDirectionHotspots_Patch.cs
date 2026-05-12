using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using static ROR_O.patches.OptimizeGetComponentPatchTools;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(EntityStates.ClaymanMonster.SwipeForward), nameof(EntityStates.ClaymanMonster.SwipeForward.FixedUpdate))]
    public static class OptimizeSwipeForwardPatch
    {
        private static readonly MethodInfo ComponentGetCharacterDirectionMethod = MakeComponentGetComponentMethod(typeof(CharacterDirection));
        private static readonly MethodInfo CachedCharacterDirectionMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterDirectionFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetCharacterDirectionMethod, CachedCharacterDirectionMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"SwipeForward.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.Merc.GroundLight), nameof(EntityStates.Merc.GroundLight.FixedUpdate))]
    public static class OptimizeGroundLightPatch
    {
        private static readonly MethodInfo ComponentGetCharacterDirectionMethod = MakeComponentGetComponentMethod(typeof(CharacterDirection));
        private static readonly MethodInfo CachedCharacterDirectionMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterDirectionFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetCharacterDirectionMethod, CachedCharacterDirectionMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"GroundLight.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.Assassin.Weapon.SlashCombo), nameof(EntityStates.Assassin.Weapon.SlashCombo.FixedUpdate))]
    public static class OptimizeSlashComboPatch
    {
        private static readonly MethodInfo ComponentGetCharacterDirectionMethod = MakeComponentGetComponentMethod(typeof(CharacterDirection));
        private static readonly MethodInfo CachedCharacterDirectionMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterDirectionFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetCharacterDirectionMethod, CachedCharacterDirectionMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"SlashCombo.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.Merc.Assaulter), nameof(EntityStates.Merc.Assaulter.FixedUpdate))]
    public static class OptimizeAssaulterPatch
    {
        private static readonly MethodInfo ComponentGetCharacterModelMethod = MakeComponentGetComponentMethod(typeof(CharacterModel));
        private static readonly MethodInfo CachedCharacterModelMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterModelFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetCharacterModelMethod, CachedCharacterModelMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"Assaulter.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.Merc.Evis), nameof(EntityStates.Merc.Evis.FixedUpdate))]
    public static class OptimizeEvisPatch
    {
        private static readonly MethodInfo ComponentGetCharacterModelMethod = MakeComponentGetComponentMethod(typeof(CharacterModel));
        private static readonly MethodInfo CachedCharacterModelMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterModelFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetCharacterModelMethod, CachedCharacterModelMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"Evis.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.Merc.EvisDash), nameof(EntityStates.Merc.EvisDash.FixedUpdate))]
    public static class OptimizeEvisDashPatch
    {
        private static readonly MethodInfo ComponentGetCharacterModelMethod = MakeComponentGetComponentMethod(typeof(CharacterModel));
        private static readonly MethodInfo ComponentGetHurtBoxMethod = MakeComponentGetComponentMethod(typeof(HurtBox));

        private static readonly MethodInfo CachedCharacterModelMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetCharacterModelFromComponent));

        private static readonly MethodInfo CachedHurtBoxMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetHurtBoxFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetCharacterModelMethod, CachedCharacterModelMethod)
                    || ReplaceCall(instruction, ComponentGetHurtBoxMethod, CachedHurtBoxMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"EvisDash.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.NewtMonster.KickFromShop), nameof(EntityStates.NewtMonster.KickFromShop.FixedUpdate))]
    public static class OptimizeKickFromShopPatch
    {
        private static readonly MethodInfo ComponentGetHurtBoxGroupMethod = MakeComponentGetComponentMethod(typeof(HurtBoxGroup));
        private static readonly MethodInfo CachedHurtBoxGroupMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetHurtBoxGroupFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetHurtBoxGroupMethod, CachedHurtBoxGroupMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"KickFromShop.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.Paladin.DashSlam), nameof(EntityStates.Paladin.DashSlam.FixedUpdate))]
    public static class OptimizeDashSlamPatch
    {
        private static readonly MethodInfo ComponentGetHurtBoxMethod = MakeComponentGetComponentMethod(typeof(HurtBox));
        private static readonly MethodInfo CachedHurtBoxMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetHurtBoxFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetHurtBoxMethod, CachedHurtBoxMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"DashSlam.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.GolemMonster.ClapState), nameof(EntityStates.GolemMonster.ClapState.FixedUpdate))]
    public static class OptimizeClapStatePatch
    {
        private static readonly MethodInfo ComponentGetChildLocatorMethod = MakeComponentGetComponentMethod(typeof(ChildLocator));
        private static readonly MethodInfo CachedChildLocatorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetChildLocatorFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetChildLocatorMethod, CachedChildLocatorMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"ClapState.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }

    [HarmonyPatch(typeof(EntityStates.FalseSon.LaserFather), nameof(EntityStates.FalseSon.LaserFather.FixedUpdate))]
    public static class OptimizeLaserFatherPatch
    {
        private static readonly MethodInfo ComponentGetChildLocatorMethod = MakeComponentGetComponentMethod(typeof(ChildLocator));
        private static readonly MethodInfo CachedChildLocatorMethod = AccessTools.Method(
            typeof(OptimizedComponentCache),
            nameof(OptimizedComponentCache.GetChildLocatorFromComponent));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (ReplaceCall(instruction, ComponentGetChildLocatorMethod, CachedChildLocatorMethod))
                {
                    replacements++;
                }

                yield return instruction;
            }

            RORO.GlobalLogger?.LogInfo($"LaserFather.FixedUpdate optimized: cached GetComponent calls={replacements}");
        }
    }
}
