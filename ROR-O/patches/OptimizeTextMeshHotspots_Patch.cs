using System.Text;
using HarmonyLib;
using TMPro;

namespace ROR_O.patches
{
    internal static class TextMeshHotspotDeduplication
    {
        private static readonly AccessTools.FieldRef<TMP_Text, string> CurrentTextField =
            AccessTools.FieldRefAccess<TMP_Text, string>("m_text");

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
}
