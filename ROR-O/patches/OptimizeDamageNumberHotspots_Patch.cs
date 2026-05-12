using HarmonyLib;
using RoR2;
using UnityEngine;

namespace ROR_O.patches
{
    internal enum DamageNumberLoadLevel
    {
        None,
        Soft,
        Hard,
        Absolute
    }

    internal static class DamageNumberLoadShedState
    {
        private static readonly AccessTools.FieldRef<DamageNumberManager, ParticleSystem> ParticleSystemField =
            AccessTools.FieldRefAccess<DamageNumberManager, ParticleSystem>("ps");

        private static int lastFrame = -1;
        private static int spawnsThisFrame;
        private static uint noisySpawnSequence;
        private static float recentPeakDamage = 1f;
        private static float lastPeakUpdateTime;

        public static bool ShouldAllowSpawn(DamageNumberManager? manager, float damage, bool crit, DamageColorIndex damageColorIndex)
        {
            if (!ROROConfig.EnableDamageNumberLoadShedding)
            {
                return true;
            }

            int currentFrame = Time.frameCount;
            if (currentFrame != lastFrame)
            {
                lastFrame = currentFrame;
                spawnsThisFrame = 0;
            }

            int activeDamageNumbers = GetActiveDamageNumberCount(manager);
            bool criticalColor = IsCriticalColor(damageColorIndex);
            UpdateRecentPeakDamage(damage);
            int softParticleCap = GetOrderedThreshold(ROROConfig.SoftParticleCap, 1);
            int hardParticleCap = GetOrderedThreshold(ROROConfig.HardParticleCap, softParticleCap);
            int absoluteParticleCap = GetOrderedThreshold(ROROConfig.AbsoluteParticleCap, hardParticleCap);

            int softSpawnsPerFrame = GetOrderedThreshold(ROROConfig.SoftSpawnsPerFrame, 1);
            int hardSpawnsPerFrame = GetOrderedThreshold(ROROConfig.HardSpawnsPerFrame, softSpawnsPerFrame);
            int absoluteSpawnsPerFrame = GetOrderedThreshold(ROROConfig.AbsoluteSpawnsPerFrame, hardSpawnsPerFrame);

            DamageNumberLoadLevel loadLevel = GetLoadLevel(
                activeDamageNumbers,
                spawnsThisFrame,
                softParticleCap,
                hardParticleCap,
                absoluteParticleCap,
                softSpawnsPerFrame,
                hardSpawnsPerFrame,
                absoluteSpawnsPerFrame);

            if (loadLevel == DamageNumberLoadLevel.None)
            {
                spawnsThisFrame++;
                return true;
            }

            if (criticalColor || crit || IsHighPriorityDamage(damage, loadLevel))
            {
                spawnsThisFrame++;
                return true;
            }

            if (loadLevel == DamageNumberLoadLevel.Absolute)
            {
                if (!ShouldSample(crit, criticalColor, 3, 8))
                {
                    return false;
                }
            }
            else if (loadLevel == DamageNumberLoadLevel.Hard)
            {
                if (!ShouldSample(crit, criticalColor, 2, 4))
                {
                    return false;
                }
            }
            else
            {
                if (!ShouldSample(crit, criticalColor, 1, 2))
                {
                    return false;
                }
            }

            spawnsThisFrame++;
            return true;
        }

        private static int GetActiveDamageNumberCount(DamageNumberManager? manager)
        {
            if (manager == null)
            {
                return 0;
            }

            ParticleSystem particleSystem = ParticleSystemField(manager);
            if (particleSystem == null)
            {
                return 0;
            }

            return particleSystem.particleCount;
        }

        private static void UpdateRecentPeakDamage(float damage)
        {
            float now = Time.unscaledTime;
            float currentPeak = recentPeakDamage;
            float deltaTime = now - lastPeakUpdateTime;
            lastPeakUpdateTime = now;

            float halfLifeSeconds = Mathf.Max(0.05f, ROROConfig.PeakDamageHalfLifeSeconds);
            if (deltaTime > 0f)
            {
                float decayFactor = Mathf.Pow(0.5f, deltaTime / halfLifeSeconds);
                currentPeak = Mathf.Max(1f, currentPeak * decayFactor);
            }

            if (damage > currentPeak)
            {
                currentPeak = damage;
            }

            recentPeakDamage = Mathf.Max(1f, currentPeak);
        }

        private static DamageNumberLoadLevel GetLoadLevel(
            int activeDamageNumbers,
            int currentSpawnsPerFrame,
            int softParticleCap,
            int hardParticleCap,
            int absoluteParticleCap,
            int softSpawnsPerFrame,
            int hardSpawnsPerFrame,
            int absoluteSpawnsPerFrame)
        {
            if (activeDamageNumbers >= absoluteParticleCap || currentSpawnsPerFrame >= absoluteSpawnsPerFrame)
            {
                return DamageNumberLoadLevel.Absolute;
            }

            if (activeDamageNumbers >= hardParticleCap || currentSpawnsPerFrame >= hardSpawnsPerFrame)
            {
                return DamageNumberLoadLevel.Hard;
            }

            if (activeDamageNumbers >= softParticleCap || currentSpawnsPerFrame >= softSpawnsPerFrame)
            {
                return DamageNumberLoadLevel.Soft;
            }

            return DamageNumberLoadLevel.None;
        }

        private static int GetOrderedThreshold(int configuredValue, int minimumValue)
        {
            return configuredValue < minimumValue ? minimumValue : configuredValue;
        }

        private static bool IsHighPriorityDamage(float damage, DamageNumberLoadLevel loadLevel)
        {
            float fraction;
            float minimumDamage;

            switch (loadLevel)
            {
                case DamageNumberLoadLevel.Absolute:
                    fraction = GetOrderedFraction(ROROConfig.AbsolutePeakDamageFraction, ROROConfig.HardPeakDamageFraction);
                    minimumDamage = GetOrderedFloat(ROROConfig.AbsoluteMinimumDamage, ROROConfig.HardMinimumDamage);
                    break;

                case DamageNumberLoadLevel.Hard:
                    fraction = GetOrderedFraction(ROROConfig.HardPeakDamageFraction, ROROConfig.SoftPeakDamageFraction);
                    minimumDamage = GetOrderedFloat(ROROConfig.HardMinimumDamage, ROROConfig.SoftMinimumDamage);
                    break;

                default:
                    fraction = GetOrderedFraction(ROROConfig.SoftPeakDamageFraction, 0f);
                    minimumDamage = GetOrderedFloat(ROROConfig.SoftMinimumDamage, 0f);
                    break;
            }

            float peakThreshold = recentPeakDamage * fraction;
            float effectiveThreshold = Mathf.Max(minimumDamage, peakThreshold);
            return damage >= effectiveThreshold;
        }

        private static float GetOrderedFraction(float configuredValue, float minimumValue)
        {
            return Mathf.Clamp(configuredValue < minimumValue ? minimumValue : configuredValue, 0f, 1f);
        }

        private static float GetOrderedFloat(float configuredValue, float minimumValue)
        {
            return configuredValue < minimumValue ? minimumValue : configuredValue;
        }

        private static bool ShouldSample(bool crit, bool criticalColor, int critDivisor, int noisyDivisor)
        {
            if (criticalColor)
            {
                return true;
            }

            int divisor = crit ? critDivisor : noisyDivisor;
            if (divisor <= 1)
            {
                return true;
            }

            unchecked
            {
                noisySpawnSequence++;
            }

            return noisySpawnSequence % (uint)divisor == 0u;
        }

        private static bool IsCriticalColor(DamageColorIndex damageColorIndex)
        {
            if (!ROROConfig.PreserveImportantColors)
            {
                return false;
            }

            switch (damageColorIndex)
            {
                case DamageColorIndex.Heal:
                case DamageColorIndex.CritHeal:
                case DamageColorIndex.WeakPoint:
                case DamageColorIndex.Sniper:
                case DamageColorIndex.Luminous:
                case DamageColorIndex.KnockBackHitEnemies:
                    return true;
                default:
                    return false;
            }
        }
    }

    [HarmonyPatch(typeof(DamageNumberManager), nameof(DamageNumberManager.SpawnDamageNumber))]
    public static class OptimizeDamageNumberManagerSpawnPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(DamageNumberManager __instance, float amount, bool crit, DamageColorIndex damageColorIndex)
        {
            bool preserveCrit = ROROConfig.PreserveCriticalHits && crit;
            return DamageNumberLoadShedState.ShouldAllowSpawn(__instance, amount, preserveCrit, damageColorIndex);
        }
    }
}
