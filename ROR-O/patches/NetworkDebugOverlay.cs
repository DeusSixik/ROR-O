using HarmonyLib;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;

namespace ROR_O.patches
{
    internal static class NetworkDebugText
    {
        private const string Marker = " <size=70%><color=#";
        private const string OldMarker = "\n<size=62%><color=#";
        private const string MatchColor = "A9C3D4";
        private const string MismatchColor = "FF5C5C";

        public static void Apply(TMP_Text? label, ulong steamId, bool includeVersion)
        {
            if (label == null)
            {
                return;
            }

            string current = label.text ?? "";
            string baseText = Strip(current);
            string? extra = FormatExtra(steamId, includeVersion);
            string next = extra == null ? baseText : baseText + extra;
            if (!string.Equals(current, next, System.StringComparison.Ordinal))
            {
                label.text = next;
            }
        }

        public static void Strip(TMP_Text? label)
        {
            if (label == null || label.text == null)
            {
                return;
            }

            string stripped = Strip(label.text);
            if (!string.Equals(label.text, stripped, System.StringComparison.Ordinal))
            {
                label.text = stripped;
            }
        }

        private static string? FormatExtra(ulong steamId, bool includeVersion)
        {
            string version = "";
            bool mismatch = false;
            bool haveVersion = includeVersion && NetworkLinkDebug.TryDescribeVersion(steamId, out version, out mismatch);
            string? link = NetworkLinkDebug.Describe(steamId);
            if (!includeVersion && string.IsNullOrEmpty(link))
            {
                return null;
            }

            string body = haveVersion ? version : "нет данных";
            string color = haveVersion && mismatch ? MismatchColor : MatchColor;
            if (!string.IsNullOrEmpty(link))
            {
                body += " · " + link;
            }

            return Marker + color + ">" + body + "</color></size>";
        }

        private static string Strip(string text)
        {
            int marker = IndexOfMarker(text);
            return marker < 0 ? text : text.Substring(0, marker);
        }

        private static int IndexOfMarker(string text)
        {
            int current = text.IndexOf(Marker, System.StringComparison.Ordinal);
            int previous = text.IndexOf(OldMarker, System.StringComparison.Ordinal);
            if (current < 0)
            {
                return previous;
            }

            if (previous < 0)
            {
                return current;
            }

            return current < previous ? current : previous;
        }
    }

    internal sealed class NetworkDebugDriver : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = 0.25f;

        private float nextRefreshTime;

        private void Update()
        {
            NetworkLinkDebug.PublishLocalVersion();
            NetworkLinkDebug.Tick();
            if (Time.unscaledTime < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
            AllyCardController[] cards = FindObjectsOfType<AllyCardController>();
            for (int i = 0; i < cards.Length; i++)
            {
                AllyCardController card = cards[i];
                if (card == null)
                {
                    continue;
                }

                NetworkDebugText.Apply(card.nameLabel, SteamIdOf(card.sourceMaster), includeVersion: true);
            }
        }

        internal static ulong SteamIdOf(CharacterMaster? master)
        {
            NetworkUser? user = master != null ? master.playerCharacterMasterController?.networkUser : null;
            return user != null ? user.id.steamId.ID : 0;
        }
    }

    [HarmonyPatch(typeof(AllyCardController), "UpdateInfo")]
    public static class NetworkDebugAllyCardPatch
    {
        private static void Postfix(AllyCardController __instance)
        {
            NetworkDebugText.Apply(__instance.nameLabel, NetworkDebugDriver.SteamIdOf(__instance.sourceMaster), includeVersion: true);
        }
    }

    [HarmonyPatch(typeof(SocialUsernameLabel), nameof(SocialUsernameLabel.Refresh))]
    public static class NetworkDebugLobbyNamePatch
    {
        private static void Postfix(SocialUsernameLabel __instance)
        {
            NetworkDebugText.Apply(__instance.GetComponent<TMP_Text>(), __instance.userId.ID, includeVersion: true);
        }
    }
}
