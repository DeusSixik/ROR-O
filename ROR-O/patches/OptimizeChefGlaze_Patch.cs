using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(EntityStates.Chef.Glaze), "FixedUpdate")]
    public class OptimizeChefGlazePatch
    {
        private static readonly System.Reflection.MethodInfo GetFastMuzzleMethod =
            AccessTools.Method(typeof(OptimizeChefGlazePatch), nameof(GetFastMuzzle));

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
            List<CodeInstruction> codeList = new List<CodeInstruction>(instructions);
            System.Reflection.MethodInfo concatMethod =
                AccessTools.Method(typeof(string), nameof(string.Concat), new[] { typeof(string), typeof(string) });

            int startIndex = -1;
            for (int i = 0; i < codeList.Count; i++)
            {
                if (codeList[i].opcode == OpCodes.Ldstr && Equals(codeList[i].operand, "MuzzleGlaze"))
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
                    codeList.Insert(startIndex + 1, new CodeInstruction(OpCodes.Call, GetFastMuzzleMethod));
                }
            }

            return codeList;
        }
    }
}
