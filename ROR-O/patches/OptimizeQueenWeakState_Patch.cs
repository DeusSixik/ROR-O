using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(EntityStates.BeetleQueenMonster.WeakState), nameof(EntityStates.BeetleQueenMonster.WeakState.FixedUpdate))]
    public class OptimizeQueenWeakStatePatch
    {
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
            var matcher = new CodeMatcher(instructions);

            matcher.MatchForward(false, new CodeMatch(OpCodes.Ldstr, "GrubSpawnPoint"));

            if (matcher.IsValid)
            {
                int startIndex = matcher.Pos;
                matcher.MatchForward(false, new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(string), nameof(string.Concat), new[] { typeof(string), typeof(string) })));
                
                if (matcher.IsValid)
                {
                    int instructionsToRemove = matcher.Pos - startIndex + 1;

                    matcher.Start().Advance(startIndex)
                        .RemoveInstructions(instructionsToRemove)
                        .InsertAndAdvance(Transpilers.EmitDelegate<Func<string>>(GetRandomGrubPoint));
                }
            }
            return matcher.InstructionEnumeration();
        }
    }
}