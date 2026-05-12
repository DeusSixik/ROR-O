using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ROR_O.patches
{
    internal static class TextMeshHotspotDeduplication
    {
        private static readonly AccessTools.FieldRef<TMP_Text, string> CurrentTextField =
            AccessTools.FieldRefAccess<TMP_Text, string>("m_text");

        private static readonly AccessTools.FieldRef<TMP_Text, int> CurrentMaxVisibleCharactersField =
            AccessTools.FieldRefAccess<TMP_Text, int>("m_maxVisibleCharacters");

        private static readonly AccessTools.FieldRef<TMP_Text, Color> CurrentFontColorField =
            AccessTools.FieldRefAccess<TMP_Text, Color>("m_fontColor");

        private static readonly AccessTools.FieldRef<TMP_Text, float> CurrentFontSizeField =
            AccessTools.FieldRefAccess<TMP_Text, float>("m_fontSize");

        private static readonly AccessTools.FieldRef<TMP_Text, TextAlignmentOptions> CurrentAlignmentField =
            AccessTools.FieldRefAccess<TMP_Text, TextAlignmentOptions>("m_textAlignment");

        private static readonly AccessTools.FieldRef<TMP_Text, TMP_FontAsset> CurrentFontAssetField =
            AccessTools.FieldRefAccess<TMP_Text, TMP_FontAsset>("m_fontAsset");

        private static readonly AccessTools.FieldRef<Graphic, bool> CurrentVertsDirtyField =
            AccessTools.FieldRefAccess<Graphic, bool>("m_VertsDirty");

        private static readonly AccessTools.FieldRef<TMP_Text, bool> CurrentLayoutDirtyField =
            AccessTools.FieldRefAccess<TMP_Text, bool>("m_isLayoutDirty");

        private static readonly AccessTools.FieldRef<TMP_Text, bool> CurrentMaterialDirtyField =
            AccessTools.FieldRefAccess<TMP_Text, bool>("m_isMaterialDirty");

        public static bool ShouldApplyString(TMP_Text? textComponent, string? incomingText)
        {
            if (textComponent == null)
            {
                return true;
            }

            return !string.Equals(CurrentTextField(textComponent), incomingText);
        }

        public static bool ShouldApplyStringBuilder(TMP_Text? textComponent, StringBuilder? sourceText, int startIndex, int length)
        {
            if (textComponent == null || sourceText == null)
            {
                return true;
            }

            string currentText = CurrentTextField(textComponent) ?? string.Empty;
            if (length < 0 || startIndex < 0 || startIndex + length > sourceText.Length)
            {
                return true;
            }

            if (currentText.Length != length)
            {
                return true;
            }

            for (int i = 0; i < length; i++)
            {
                if (currentText[i] != sourceText[startIndex + i])
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ShouldApplyWholeStringBuilder(TMP_Text? textComponent, StringBuilder? sourceText)
        {
            if (sourceText == null)
            {
                return true;
            }

            return ShouldApplyStringBuilder(textComponent, sourceText, 0, sourceText.Length);
        }

        public static bool ShouldApplyMaxVisibleCharacters(TMP_Text? textComponent, int value)
        {
            if (textComponent == null)
            {
                return true;
            }

            return CurrentMaxVisibleCharactersField(textComponent) != value;
        }

        public static bool ShouldApplyAlpha(TMP_Text? textComponent, float value)
        {
            if (textComponent == null)
            {
                return true;
            }

            return !Mathf.Approximately(CurrentFontColorField(textComponent).a, value);
        }

        public static bool ShouldApplyFontSize(TMP_Text? textComponent, float value)
        {
            if (textComponent == null)
            {
                return true;
            }

            return !Mathf.Approximately(CurrentFontSizeField(textComponent), value);
        }

        public static bool ShouldApplyAlignment(TMP_Text? textComponent, TextAlignmentOptions value)
        {
            if (textComponent == null)
            {
                return true;
            }

            return CurrentAlignmentField(textComponent) != value;
        }

        public static bool ShouldApplyFont(TMP_Text? textComponent, TMP_FontAsset? value)
        {
            if (textComponent == null)
            {
                return true;
            }

            return CurrentFontAssetField(textComponent) != value;
        }

        public static bool ShouldSetVerticesDirty(Graphic? graphic)
        {
            if (graphic == null || !(graphic is TextMeshProUGUI))
            {
                return true;
            }

            return !CurrentVertsDirtyField(graphic);
        }

        public static bool ShouldSetLayoutDirty(TMP_Text? textComponent)
        {
            if (textComponent == null)
            {
                return true;
            }

            return !CurrentLayoutDirtyField(textComponent);
        }

        public static bool ShouldSetMaterialDirty(TMP_Text? textComponent)
        {
            if (textComponent == null)
            {
                return true;
            }

            return !CurrentMaterialDirtyField(textComponent);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "set_text")]
    public static class OptimizeTmpTextSetterPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TMP_Text __instance, string value)
        {
            return TextMeshHotspotDeduplication.ShouldApplyString(__instance, value);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string), typeof(bool) })]
    public static class OptimizeTmpTextSetTextStringPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TMP_Text __instance, string sourceText)
        {
            return TextMeshHotspotDeduplication.ShouldApplyString(__instance, sourceText);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(StringBuilder) })]
    public static class OptimizeTmpTextSetTextStringBuilderPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TMP_Text __instance, StringBuilder sourceText)
        {
            return TextMeshHotspotDeduplication.ShouldApplyWholeStringBuilder(__instance, sourceText);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(StringBuilder), typeof(int), typeof(int) })]
    public static class OptimizeTmpTextSetTextStringBuilderRangePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TMP_Text __instance, StringBuilder sourceText, int start, int length)
        {
            return TextMeshHotspotDeduplication.ShouldApplyStringBuilder(__instance, sourceText, start, length);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "set_maxVisibleCharacters")]
    public static class OptimizeTmpTextMaxVisibleCharactersPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TMP_Text __instance, int value)
        {
            return TextMeshHotspotDeduplication.ShouldApplyMaxVisibleCharacters(__instance, value);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "set_alpha")]
    public static class OptimizeTmpTextAlphaPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TMP_Text __instance, float value)
        {
            return TextMeshHotspotDeduplication.ShouldApplyAlpha(__instance, value);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "set_fontSize")]
    public static class OptimizeTmpTextFontSizePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TMP_Text __instance, float value)
        {
            return TextMeshHotspotDeduplication.ShouldApplyFontSize(__instance, value);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "set_alignment")]
    public static class OptimizeTmpTextAlignmentPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TMP_Text __instance, TextAlignmentOptions value)
        {
            return TextMeshHotspotDeduplication.ShouldApplyAlignment(__instance, value);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "set_font")]
    public static class OptimizeTmpTextFontPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TMP_Text __instance, TMP_FontAsset value)
        {
            return TextMeshHotspotDeduplication.ShouldApplyFont(__instance, value);
        }
    }

    [HarmonyPatch(typeof(Graphic), nameof(Graphic.SetVerticesDirty))]
    public static class OptimizeTmpGraphicSetVerticesDirtyPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Graphic __instance)
        {
            return TextMeshHotspotDeduplication.ShouldSetVerticesDirty(__instance);
        }
    }

    [HarmonyPatch(typeof(TextMeshProUGUI), nameof(TextMeshProUGUI.SetLayoutDirty))]
    public static class OptimizeTmpTextSetLayoutDirtyPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TextMeshProUGUI __instance)
        {
            return TextMeshHotspotDeduplication.ShouldSetLayoutDirty(__instance);
        }
    }

    [HarmonyPatch(typeof(TextMeshProUGUI), nameof(TextMeshProUGUI.SetMaterialDirty))]
    public static class OptimizeTmpTextSetMaterialDirtyPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TextMeshProUGUI __instance)
        {
            return TextMeshHotspotDeduplication.ShouldSetMaterialDirty(__instance);
        }
    }
}
