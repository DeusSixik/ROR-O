using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LookingGlass.ItemStatsNameSpace;
using RoR2;
using UnityEngine;

namespace ROR_O.patches.mods
{
    public class OptimizeLookingGlassPatch
    {
        private const int CacheLimit = 1024;

        private static readonly Dictionary<DescriptionKey, string> Descriptions =
            new Dictionary<DescriptionKey, string>();

        private static readonly MasterStamp[] Stamps = new MasterStamp[8];

        private static int stampCursor;
        private static string? languageName;
        private static int languageHash;

        internal static bool Applied { get; private set; }

        // PatchAll reflects every HarmonyPatch method. A prefix that names LookingGlass types throws
        // while plugins load when that mod is absent, and Awake stops before Risk of Options is published.
        // This method is applied only after the assembly is already loaded.
        // The formatter stays LookingGlass's own. A copied one goes stale on the next mod update, and the
        // scoreboard hook rebuilds every icon description on each SetItemIndex even when the icon did not change.
        internal static void Register(Harmony harmony)
        {
            try
            {
                Type? type = AccessTools.TypeByName("LookingGlass.ItemStatsNameSpace.ItemStats");
                MethodInfo? target = type == null ? null : AccessTools.DeclaredMethod(type, "GetItemDescription");
                MethodInfo? prefix = AccessTools.DeclaredMethod(typeof(OptimizeLookingGlassPatch), nameof(Prefix));
                MethodInfo? postfix = AccessTools.DeclaredMethod(typeof(OptimizeLookingGlassPatch), nameof(Postfix));
                if (target == null || prefix == null || postfix == null)
                {
                    if (type != null)
                    {
                        RORO.GlobalLogger?.LogWarning("LookingGlass is loaded, but GetItemDescription could not be patched.");
                    }

                    return;
                }

                harmony.Patch(target, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
                Applied = true;
                RORO.GlobalLogger?.LogInfo("LookingGlass item descriptions are reused while the item and the stats behind them stay the same.");
            }
            catch (Exception exception)
            {
                RORO.GlobalLogger?.LogError($"LookingGlass description patch was skipped: {exception}");
            }
        }

        internal static void Clear()
        {
            Descriptions.Clear();
        }

        private static bool Prefix(
            ItemDef itemDef,
            int newItemCount,
            CharacterMaster master,
            bool withOneMore,
            bool forceNew,
            ref string __result,
            out DescriptionKey __state)
        {
            if (!itemDef)
            {
                __state = default;
                return true;
            }

            __state = DescriptionKey.Create(itemDef, newItemCount, master, withOneMore, forceNew);
            if (!Descriptions.TryGetValue(__state, out string cached))
            {
                return true;
            }

            __result = cached;
            return false;
        }

        private static void Postfix(string __result, DescriptionKey __state)
        {
            if (!__state.Valid || __result == null || Descriptions.ContainsKey(__state))
            {
                return;
            }

            if (Descriptions.Count >= CacheLimit)
            {
                Descriptions.Clear();
            }

            Descriptions.Add(__state, __result);
        }

        private static int LanguageStamp()
        {
            string name = Language.currentLanguageName ?? string.Empty;
            if (ReferenceEquals(name, languageName))
            {
                return languageHash;
            }

            languageName = name;
            languageHash = name.GetHashCode();
            Descriptions.Clear();
            return languageHash;
        }

        private static void Stamp(CharacterMaster? master, out int inventory, out int body)
        {
            int frame = Time.frameCount;
            if (master)
            {
                for (int i = 0; i < Stamps.Length; i++)
                {
                    if (Stamps[i].Frame == frame && ReferenceEquals(Stamps[i].Master, master))
                    {
                        inventory = Stamps[i].Inventory;
                        body = Stamps[i].Body;
                        return;
                    }
                }
            }

            inventory = HashInventory(master);
            body = HashBody(master);
            if (!master)
            {
                return;
            }

            Stamps[stampCursor] = new MasterStamp
            {
                Frame = frame,
                Master = master,
                Inventory = inventory,
                Body = body,
            };
            stampCursor = (stampCursor + 1) % Stamps.Length;
        }

        private static int HashInventory(CharacterMaster? master)
        {
            if (!master)
            {
                return 0;
            }

            Inventory inventory = master!.inventory;
            if (!inventory)
            {
                return 0;
            }

            int hash = 17;
            int count = ItemCatalog.itemCount;
            for (int i = 0; i < count; i++)
            {
                int stacks = inventory.GetItemCountEffective((ItemIndex)i);
                if (stacks == 0)
                {
                    continue;
                }

                unchecked
                {
                    hash = (hash * 31) ^ (i * 397) ^ stacks;
                }
            }

            return hash;
        }

        private static int HashBody(CharacterMaster? master)
        {
            CharacterBody? resolved = master ? master!.GetBody() : LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (!resolved)
            {
                return 0;
            }

            CharacterBody body = resolved!;
            unchecked
            {
                int hash = Mathf.RoundToInt(body.level);
                hash = (hash * 31) ^ Mathf.RoundToInt(body.damage);
                hash = (hash * 31) ^ Mathf.RoundToInt(body.attackSpeed * 100f);
                hash = (hash * 31) ^ Mathf.RoundToInt(body.crit * 10f);
                hash = (hash * 31) ^ Mathf.RoundToInt(body.armor);
                hash = (hash * 31) ^ Mathf.RoundToInt(body.regen * 10f);
                hash = (hash * 31) ^ Mathf.RoundToInt(body.moveSpeed * 10f);
                hash = (hash * 31) ^ Mathf.RoundToInt(body.maxHealth);
                hash = (hash * 31) ^ Mathf.RoundToInt(body.maxShield);
                return hash;
            }
        }

        private readonly struct DescriptionKey : IEquatable<DescriptionKey>
        {
            public readonly bool Valid;
            private readonly int item;
            private readonly int count;
            private readonly int luckBits;
            private readonly int fontBits;
            private readonly int flags;
            private readonly int language;
            private readonly int inventory;
            private readonly int body;

            private DescriptionKey(
                int item,
                int count,
                int luckBits,
                int fontBits,
                int flags,
                int language,
                int inventory,
                int body)
            {
                Valid = true;
                this.item = item;
                this.count = count;
                this.luckBits = luckBits;
                this.fontBits = fontBits;
                this.flags = flags;
                this.language = language;
                this.inventory = inventory;
                this.body = body;
            }

            public static DescriptionKey Create(
                ItemDef itemDef,
                int count,
                CharacterMaster? master,
                bool withOneMore,
                bool forceNew)
            {
                bool calculations = ItemStats.itemStatsCalculations != null && ItemStats.itemStatsCalculations.Value;
                int inventory = 0;
                int body = 0;
                if (calculations)
                {
                    Stamp(master, out inventory, out body);
                }

                int flags = (withOneMore ? 1 : 0) | (forceNew ? 2 : 0) | (calculations ? 4 : 0);
                float luck = master ? master!.luck : 0f;
                float fontSize = ItemStats.itemStatsFontSize != null ? ItemStats.itemStatsFontSize.Value : 0f;
                return new DescriptionKey(
                    (int)itemDef.itemIndex,
                    count,
                    BitConverter.SingleToInt32Bits(luck),
                    BitConverter.SingleToInt32Bits(fontSize),
                    flags,
                    LanguageStamp(),
                    inventory,
                    body);
            }

            public bool Equals(DescriptionKey other)
            {
                return Valid == other.Valid
                    && item == other.item
                    && count == other.count
                    && luckBits == other.luckBits
                    && fontBits == other.fontBits
                    && flags == other.flags
                    && language == other.language
                    && inventory == other.inventory
                    && body == other.body;
            }

            public override bool Equals(object? obj)
            {
                return obj is DescriptionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = item;
                    hash = (hash * 31) ^ count;
                    hash = (hash * 31) ^ luckBits;
                    hash = (hash * 31) ^ fontBits;
                    hash = (hash * 31) ^ flags;
                    hash = (hash * 31) ^ language;
                    hash = (hash * 31) ^ inventory;
                    hash = (hash * 31) ^ body;
                    return hash;
                }
            }
        }

        private struct MasterStamp
        {
            public int Frame;
            public CharacterMaster? Master;
            public int Inventory;
            public int Body;
        }
    }

    [HarmonyPatch(typeof(Run), nameof(Run.Start))]
    public class ClearLookingGlassCachePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!OptimizeLookingGlassPatch.Applied)
            {
                return;
            }

            OptimizeLookingGlassPatch.Clear();
        }
    }
}
