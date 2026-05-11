using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(EntityStates.BeetleQueenMonster.WeakState), nameof(EntityStates.BeetleQueenMonster.WeakState.FixedUpdate))]
    public class OptimizeQueenWeakStatePatch
    {
        private static readonly System.Reflection.MethodInfo GetRandomGrubPointMethod =
            AccessTools.Method(typeof(OptimizeQueenWeakStatePatch), nameof(GetRandomGrubPoint));

        private static readonly string[] CachedGrubPoints = 
        { 
            "GrubSpawnPoint1", "GrubSpawnPoint2", "GrubSpawnPoint3", 
            "GrubSpawnPoint4", "GrubSpawnPoint5", "GrubSpawnPoint6", 
            "GrubSpawnPoint7", "GrubSpawnPoint8", "GrubSpawnPoint9" 
        };

        public static string GetRandomGrubPoint()
        {
            return CachedGrubPoints[UnityEngine.Random.Range(0, CachedGrubPoints.Length)];
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codeList = new List<CodeInstruction>(instructions);
            System.Reflection.MethodInfo concatMethod =
                AccessTools.Method(typeof(string), nameof(string.Concat), new[] { typeof(string), typeof(string) });

            int startIndex = -1;
            for (int i = 0; i < codeList.Count; i++)
            {
                if (codeList[i].opcode == OpCodes.Ldstr && Equals(codeList[i].operand, "GrubSpawnPoint"))
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
                    codeList.Insert(startIndex, new CodeInstruction(OpCodes.Call, GetRandomGrubPointMethod));
                }
            }

            return codeList;
        }
    }
}
