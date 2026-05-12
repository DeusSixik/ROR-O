using HarmonyLib;
using RoR2.UI;
using TMPro;
using UnityEngine;

namespace ROR_O.patches
{
    internal static class UiTextStyleHotspotTools
    {
        public static bool IsAlreadyApplied(UISkinData.TextStyle textStyle, TextMeshProUGUI? label, bool useAlignment)
        {
            if (label == null)
            {
                return false;
            }

            if (label.font != textStyle.font)
            {
                return false;
            }

            if (!Mathf.Approximately(label.fontSize, textStyle.fontSize))
            {
                return false;
            }

            if (!AreColorsEqual(label.color, textStyle.color))
            {
                return false;
            }

            if (useAlignment && label.alignment != textStyle.alignment)
            {
                return false;
            }

            return true;
        }

        private static bool AreColorsEqual(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r)
                && Mathf.Approximately(a.g, b.g)
                && Mathf.Approximately(a.b, b.b)
                && Mathf.Approximately(a.a, b.a);
        }
    }

    [HarmonyPatch(typeof(UISkinData.TextStyle), nameof(UISkinData.TextStyle.Apply))]
    public static class OptimizeUiSkinTextStyleApplyPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(UISkinData.TextStyle __instance, TextMeshProUGUI label, bool useAlignment)
        {
            return !UiTextStyleHotspotTools.IsAlreadyApplied(__instance, label, useAlignment);
        }
    }
}
