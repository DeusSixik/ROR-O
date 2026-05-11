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
        private static readonly System.Reflection.MethodInfo GetFastVultureMuzzleMethod =
            AccessTools.Method(typeof(OptimizeVulturesPatch), nameof(GetFastVultureMuzzle));

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
            List<CodeInstruction> codeList = new List<CodeInstruction>(instructions);
            System.Reflection.FieldInfo summonMuzzleField =
                AccessTools.Field(typeof(EntityStates.SolusWing.SummonEliteVultures),
                    nameof(EntityStates.SolusWing.SummonEliteVultures.summonMuzzleString));

            System.Reflection.MethodInfo concatMethod =
                AccessTools.Method(typeof(string), nameof(string.Concat),
                    new[] { typeof(string), typeof(string), typeof(string) });

            int startIndex = -1;
            for (int i = 0; i < codeList.Count; i++)
            {
                if (codeList[i].opcode == OpCodes.Ldsfld && Equals(codeList[i].operand, summonMuzzleField))
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex >= 0)
            {
                int endIndex = -1;
                for (int i = startIndex; i < codeList.Count; i++)
                {
                    if (codeList[i].opcode == OpCodes.Call && Equals(codeList[i].operand, concatMethod))
                    {
                        endIndex = i;
                        break;
                    }
                }

                if (endIndex >= startIndex)
                {
                    codeList.RemoveRange(startIndex, endIndex - startIndex + 1);
                    codeList.Insert(startIndex, new CodeInstruction(OpCodes.Ldarg_0));
                    codeList.Insert(startIndex + 1, new CodeInstruction(OpCodes.Call, GetFastVultureMuzzleMethod));
                }
            }

            return codeList;
        }
    }
}
