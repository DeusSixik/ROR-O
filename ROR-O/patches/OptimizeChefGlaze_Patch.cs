using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(EntityStates.Chef.Glaze), "FixedUpdate")]
    public class OptimizeChefGlazePatch
    {
        private static readonly string[] CachedMuzzles = 
        { 
            "MuzzleGlaze0", "MuzzleGlaze1", "MuzzleGlaze2", 
            "MuzzleGlaze3", "MuzzleGlaze4", "MuzzleGlaze5" 
        };

        public static string GetFastMuzzle(EntityStates.Chef.Glaze instance)
        {
            int index = instance.muzzleStringEndNum;
            if (index >= 0 && index < CachedMuzzles.Length) return CachedMuzzles[index];
            return "MuzzleGlaze" + index; // Запасной вариант
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);

            matcher.MatchForward(false, new CodeMatch(OpCodes.Ldstr, "MuzzleGlaze"));

            if (matcher.IsValid)
            {
                int startIndex = matcher.Pos;

                matcher.MatchForward(false, new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(string), nameof(string.Concat), new[] { typeof(string), typeof(string) })));
                
                if (matcher.IsValid)
                {
                    int endIndex = matcher.Pos;
                    int instructionsToRemove = endIndex - startIndex + 1;

                    matcher.Start().Advance(startIndex)
                        .RemoveInstructions(instructionsToRemove)
                        .InsertAndAdvance(
                            new CodeInstruction(OpCodes.Ldarg_0),
                            Transpilers.EmitDelegate<Func<EntityStates.Chef.Glaze, string>>(GetFastMuzzle)
                        );
                }
            }
            return matcher.InstructionEnumeration();
        }
    }
}