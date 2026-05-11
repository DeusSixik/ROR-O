using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using LookingGlass.ItemStatsNameSpace;
using RoR2;

namespace ROR_O.patches.mods
{
    [HarmonyPatch]
    public class OptimizeLookingGlassPatch
    {
        internal static readonly Dictionary<string, string> DescriptionCache = new Dictionary<string, string>();
        
        static MethodBase? TargetMethod()
        {
            var type = AccessTools.TypeByName("LookingGlass.ItemStatsNameSpace.ItemStats");
            if (type == null) return null;
            RORO.GlobalLogger?.LogInfo("Mod LookingGlass found! Apply patch !");
            return AccessTools.Method(type, "GetItemDescription");
        }
        
        static bool Prefix(ItemDef itemDef, int newItemCount, CharacterMaster master, bool withOneMore, bool forceNew,
            ref string __result)
        {
            if (itemDef == null) return true;

            float luck = master?.luck ?? 0f;
            string cacheKey = $"{itemDef.itemIndex}_{newItemCount}_{luck}_{withOneMore}_{forceNew}";

            if (DescriptionCache.TryGetValue(cacheKey, out string cachedDescription))
            {
                __result = cachedDescription;
                return false;
            }
            
            if (Language.IsTokenInvalid(itemDef.descriptionToken))
            {
                __result = Language.GetString(itemDef.pickupToken);
                return false;
            }

            StringBuilder sb = new StringBuilder();

            sb.Append($"<size={ItemStats.itemStatsFontSize.Value}%>");
            sb.Append(Language.GetString(itemDef.descriptionToken));
            sb.Append("\n");

            try
            {
                if (ItemStats.itemStatsCalculations.Value &&
                    ItemDefinitions.allItemDefinitions.ContainsKey((int)itemDef.itemIndex))
                {
                    ItemStatsDef statsDef = ItemDefinitions.allItemDefinitions[(int)itemDef.itemIndex];

                    // Логика добавления текста "С еще одним таким предметом..."
                    if (withOneMore && statsDef.descriptions.Count != 0)
                    {
                        if (newItemCount == 0 || forceNew)
                            sb.Append("\nWith this item, you will have:");
                        else
                            sb.Append("\nWith another stack, you will have:");

                        newItemCount++;
                    }

                    // Пропуск логики для металлолома (Scrap), если нет нужного предмета
                    bool skipCalculations = false;
                    if (statsDef.isScrap)
                    {
                        if (master == null ||
                            master.inventory.GetItemCountEffective(DLC3Content.Items.StatsFromScrap) <= 0)
                        {
                            skipCalculations = true;
                        }
                    }

                    if (!skipCalculations)
                    {
                        List<float>? values = null;

                        if (statsDef.calculateValuesFlat != null)
                        {
                            values = statsDef.calculateValuesFlat(newItemCount);
                        }
                        else if (statsDef.calculateValuesNew != null)
                        {
                            values = statsDef.calculateValuesNew(luck, newItemCount, 1f);
                        }
                        else if (statsDef.calculateValues != null)
                        {
                            values = statsDef.calculateValues(master, newItemCount);
                        }
                        else if (statsDef.calculateValuesBody != null)
                        {
                            // Если master == null, берем тело локального игрока (защита от краша)
                            CharacterBody? body = master?.GetBody() ?? LocalUserManager.GetFirstLocalUser()?.cachedBody;
                            if (body != null)
                            {
                                values = statsDef.calculateValuesBody(body, newItemCount);
                            }
                        }

                        // Если формулы вернули данные, форматируем их БЕЗ создания мусора!
                        if (values != null)
                        {
                            AppendFormattedStats(sb, statsDef, values, true);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }

            sb.Append("</size>");
            
            string finalString = sb.ToString();
            DescriptionCache[cacheKey] = finalString;

            __result = finalString;
            return false; // Отменяем оригинальный метод LookingGlass
        }
        
        private static void AppendFormattedStats(StringBuilder sb, ItemStatsDef statsDef, List<float> values,
            bool white)
        {
            for (int i = 0; i < statsDef.descriptions.Count; ++i)
            {
                sb.Append('\n');

                if (white) sb.Append("<color=\"white\">");
                sb.Append(statsDef.descriptions[i]);
                if (white) sb.Append("</color>");

                switch (statsDef.valueTypes[i])
                {
                    case ItemStatsDef.ValueType.Healing:
                    case ItemStatsDef.ValueType.Armor:
                        sb.Append("<style=\"cIsHealing"); break;
                    case ItemStatsDef.ValueType.Damage:
                        sb.Append("<style=\"cIsDamage"); break;
                    case ItemStatsDef.ValueType.Utility:
                        sb.Append("<style=\"cIsUtility"); break;
                    default:
                        sb.Append("<style=\"cStack"); break;
                }

                switch (statsDef.measurementUnits[i])
                {
                    case ItemStatsDef.MeasurementUnits.Meters:
                        sb.Append("\">");
                        sb.Append(values[i].ToString("0.##"));
                        sb.Append("m</style>");
                        break;

                    case ItemStatsDef.MeasurementUnits.Percentage:
                        sb.Append("\">");
                        sb.Append((values[i] * 100f).ToString("0.##"));
                        sb.Append("%</style>");
                        break;

                    case ItemStatsDef.MeasurementUnits.FlatHealth:
                        sb.Append("\">");
                        sb.Append(values[i].ToString("0.##"));
                        sb.Append(" HP</style>");
                        break;

                    case ItemStatsDef.MeasurementUnits.FlatHealing:
                        sb.Append("\">");
                        sb.Append(values[i].ToString("0.##"));
                        sb.Append(" HP/s</style>");
                        break;

                    case ItemStatsDef.MeasurementUnits.Number:
                        sb.Append("\">");
                        sb.Append(values[i].ToString("0.##"));
                        sb.Append("</style>");
                        break;

                    case ItemStatsDef.MeasurementUnits.Money:
                        sb.Append("\">");
                        sb.Append(values[i].ToString("0.#"));
                        sb.Append("$</style>");
                        break;

                    case ItemStatsDef.MeasurementUnits.Seconds:
                        sb.Append("\">");
                        sb.Append(values[i].ToString("0.##"));
                        sb.Append(" seconds</style>");
                        break;

                    case ItemStatsDef.MeasurementUnits.PercentHealth:
                        sb.Append("\">");
                        sb.Append((values[i] * 100f).ToString("0.##"));
                        sb.Append("% HP</style>");
                        break;

                    case ItemStatsDef.MeasurementUnits.PercentHealing:
                        sb.Append("\">");
                        sb.Append((values[i] * 100f).ToString("0.##"));
                        sb.Append("% HP/s</style>");
                        break;

                    case ItemStatsDef.MeasurementUnits.ProcCoeff:
                        sb.Append("\">");
                        sb.Append(values[i].ToString("0.0##"));
                        sb.Append("</style>");
                        break;
                }
            }
        }
    }
    
    [HarmonyPatch(typeof(Run), nameof(Run.Start))]
    public class ClearLookingGlassCachePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            OptimizeLookingGlassPatch.DescriptionCache.Clear();
            RORO.GlobalLogger?.LogInfo("The LookingGlass cache has been successfully cleared for a new race!");
        }
    }
}
