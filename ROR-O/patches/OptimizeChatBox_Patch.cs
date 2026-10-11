using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using HarmonyLib;
using RoR2;
using RoR2.UI;

namespace ROR_O.patches
{
    internal static class ChatBoxRebuildState
    {
        // The collapsed in-run chat box only shows a few lines, but vanilla lays out all 30 stored messages on every new message.
        public const int CollapsedMessageLimit = 10;

        public static readonly HashSet<ChatBox> PendingRebuild = new HashSet<ChatBox>();
        public static readonly HashSet<ChatBox> TrimmingAllowed = new HashSet<ChatBox>();
        public static readonly HashSet<ChatBox> BuiltTrimmed = new HashSet<ChatBox>();

        public static readonly AccessTools.FieldRef<ChatBox, bool> ShowInputRef =
            AccessTools.FieldRefAccess<ChatBox, bool>("_showInput");

        public static readonly Action<ChatBox> ResetFadeTimer =
            AccessTools.MethodDelegate<Action<ChatBox>>(AccessTools.Method(typeof(ChatBox), "ResetFadeTimer"));

        public static readonly Action<ChatBox> RebuildChatRects =
            AccessTools.MethodDelegate<Action<ChatBox>>(AccessTools.Method(typeof(ChatBox), "RebuildChatRects"));

        public static readonly Action<ChatBox> BuildChat =
            AccessTools.MethodDelegate<Action<ChatBox>>(AccessTools.Method(typeof(ChatBox), "BuildChat"));

        private static readonly StringBuilder TextBuilder = new StringBuilder(4096);

        public static void Rebuild(ChatBox chatBox)
        {
            BuildChat(chatBox);
            chatBox.ScrollToBottom();
        }

        public static string Join(ReadOnlyCollection<string> log, int start, int count)
        {
            TextBuilder.Clear();
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    TextBuilder.Append('\n');
                }

                TextBuilder.Append(log[start + i]);
            }

            return TextBuilder.ToString();
        }

        public static void Forget(ChatBox chatBox)
        {
            PendingRebuild.Remove(chatBox);
            TrimmingAllowed.Remove(chatBox);
            BuiltTrimmed.Remove(chatBox);
        }
    }

    [HarmonyPatch(typeof(ChatBox), "Awake")]
    public static class OptimizeChatBoxAwakePatch
    {
        private static void Postfix(ChatBox __instance)
        {
            bool allowed = __instance.allowExpandedChatbox || __instance.GetComponentInParent<HUD>() != null;
            if (allowed)
            {
                ChatBoxRebuildState.TrimmingAllowed.Add(__instance);
            }

            RORO.GlobalLogger?.LogDebug(
                $"ChatBox {__instance.name}: collapsed history trimming={(allowed ? ChatBoxRebuildState.CollapsedMessageLimit.ToString() : "off")}");
        }
    }

    [HarmonyPatch(typeof(ChatBox), "OnDestroy")]
    public static class OptimizeChatBoxOnDestroyPatch
    {
        private static void Postfix(ChatBox __instance)
        {
            ChatBoxRebuildState.Forget(__instance);
        }
    }

    [HarmonyPatch(typeof(ChatBox), "OnEnable")]
    public static class OptimizeChatBoxOnEnablePatch
    {
        private static void Postfix(ChatBox __instance)
        {
            ChatBoxRebuildState.PendingRebuild.Remove(__instance);
        }
    }

    [HarmonyPatch(typeof(ChatBox), "OnChatChangedHandler")]
    public static class OptimizeChatBoxOnChatChangedPatch
    {
        private static bool Prefix(ChatBox __instance)
        {
            ChatBoxRebuildState.ResetFadeTimer(__instance);
            if (__instance.enabled)
            {
                ChatBoxRebuildState.PendingRebuild.Add(__instance);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ChatBox), "Update")]
    public static class OptimizeChatBoxUpdatePatch
    {
        private static void Postfix(ChatBox __instance)
        {
            if (ChatBoxRebuildState.PendingRebuild.Remove(__instance))
            {
                ChatBoxRebuildState.Rebuild(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ChatBox), "showInput", MethodType.Setter)]
    public static class OptimizeChatBoxShowInputPatch
    {
        private static void Postfix(ChatBox __instance)
        {
            if (ChatBoxRebuildState.ShowInputRef(__instance) && ChatBoxRebuildState.BuiltTrimmed.Contains(__instance))
            {
                ChatBoxRebuildState.Rebuild(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ChatBox), "BuildChat")]
    public static class OptimizeChatBoxBuildChatPatch
    {
        private static bool Prefix(ChatBox __instance)
        {
            ReadOnlyCollection<string> log = Chat.readOnlyLog;
            int count = log.Count;
            int shown = count;

            ChatBox.LanguageAdjustedLineCount[]? languageLimits = __instance.languageAffectedbyLineCounts;
            if (languageLimits != null)
            {
                for (int i = 0; i < languageLimits.Length; i++)
                {
                    if (Language.currentLanguageName == languageLimits[i].languageAffected)
                    {
                        shown = Math.Min(count, languageLimits[i].limitedLines);
                    }
                }
            }

            bool trimmed = false;
            if (shown > ChatBoxRebuildState.CollapsedMessageLimit
                && !ChatBoxRebuildState.ShowInputRef(__instance)
                && ChatBoxRebuildState.TrimmingAllowed.Contains(__instance))
            {
                shown = ChatBoxRebuildState.CollapsedMessageLimit;
                trimmed = true;
            }

            if (trimmed)
            {
                ChatBoxRebuildState.BuiltTrimmed.Add(__instance);
            }
            else
            {
                ChatBoxRebuildState.BuiltTrimmed.Remove(__instance);
            }

            string text = ChatBoxRebuildState.Join(log, count - shown, shown);
            if (!string.Equals(__instance.messagesText.text, text, StringComparison.Ordinal))
            {
                __instance.messagesText.text = text;
            }

            ChatBoxRebuildState.RebuildChatRects(__instance);
            return false;
        }
    }
}
