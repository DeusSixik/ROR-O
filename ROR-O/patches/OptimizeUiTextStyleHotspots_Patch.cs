using HarmonyLib;
using RoR2.UI;
using RoR2.UI.SkinControllers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

            bool keepsLanguageFont = label is HGTextMeshProUGUI languageLabel && languageLabel.useLanguageDefaultFont;
            if (!keepsLanguageFont && label.font != textStyle.font)
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

    [HarmonyPatch(typeof(ButtonSkinController), "UpdateLabelStyle")]
    public static class OptimizeButtonSkinLabelStylePatch
    {
        private sealed class AppliedLabelStyle
        {
            public UISkinData? Skin;
            public bool Interactable;
            public bool UseAlignment;
            public int LabelId;
            public TMP_FontAsset? Font;
            public float FontSize;
            public TextAlignmentOptions Alignment;
            public Color Color;
            public bool Valid;
        }

        private static readonly FixedConditionalWeakTable<ButtonSkinController, AppliedLabelStyle> AppliedStyles =
            new FixedConditionalWeakTable<ButtonSkinController, AppliedLabelStyle>();

        [HarmonyPrefix]
        private static bool Prefix(ButtonSkinController __instance, ref UISkinData.ButtonStyle buttonStyle, Button? ___button)
        {
            if (!__instance.useRecommendedLabel || ___button == null)
            {
                return false;
            }

            TextMeshProUGUI? label = ___button.GetComponentInChildren<TextMeshProUGUI>();
            if (label == null)
            {
                return false;
            }

            bool interactable = ___button.interactable;
            UISkinData.TextStyle style = interactable ? buttonStyle.interactableTextStyle : buttonStyle.disabledTextStyle;
            AppliedLabelStyle state = AppliedStyles.GetOrCreateValue(__instance);
            if (state.Valid
                && state.Skin == __instance.skinData
                && state.Interactable == interactable
                && state.UseAlignment == __instance.useRecommendedAlignment
                && state.LabelId == label.GetInstanceID()
                && state.Font == style.font
                && Mathf.Approximately(state.FontSize, style.fontSize)
                && state.Alignment == style.alignment
                && state.Color == style.color)
            {
                return false;
            }

            state.Skin = __instance.skinData;
            state.Interactable = interactable;
            state.UseAlignment = __instance.useRecommendedAlignment;
            state.LabelId = label.GetInstanceID();
            state.Font = style.font;
            state.FontSize = style.fontSize;
            state.Alignment = style.alignment;
            state.Color = style.color;
            state.Valid = true;
            return true;
        }
    }
}
