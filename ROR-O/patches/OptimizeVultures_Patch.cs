using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(EntityStates.SolusWing.SummonEliteVultures),
        nameof(EntityStates.SolusWing.SummonEliteVultures.FixedUpdate))]
    public class OptimizeVulturesPatch
    {
        private static readonly string[] CachedVultureMuzzles = new string[10];

        static OptimizeVulturesPatch()
        {
            string baseString = EntityStates.SolusWing.SummonEliteVultures.summonMuzzleString ?? "Muzzle";
            for (int i = 0; i < CachedVultureMuzzles.Length; i++)
            {
                CachedVultureMuzzles[i] = $"{baseString} {i}";
            }
        }

        public static string GetFastVultureMuzzle(EntityStates.SolusWing.SummonEliteVultures instance)
        {
            int count = Traverse.Create(instance).Field<int>("summonCount").Value;

            if (count >= 0 && count < CachedVultureMuzzles.Length)
                return CachedVultureMuzzles[count];

            return $"{EntityStates.SolusWing.SummonEliteVultures.summonMuzzleString} {count}";
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);

            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldsfld,
                    AccessTools.Field(typeof(EntityStates.SolusWing.SummonEliteVultures),
                        nameof(EntityStates.SolusWing.SummonEliteVultures.summonMuzzleString))));

            if (matcher.IsValid)
            {
                int startIndex = matcher.Pos;

                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Call,
                        AccessTools.Method(typeof(string), nameof(string.Concat),
                            new[] { typeof(string), typeof(string), typeof(string) })));

                if (matcher.IsValid)
                {
                    int instructionsToRemove = matcher.Pos - startIndex + 1;

                    matcher.Start().Advance(startIndex)
                        .RemoveInstructions(instructionsToRemove)
                        .InsertAndAdvance(
                            new CodeInstruction(OpCodes.Ldarg_0), // Передаем 'this'
                            Transpilers.EmitDelegate<Func<EntityStates.SolusWing.SummonEliteVultures, string>>(
                                GetFastVultureMuzzle)
                        );
                }
            }

            return matcher.InstructionEnumeration();
        }
    }
}