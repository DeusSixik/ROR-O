using HarmonyLib;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;

namespace ROR_O.patches
{
    internal static class NetworkDebugText
    {
        private const string Marker = "\n<size=62%><color=#A9C3D4>";

        public static void Apply(TMP_Text? label, ulong steamId)
        {
            if (label == null)
            {
                return;
            }

            string current = label.text ?? "";
            string baseText = Strip(current);
            string? detail = NetworkLinkDebug.Describe(steamId);
            string next = string.IsNullOrEmpty(detail) ? baseText : baseText + Marker + detail + "</color></size>";
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

        private static string Strip(string text)
        {
            int marker = text.IndexOf(Marker, System.StringComparison.Ordinal);
            return marker < 0 ? text : text.Substring(0, marker);
        }
    }

    internal sealed class NetworkDebugDriver : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = 0.25f;

        private float nextRefreshTime;
        private bool labelsVisible;

        private void Update()
        {
            NetworkLinkDebug.Tick();
            if (Time.unscaledTime < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
            if (!ROROConfig.EnableNetworkDebug)
            {
                if (labelsVisible)
                {
                    ClearLabels();
                    labelsVisible = false;
                }

                return;
            }

            labelsVisible = true;
            AllyCardController[] cards = FindObjectsOfType<AllyCardController>();
            for (int i = 0; i < cards.Length; i++)
            {
                AllyCardController card = cards[i];
                if (card == null)
                {
                    continue;
                }

                NetworkDebugText.Apply(card.nameLabel, SteamIdOf(card.sourceMaster));
            }
        }

        private static void ClearLabels()
        {
            AllyCardController[] cards = FindObjectsOfType<AllyCardController>();
            for (int i = 0; i < cards.Length; i++)
            {
                NetworkDebugText.Strip(cards[i] != null ? cards[i].nameLabel : null);
            }

            SocialUsernameLabel[] names = FindObjectsOfType<SocialUsernameLabel>();
            for (int i = 0; i < names.Length; i++)
            {
                NetworkDebugText.Strip(names[i] != null ? names[i].GetComponent<TMP_Text>() : null);
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
            if (!ROROConfig.EnableNetworkDebug)
            {
                return;
            }

            NetworkDebugText.Apply(__instance.nameLabel, NetworkDebugDriver.SteamIdOf(__instance.sourceMaster));
        }
    }

    [HarmonyPatch(typeof(SocialUsernameLabel), nameof(SocialUsernameLabel.Refresh))]
    public static class NetworkDebugLobbyNamePatch
    {
        private static void Postfix(SocialUsernameLabel __instance)
        {
            TMP_Text label = __instance.GetComponent<TMP_Text>();
            if (!ROROConfig.EnableNetworkDebug)
            {
                NetworkDebugText.Strip(label);
                return;
            }

            NetworkDebugText.Apply(label, __instance.userId.ID);
        }
    }
}
