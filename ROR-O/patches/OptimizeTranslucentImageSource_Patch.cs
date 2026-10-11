using HarmonyLib;
using LeTai.Asset.TranslucentImage;
using UnityEngine;

namespace ROR_O.patches
{
    internal static class TranslucentImageSourceCache
    {
        private const float MissingSourceRetrySeconds = 0.5f;

        private static TranslucentImageSource? cachedSource;
        private static float retryAfterTime;

        public static TranslucentImageSource? Find()
        {
            if (cachedSource != null && cachedSource.gameObject.activeInHierarchy)
            {
                return cachedSource;
            }

            float now = Time.unscaledTime;
            if (now < retryAfterTime)
            {
                return null;
            }

            cachedSource = Object.FindObjectOfType<TranslucentImageSource>();
            if (cachedSource == null)
            {
                retryAfterTime = now + MissingSourceRetrySeconds;
            }

            return cachedSource;
        }

        public static void Remember(TranslucentImageSource source)
        {
            retryAfterTime = 0f;
            if (cachedSource == null || !cachedSource.gameObject.activeInHierarchy)
            {
                cachedSource = source;
            }
        }
    }

    [HarmonyPatch(typeof(TranslucentImage), "UpdateSource")]
    public static class OptimizeTranslucentImageUpdateSourcePatch
    {
        private static bool Prefix(TranslucentImage __instance)
        {
            __instance.source = TranslucentImageSourceCache.Find();
            return false;
        }
    }

    [HarmonyPatch(typeof(TranslucentImageSource), "OnEnable")]
    public static class RememberTranslucentImageSourcePatch
    {
        private static void Postfix(TranslucentImageSource __instance)
        {
            TranslucentImageSourceCache.Remember(__instance);
        }
    }
}
