using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;

namespace ROR_O.patches
{
    internal static class MorgueHistoryRunReportCache
    {
        private const int MaxCachedEntries = 256;

        private sealed class CachedEntry
        {
            public string Path = string.Empty;
            public DateTime LastModifiedUtc;
            public RunReport? RunReport;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CachedEntry> Entries = new Dictionary<string, CachedEntry>(StringComparer.Ordinal);

        private static readonly FieldInfo? HistoryPathField =
            AccessTools.Field(typeof(MorgueManager.HistoryFileInfo), "path");

        private static readonly FieldInfo? HistoryLastModifiedField =
            AccessTools.Field(typeof(MorgueManager.HistoryFileInfo), "lastModified");

        public static bool TryGet(MorgueManager.HistoryFileInfo historyFileInfo, out RunReport? runReport)
        {
            string cacheKey = BuildCacheKey(historyFileInfo, out _, out _);

            lock (Sync)
            {
                if (Entries.TryGetValue(cacheKey, out CachedEntry cachedEntry) && cachedEntry.RunReport != null)
                {
                    runReport = cachedEntry.RunReport;
                    return true;
                }
            }

            runReport = null;
            return false;
        }

        public static void Store(MorgueManager.HistoryFileInfo historyFileInfo, RunReport? runReport)
        {
            if (runReport == null)
            {
                return;
            }

            string cacheKey = BuildCacheKey(historyFileInfo, out string path, out DateTime lastModifiedUtc);

            lock (Sync)
            {
                Entries[cacheKey] = new CachedEntry
                {
                    Path = path,
                    LastModifiedUtc = lastModifiedUtc,
                    RunReport = runReport
                };

                if (Entries.Count > MaxCachedEntries)
                {
                    Entries.Clear();
                }
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                Entries.Clear();
            }
        }

        private static string BuildCacheKey(MorgueManager.HistoryFileInfo historyFileInfo, out string path, out DateTime lastModifiedUtc)
        {
            path = HistoryPathField?.GetValue(historyFileInfo)?.ToString() ?? string.Empty;
            lastModifiedUtc = HistoryLastModifiedField != null
                ? (DateTime)(HistoryLastModifiedField.GetValue(historyFileInfo) ?? default(DateTime))
                : default;

            return path + "|" + lastModifiedUtc.Ticks;
        }
    }

    [HarmonyPatch(typeof(MorgueManager.HistoryFileInfo), nameof(MorgueManager.HistoryFileInfo.LoadRunReport))]
    public static class OptimizeMorgueHistoryFileInfoLoadRunReportPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(MorgueManager.HistoryFileInfo __instance, ref RunReport __result)
        {
            if (MorgueHistoryRunReportCache.TryGet(__instance, out RunReport? cachedRunReport) && cachedRunReport != null)
            {
                __result = cachedRunReport;
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(MorgueManager.HistoryFileInfo __instance, RunReport __result)
        {
            MorgueHistoryRunReportCache.Store(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(MorgueManager), nameof(MorgueManager.AddRunReportToHistory))]
    public static class OptimizeMorgueAddRunReportToHistoryPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            MorgueHistoryRunReportCache.Clear();
        }
    }

    [HarmonyPatch(typeof(MorgueManager), nameof(MorgueManager.EnforceHistoryLimit))]
    public static class OptimizeMorgueEnforceHistoryLimitPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            MorgueHistoryRunReportCache.Clear();
        }
    }
}
