using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ROR_O.patches
{
    internal static class NetworkDebugText
    {
        private const string VersionOpen = " <size=70%><color=#";
        private const string DebugOpen = "\n<size=62%><color=#";
        private const string Close = "</color></size>";
        private const string MatchColor = "A9C3D4";
        private const string MismatchColor = "FF5C5C";
        private const string Missing = "нет данных";

        private static readonly Dictionary<ulong, ExtraCache> Extras = new Dictionary<ulong, ExtraCache>();
        private static readonly StringBuilder ExtraBuilder = new StringBuilder(128);

        private struct ExtraCache
        {
            public string Version;
            public string Link;
            public bool Mismatch;
            public bool Waiting;
            public string? Text;
        }

        public static void Apply(TMP_Text? label, ulong steamId)
        {
            if (label == null)
            {
                return;
            }

            if (steamId == 0)
            {
                Clear(label);
                return;
            }

            string? extra = FormatExtra(steamId);
            PlayerNameMeta? meta = label.GetComponent<PlayerNameMeta>();
            if (meta != null && meta.TryKeep(label.text, extra))
            {
                return;
            }

            string baseText = Strip(label.text ?? "");
            if (meta == null)
            {
                meta = label.gameObject.AddComponent<PlayerNameMeta>();
            }

            meta.Apply(baseText, extra);
        }

        public static void Clear(TMP_Text? label)
        {
            if (label == null)
            {
                return;
            }

            PlayerNameMeta? meta = label.GetComponent<PlayerNameMeta>();
            string stripped = Strip(label.text ?? "");
            if (meta != null)
            {
                meta.Apply(stripped, null);
                return;
            }

            if (!string.Equals(label.text, stripped, System.StringComparison.Ordinal))
            {
                label.text = stripped;
            }
        }

        private static string? FormatExtra(ulong steamId)
        {
            bool haveVersion = NetworkLinkDebug.TryDescribeVersion(steamId, out string version, out bool mismatch);
            string link = NetworkLinkDebug.Describe(steamId) ?? "";
            bool waiting = !haveVersion && NetworkLinkDebug.ShouldWaitForVersion(steamId);
            if (Extras.TryGetValue(steamId, out ExtraCache cached)
                && string.Equals(cached.Version, version, System.StringComparison.Ordinal)
                && string.Equals(cached.Link, link, System.StringComparison.Ordinal)
                && cached.Mismatch == mismatch
                && cached.Waiting == waiting)
            {
                return cached.Text;
            }

            string? text = BuildExtra(haveVersion, version, mismatch, link, waiting);
            Extras[steamId] = new ExtraCache
            {
                Version = version ?? "",
                Link = link,
                Mismatch = mismatch,
                Waiting = waiting,
                Text = text,
            };
            return text;
        }

        private static string? BuildExtra(bool haveVersion, string version, bool mismatch, string link, bool waiting)
        {
            bool haveLink = link.Length > 0;
            if (!haveVersion && !haveLink && waiting)
            {
                return null;
            }

            ExtraBuilder.Clear();
            if (haveVersion)
            {
                AppendTag(VersionOpen, mismatch ? MismatchColor : MatchColor, version);
            }
            else if (!waiting)
            {
                AppendTag(VersionOpen, MatchColor, Missing);
            }

            if (haveLink)
            {
                AppendTag(DebugOpen, MatchColor, link);
            }

            return ExtraBuilder.Length == 0 ? null : ExtraBuilder.ToString();
        }

        private static void AppendTag(string open, string color, string body)
        {
            ExtraBuilder.Append(open);
            ExtraBuilder.Append(color);
            ExtraBuilder.Append('>');
            ExtraBuilder.Append(body);
            ExtraBuilder.Append(Close);
        }

        private static string Strip(string text)
        {
            int marker = IndexOfMarker(text);
            return marker < 0 ? text : text.Substring(0, marker);
        }

        private static int IndexOfMarker(string text)
        {
            int version = text.IndexOf(VersionOpen, System.StringComparison.Ordinal);
            int debug = text.IndexOf(DebugOpen, System.StringComparison.Ordinal);
            if (version < 0)
            {
                return debug;
            }

            if (debug < 0)
            {
                return version;
            }

            return version < debug ? version : debug;
        }
    }

    internal sealed class PlayerNameMeta : MonoBehaviour
    {
        private const float HeightSlack = 0.5f;

        private TMP_Text? label;
        private LayoutElement? heightTarget;
        private ContentSizeFitter? widthFitter;
        private RectTransform? row;
        private bool captured;
        private bool baseWrapping;
        private bool baseRichText;
        private TextOverflowModes baseOverflow;
        private TextAlignmentOptions baseAlignment;
        private ContentSizeFitter.FitMode baseWidthFit;
        private float basePreferredHeight;
        private float baseMinHeight;
        private string lastBase = "";
        private string lastExtra = "";
        private string shownText = "";
        private bool usingMeta;
        private bool expanded;
        private float measuredWidth = -1f;
        private float appliedHeight = -1f;

        public bool IsExpanded => expanded;

        public bool TryKeep(string? current, string? extra)
        {
            return captured
                && string.Equals(current, shownText, System.StringComparison.Ordinal)
                && string.Equals(lastExtra, extra ?? "", System.StringComparison.Ordinal);
        }

        public void Apply(string baseText, string? extra)
        {
            if (label == null)
            {
                label = GetComponent<TMP_Text>();
                if (label == null)
                {
                    return;
                }
            }

            string suffix = extra ?? "";
            if (captured
                && string.Equals(lastBase, baseText, System.StringComparison.Ordinal)
                && string.Equals(lastExtra, suffix, System.StringComparison.Ordinal))
            {
                if (!string.Equals(label.text, shownText, System.StringComparison.Ordinal))
                {
                    label.text = shownText;
                }

                return;
            }

            if (!EnsureCaptured())
            {
                string fallback = suffix.Length == 0 ? baseText : baseText + suffix;
                if (!string.Equals(label.text, fallback, System.StringComparison.Ordinal))
                {
                    label.text = fallback;
                }

                return;
            }

            lastBase = baseText;
            lastExtra = suffix;
            usingMeta = suffix.Length > 0;
            if (usingMeta)
            {
                label.enableWordWrapping = true;
                label.overflowMode = TextOverflowModes.Overflow;
                label.richText = true;
                if (widthFitter != null)
                {
                    widthFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                }
            }
            else
            {
                label.enableWordWrapping = baseWrapping;
                label.overflowMode = baseOverflow;
                label.richText = baseRichText;
                label.alignment = baseAlignment;
                if (widthFitter != null)
                {
                    widthFitter.horizontalFit = baseWidthFit;
                }

                RestoreHeight();
            }

            shownText = usingMeta ? baseText + suffix : baseText;
            if (!string.Equals(label.text, shownText, System.StringComparison.Ordinal))
            {
                label.text = shownText;
            }

            if (usingMeta)
            {
                measuredWidth = -1f;
                FitHeight();
            }
        }

        private void LateUpdate()
        {
            if (!usingMeta || label == null || !captured)
            {
                return;
            }

            float width = label.rectTransform.rect.width;
            if (width < 2f || Mathf.Abs(width - measuredWidth) < HeightSlack)
            {
                return;
            }

            FitHeight();
        }

        private bool EnsureCaptured()
        {
            if (captured && basePreferredHeight > 1f)
            {
                return true;
            }

            if (label == null)
            {
                return false;
            }

            baseWrapping = label.enableWordWrapping;
            baseOverflow = label.overflowMode;
            baseRichText = label.richText;
            baseAlignment = label.alignment;
            widthFitter = label.GetComponent<ContentSizeFitter>();
            if (widthFitter != null)
            {
                baseWidthFit = widthFitter.horizontalFit;
            }

            AllyCardController? card = GetComponentInParent<AllyCardController>();
            if (card != null && card.layoutElement != null)
            {
                heightTarget = card.layoutElement;
                row = card.rectTransform;
            }
            else
            {
                row = FindRow((RectTransform)transform);
                heightTarget = row.GetComponent<LayoutElement>();
            }

            float current = row.rect.height;
            float preferred = heightTarget != null ? heightTarget.preferredHeight : 0f;
            basePreferredHeight = preferred > 1f ? preferred : current;
            baseMinHeight = heightTarget != null ? heightTarget.minHeight : 0f;
            if (basePreferredHeight <= 1f)
            {
                return false;
            }

            captured = true;
            appliedHeight = basePreferredHeight;
            return true;
        }

        private void FitHeight()
        {
            if (label == null || row == null)
            {
                return;
            }

            float width = label.rectTransform.rect.width;
            if (width < 2f)
            {
                return;
            }

            measuredWidth = width;
            float textHeight = label.GetPreferredValues(shownText, width, 0f).y;
            if (textHeight < 1f)
            {
                measuredWidth = -1f;
                return;
            }

            float oneLine = label.GetPreferredValues("Ay", width, 0f).y;
            float extraHeight = Mathf.Max(0f, textHeight - oneLine);
            bool nowExpanded = extraHeight > HeightSlack;
            expanded = nowExpanded;
            label.alignment = nowExpanded ? TopAlignment(baseAlignment) : baseAlignment;
            float height = basePreferredHeight + extraHeight;
            if (Mathf.Abs(appliedHeight - height) < HeightSlack)
            {
                return;
            }

            if (heightTarget == null)
            {
                heightTarget = row.gameObject.AddComponent<LayoutElement>();
                baseMinHeight = 0f;
            }

            appliedHeight = height;
            heightTarget.preferredHeight = height;
            heightTarget.minHeight = nowExpanded ? height : baseMinHeight;
            row.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            LayoutRebuilder.MarkLayoutForRebuild(row);
        }

        private void RestoreHeight()
        {
            expanded = false;
            measuredWidth = -1f;
            if (row == null || appliedHeight < 0f || Mathf.Abs(appliedHeight - basePreferredHeight) < HeightSlack)
            {
                return;
            }

            appliedHeight = basePreferredHeight;
            if (heightTarget != null)
            {
                heightTarget.preferredHeight = basePreferredHeight;
                heightTarget.minHeight = baseMinHeight;
            }

            row.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, basePreferredHeight);
            LayoutRebuilder.MarkLayoutForRebuild(row);
        }

        private static TextAlignmentOptions TopAlignment(TextAlignmentOptions alignment)
        {
            return (TextAlignmentOptions)(((int)alignment & 0xFF) | 0x100);
        }

        private static RectTransform FindRow(RectTransform start)
        {
            Transform current = start;
            for (int i = 0; i < 6 && current.parent != null; i++)
            {
                if (current.parent.GetComponent<VerticalLayoutGroup>() != null
                    || current.parent.GetComponent<GridLayoutGroup>() != null)
                {
                    return (RectTransform)current;
                }

                current = current.parent;
            }

            Transform? parent = start.parent;
            if (parent != null && parent.GetComponent<ChildLocator>() != null)
            {
                return (RectTransform)parent;
            }

            Transform? grandparent = parent != null ? parent.parent : null;
            if (grandparent != null && grandparent.GetComponent<ChildLocator>() != null)
            {
                return (RectTransform)grandparent;
            }

            return parent as RectTransform ?? start;
        }
    }

    internal sealed class NetworkDebugDriver : MonoBehaviour
    {
        // The card is handed over by its own UpdateInfo and kept. A destroyed card compares false
        // and is dropped on the next driver update, including while the debug overlay is off.
        private static readonly List<AllyCardController> SeenCards = new List<AllyCardController>(8);

        private bool overlayWasOn;

        internal static void Note(AllyCardController card)
        {
            if (SeenCards.Contains(card))
            {
                return;
            }

            SeenCards.Add(card);
        }

        private void Update()
        {
            NetworkLinkDebug.PublishLocalVersion();
            bool sampled = NetworkLinkDebug.Tick();
            bool overlay = SteamP2PSettings.DebugOverlay;
            if (!overlay)
            {
                if (overlayWasOn)
                {
                    WriteSeen();
                    overlayWasOn = false;
                }
                else
                {
                    DropDestroyed();
                }

                return;
            }

            overlayWasOn = true;
            if (!sampled)
            {
                DropDestroyed();
                return;
            }

            WriteSeen();
        }

        private static void DropDestroyed()
        {
            int write = 0;
            for (int i = 0; i < SeenCards.Count; i++)
            {
                AllyCardController card = SeenCards[i];
                if (!card)
                {
                    continue;
                }

                if (write != i)
                {
                    SeenCards[write] = card;
                }

                write++;
            }

            if (write < SeenCards.Count)
            {
                SeenCards.RemoveRange(write, SeenCards.Count - write);
            }
        }

        private static void WriteSeen()
        {
            for (int i = SeenCards.Count - 1; i >= 0; i--)
            {
                AllyCardController card = SeenCards[i];
                if (!card)
                {
                    SeenCards.RemoveAt(i);
                    continue;
                }

                if (IsPlayer(card.sourceMaster))
                {
                    NetworkDebugText.Apply(card.nameLabel, SteamIdOf(card.sourceMaster));
                    continue;
                }

                NetworkDebugText.Clear(card.nameLabel);
            }
        }

        internal static bool IsPlayer(CharacterMaster? master)
        {
            return master != null && master.playerCharacterMasterController != null;
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
            NetworkDebugDriver.Note(__instance);
            if (!NetworkDebugDriver.IsPlayer(__instance.sourceMaster))
            {
                NetworkDebugText.Clear(__instance.nameLabel);
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
            NetworkDebugText.Apply(__instance.GetComponent<TMP_Text>(), __instance.userId.ID);
        }
    }

    // Ally cards are top-anchored and the stock layout overlaps a taller card with the next one.
    // Once a player row grows for a wrapped version or the connection line, stack the rows by their full height.
    [HarmonyPatch(typeof(AllyCardManager), nameof(AllyCardManager.SetLayoutVertical))]
    public static class NetworkDebugAllyCardLayoutPatch
    {
        private static void Postfix(AllyCardManager __instance)
        {
            RectTransform root = (RectTransform)__instance.transform;
            bool expanded = false;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                PlayerNameMeta? meta = child.GetComponentInChildren<PlayerNameMeta>();
                if (meta != null && meta.IsExpanded)
                {
                    expanded = true;
                    break;
                }
            }

            if (!expanded)
            {
                return;
            }

            float y = 0f;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                RectTransform rect = (RectTransform)child;
                LayoutElement? layout = child.GetComponent<LayoutElement>();
                float height = layout != null && layout.preferredHeight > 1f ? layout.preferredHeight : rect.rect.height;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                Vector2 anchored = rect.anchoredPosition;
                anchored.y = y;
                rect.anchoredPosition = anchored;
                y -= height;
            }
        }
    }
}
