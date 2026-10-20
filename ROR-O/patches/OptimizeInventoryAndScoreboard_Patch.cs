using System;
using System.Reflection;
using HarmonyLib;
using LookingGlass.BuffTimers;
using LookingGlass.StatsDisplay;
using MonoMod.RuntimeDetour;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;

namespace ROR_O.patches
{
    // ItemInventoryDisplay rebuilds every icon and marks the canvas dirty even when the visible items did not change.
    // Opening the scoreboard enables those displays again, so the same rebuild was repeating for an unchanged inventory.
    [HarmonyPatch(typeof(ItemInventoryDisplay), nameof(ItemInventoryDisplay.UpdateDisplay))]
    public static class SkipUnchangedItemInventoryDisplayPatch
    {
        private sealed class ShownItems
        {
            public bool InventoryHidden;
            public int Count;
            public int[] Items = Array.Empty<int>();
            public int[] Stacks = Array.Empty<int>();
            public byte[] Flags = Array.Empty<byte>();
        }

        private const byte RentalFlag = 1;
        private const byte DisabledFlag = 2;

        private static readonly AccessTools.FieldRef<ItemInventoryDisplay, bool> UpdateRequestPendingRef =
            AccessTools.FieldRefAccess<ItemInventoryDisplay, bool>("updateRequestPending");

        private static readonly AccessTools.FieldRef<ItemInventoryDisplay, ItemIndex[]> ItemOrderRef =
            AccessTools.FieldRefAccess<ItemInventoryDisplay, ItemIndex[]>("itemOrder");

        private static readonly AccessTools.FieldRef<ItemInventoryDisplay, int> ItemOrderCountRef =
            AccessTools.FieldRefAccess<ItemInventoryDisplay, int>("itemOrderCount");

        private static readonly AccessTools.FieldRef<ItemInventoryDisplay, int[]> ItemStacksRef =
            AccessTools.FieldRefAccess<ItemInventoryDisplay, int[]>("itemStacks");

        private static readonly AccessTools.FieldRef<ItemInventoryDisplay, int[]> ItemRentalStacksRef =
            AccessTools.FieldRefAccess<ItemInventoryDisplay, int[]>("itemRentalStacks");

        private static readonly AccessTools.FieldRef<ItemInventoryDisplay, ItemMask> DisabledItemsRef =
            AccessTools.FieldRefAccess<ItemInventoryDisplay, ItemMask>("disabledItems");

        private static readonly FixedConditionalWeakTable<ItemInventoryDisplay, ShownItems> ShownByDisplay =
            new FixedConditionalWeakTable<ItemInventoryDisplay, ShownItems>();

        private static bool Prefix(ItemInventoryDisplay __instance)
        {
            if (!__instance || !__instance.isActiveAndEnabled)
            {
                return true;
            }

            UpdateRequestPendingRef(__instance) = false;
            ShownItems shown = ShownByDisplay.GetOrCreateValue(__instance);
            return !Matches(__instance, shown);
        }

        private static void Postfix(ItemInventoryDisplay __instance)
        {
            if (!__instance || !__instance.isActiveAndEnabled)
            {
                return;
            }

            Remember(__instance, ShownByDisplay.GetOrCreateValue(__instance));
        }

        private static bool Matches(ItemInventoryDisplay display, ShownItems shown)
        {
            Inventory inventory = display.inventory;
            bool inventoryHidden = inventory && inventory.inventoryDisabled;
            if (shown.InventoryHidden != inventoryHidden)
            {
                return false;
            }

            if (inventoryHidden)
            {
                return shown.Count == 0;
            }

            ItemIndex[] order = ItemOrderRef(display);
            int orderCount = ItemOrderCountRef(display);
            int[] stacks = ItemStacksRef(display);
            int[] rentals = ItemRentalStacksRef(display);
            ItemMask disabled = DisabledItemsRef(display);
            int seen = 0;
            for (int i = 0; i < orderCount; i++)
            {
                ItemIndex item = order[i];
                if (!IsVisible(item))
                {
                    continue;
                }

                int itemId = (int)item;
                bool itemDisabled = disabled != null && disabled.Contains(item);
                if (inventory && !itemDisabled && (stacks == null || stacks[itemId] <= 0))
                {
                    continue;
                }

                if (seen >= shown.Count
                    || shown.Items[seen] != itemId
                    || shown.Stacks[seen] != StackAt(stacks, itemId)
                    || shown.Flags[seen] != FlagsAt(rentals, itemId, itemDisabled))
                {
                    return false;
                }

                seen++;
            }

            return seen == shown.Count;
        }

        private static void Remember(ItemInventoryDisplay display, ShownItems shown)
        {
            Inventory inventory = display.inventory;
            shown.InventoryHidden = inventory && inventory.inventoryDisabled;
            if (shown.InventoryHidden)
            {
                shown.Count = 0;
                return;
            }

            ItemIndex[] order = ItemOrderRef(display);
            int orderCount = ItemOrderCountRef(display);
            int[] stacks = ItemStacksRef(display);
            int[] rentals = ItemRentalStacksRef(display);
            ItemMask disabled = DisabledItemsRef(display);
            EnsureCapacity(shown, orderCount);
            int seen = 0;
            for (int i = 0; i < orderCount; i++)
            {
                ItemIndex item = order[i];
                if (!IsVisible(item))
                {
                    continue;
                }

                int itemId = (int)item;
                bool itemDisabled = disabled != null && disabled.Contains(item);
                if (inventory && !itemDisabled && (stacks == null || stacks[itemId] <= 0))
                {
                    continue;
                }

                shown.Items[seen] = itemId;
                shown.Stacks[seen] = StackAt(stacks, itemId);
                shown.Flags[seen] = FlagsAt(rentals, itemId, itemDisabled);
                seen++;
            }

            shown.Count = seen;
        }

        private static void EnsureCapacity(ShownItems shown, int count)
        {
            if (shown.Items.Length >= count)
            {
                return;
            }

            int capacity = Mathf.NextPowerOfTwo(Math.Max(count, 8));
            shown.Items = new int[capacity];
            shown.Stacks = new int[capacity];
            shown.Flags = new byte[capacity];
        }

        private static bool IsVisible(ItemIndex itemIndex)
        {
            ItemDef itemDef = ItemCatalog.GetItemDef(itemIndex);
            return itemDef != null && !itemDef.hidden;
        }

        private static int StackAt(int[]? stacks, int itemId)
        {
            return stacks != null && (uint)itemId < (uint)stacks.Length ? stacks[itemId] : 0;
        }

        private static byte FlagsAt(int[]? rentals, int itemId, bool itemDisabled)
        {
            byte flags = 0;
            if (rentals != null && (uint)itemId < (uint)rentals.Length && rentals[itemId] > 0)
            {
                flags |= RentalFlag;
            }

            if (itemDisabled)
            {
                flags |= DisabledFlag;
            }

            return flags;
        }
    }

    // Reopening the scoreboard calls SetMaster again for the same player and rebuilds that strip.
    [HarmonyPatch(typeof(ScoreboardStrip), nameof(ScoreboardStrip.SetMaster))]
    public static class SkipUnchangedScoreboardStripPatch
    {
        private sealed class ShownMaster
        {
            public CharacterBody? Body;
        }

        private static readonly AccessTools.FieldRef<ScoreboardStrip, CharacterMaster> MasterRef =
            AccessTools.FieldRefAccess<ScoreboardStrip, CharacterMaster>("master");

        private static readonly FixedConditionalWeakTable<ScoreboardStrip, ShownMaster> ShownByStrip =
            new FixedConditionalWeakTable<ScoreboardStrip, ShownMaster>();

        private static bool Prefix(ScoreboardStrip __instance, CharacterMaster newMaster)
        {
            if (!newMaster || MasterRef(__instance) != newMaster)
            {
                return true;
            }

            ShownMaster shown = ShownByStrip.GetOrCreateValue(__instance);
            return shown.Body != newMaster.GetBody();
        }

        private static void Postfix(ScoreboardStrip __instance, CharacterMaster newMaster)
        {
            if (!newMaster)
            {
                return;
            }

            ShownByStrip.GetOrCreateValue(__instance).Body = newMaster.GetBody();
        }
    }

    // LookingGlass writes a new timer string on every buff layout. The shown number only changes ten times a second,
    // or once a second without decimals, so identical writes were rebuilding the buff text.
    // BuffTimersClass.UpdateLayout is the method a MonoMod hook calls. Harmony rewrites that method with an IL hook
    // and deadlocks with the detour while plugins load, so the LookingGlass hook is replaced instead.
    public static class SkipUnchangedBuffTimerPatch
    {
        private sealed class ShownTimer
        {
            public TMP_Text? Text;
            public int Bucket = int.MinValue;
            public int Count = int.MinValue;
            public bool Decimal;
            public float FontSize = float.NaN;
        }

        private static readonly FixedConditionalWeakTable<Component, ShownTimer> ShownByIcon =
            new FixedConditionalWeakTable<Component, ShownTimer>();

        private static Hook? layoutHook;

        internal static void Register()
        {
            if (layoutHook != null)
            {
                return;
            }

            Type? type = AccessTools.TypeByName("LookingGlass.BuffTimers.BuffTimersClass");
            FieldInfo? hookField = type == null ? null : AccessTools.Field(type, "overrideHook");
            Hook? existing = hookField?.GetValue(null) as Hook;
            if (existing == null || hookField == null)
            {
                return;
            }

            MethodInfo? layout = AccessTools.DeclaredMethod(typeof(BuffDisplay), nameof(BuffDisplay.UpdateLayout));
            MethodInfo? replacement = AccessTools.DeclaredMethod(typeof(SkipUnchangedBuffTimerPatch), nameof(UpdateLayout));
            if (layout == null || replacement == null)
            {
                return;
            }

            try
            {
                existing.Undo();
            }
            catch (Exception ex)
            {
                RORO.GlobalLogger?.LogError($"Buff timer text updates were left on the LookingGlass hook: {ex}");
                return;
            }

            try
            {
                layoutHook = new Hook(layout, replacement);
            }
            catch (Exception ex)
            {
                existing.Apply();
                RORO.GlobalLogger?.LogError($"Buff timer text updates were left on the LookingGlass hook: {ex}");
                return;
            }

            try
            {
                existing.Free();
            }
            catch (Exception ex)
            {
                RORO.GlobalLogger?.LogWarning($"LookingGlass buff timer hook could not be released: {ex.Message}");
            }

            hookField.SetValue(null, null);
            RORO.GlobalLogger?.LogInfo("Buff timer text updates are skipped while the shown value is unchanged.");
        }

        private static void UpdateLayout(Action<BuffDisplay> orig, BuffDisplay self)
        {
            orig(self);
            if (BuffTimersClass.buffTimers == null || !BuffTimersClass.buffTimers.Value || !self || !self.source)
            {
                return;
            }

            CharacterBody? body = StatsDisplayClass.cachedUserBody;
            if (!body)
            {
                return;
            }

            bool decimals = BuffTimersClass.buffTimerDecimal != null && BuffTimersClass.buffTimerDecimal.Value;
            float fontSize = BuffTimersClass.buffTimersFontSize != null ? BuffTimersClass.buffTimersFontSize.Value : 80f;
            foreach (BuffDisplay.BuffIconDisplayData icon in self.buffIconDisplayData)
            {
                if (icon.buffIconComponent == null || icon.buffDef == null)
                {
                    continue;
                }

                float timer = -1f;
                foreach (CharacterBody.TimedBuff timedBuff in body.timedBuffs)
                {
                    if (timedBuff.buffIndex == icon.buffDef.buffIndex)
                    {
                        timer = timedBuff.timer;
                        break;
                    }
                }

                if (timer < 0f)
                {
                    continue;
                }

                int bucket = decimals ? Mathf.RoundToInt(timer * 10f) : Mathf.CeilToInt(timer);
                int count = icon.buffDef.canStack && icon.buffCount > 1 ? icon.buffCount : 0;
                ShownTimer shown = ShownByIcon.GetOrCreateValue(icon.buffIconComponent);
                if (shown.Text != null
                    && shown.Bucket == bucket
                    && shown.Count == count
                    && shown.Decimal == decimals
                    && Mathf.Approximately(shown.FontSize, fontSize))
                {
                    continue;
                }

                if (shown.Text == null)
                {
                    shown.Text = icon.buffIconComponent.GetComponentInChildren<TextMeshProUGUI>();
                }

                if (shown.Text == null)
                {
                    continue;
                }

                shown.Text.enabled = true;
                string number = decimals ? timer.ToString("0.0") : bucket.ToString();
                string text = $"<size={fontSize}%>{number}</size>\n";
                if (count > 1)
                {
                    text += "x" + count.ToString();
                }
                else
                {
                    text += " ";
                }

                shown.Bucket = bucket;
                shown.Count = count;
                shown.Decimal = decimals;
                shown.FontSize = fontSize;
                shown.Text.text = text;
            }
        }
    }
}
