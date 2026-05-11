using HarmonyLib;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(SniperRangeIndicator), "FixedUpdate")]
    public class OptimizeSniperUIPatch
    {
        private static readonly string[] CachedDistances = new string[1000];
        private static readonly string MaxDistanceString = "Dis: 999m";
        private static readonly string InfinityString = "Dis: ---m";

        static OptimizeSniperUIPatch()
        {
            for (int i = 0; i < 1000; i++)
            {
                CachedDistances[i] = $"Dis: {i:D3}m";
            }
        }

        static bool Prefix(SniperRangeIndicator __instance)
        {
            float num = float.PositiveInfinity;
            
            if (__instance.hudElement.targetCharacterBody)
            {
                if (__instance.hudElement.targetCharacterBody.TryGetComponent<InputBankTest>(out var component))
                {
                    Ray ray = new Ray(component.aimOrigin, component.aimDirection);
                    if (Util.CharacterRaycast(__instance.hudElement.targetCharacterBody.gameObject, ray, out RaycastHit raycastHit, float.PositiveInfinity, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.UseGlobal))
                    {
                        num = raycastHit.distance;
                    }
                }
            }
            
            if (float.IsInfinity(num))
            {
                __instance.label.text = InfinityString;
            }
            else if (num > 999f)
            {
                __instance.label.text = MaxDistanceString;
            }
            else
            {
                int distanceInt = Mathf.FloorToInt(num);
                __instance.label.text = CachedDistances[distanceInt];
            }

            return false;
        }
    }
}