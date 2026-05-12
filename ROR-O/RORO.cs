using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ROR_O
{
    internal static class ROROConfig
    {
        private static ConfigEntry<bool>? enableDamageNumberLoadShedding;
        private static ConfigEntry<int>? softParticleCap;
        private static ConfigEntry<int>? hardParticleCap;
        private static ConfigEntry<int>? absoluteParticleCap;

        private static ConfigEntry<int>? softSpawnsPerFrame;
        private static ConfigEntry<int>? hardSpawnsPerFrame;
        private static ConfigEntry<int>? absoluteSpawnsPerFrame;

        private static ConfigEntry<float>? softPeakDamageFraction;
        private static ConfigEntry<float>? hardPeakDamageFraction;
        private static ConfigEntry<float>? absolutePeakDamageFraction;

        private static ConfigEntry<float>? softMinimumDamage;
        private static ConfigEntry<float>? hardMinimumDamage;
        private static ConfigEntry<float>? absoluteMinimumDamage;

        private static ConfigEntry<float>? peakDamageHalfLifeSeconds;

        private static ConfigEntry<bool>? preserveCriticalHits;
        private static ConfigEntry<bool>? preserveImportantColors;

        public static bool EnableDamageNumberLoadShedding => enableDamageNumberLoadShedding?.Value ?? true;
        public static int SoftParticleCap => softParticleCap?.Value ?? 128;
        public static int HardParticleCap => hardParticleCap?.Value ?? 192;
        public static int AbsoluteParticleCap => absoluteParticleCap?.Value ?? 256;

        public static int SoftSpawnsPerFrame => softSpawnsPerFrame?.Value ?? 24;
        public static int HardSpawnsPerFrame => hardSpawnsPerFrame?.Value ?? 40;
        public static int AbsoluteSpawnsPerFrame => absoluteSpawnsPerFrame?.Value ?? 56;

        public static float SoftPeakDamageFraction => softPeakDamageFraction?.Value ?? 0.03f;
        public static float HardPeakDamageFraction => hardPeakDamageFraction?.Value ?? 0.08f;
        public static float AbsolutePeakDamageFraction => absolutePeakDamageFraction?.Value ?? 0.16f;

        public static float SoftMinimumDamage => softMinimumDamage?.Value ?? 1f;
        public static float HardMinimumDamage => hardMinimumDamage?.Value ?? 2f;
        public static float AbsoluteMinimumDamage => absoluteMinimumDamage?.Value ?? 4f;

        public static float PeakDamageHalfLifeSeconds => peakDamageHalfLifeSeconds?.Value ?? 1.5f;

        public static bool PreserveCriticalHits => preserveCriticalHits?.Value ?? true;
        public static bool PreserveImportantColors => preserveImportantColors?.Value ?? true;

        public static void Bind(ConfigFile config)
        {
            const string damageNumbersSection = "Damage Numbers";

            enableDamageNumberLoadShedding = config.Bind(
                damageNumbersSection,
                "Enable Load Shedding",
                true,
                "Reduces the number of spawned damage number particles during heavy combat.");

            softParticleCap = config.Bind(
                damageNumbersSection,
                "Soft Particle Cap",
                128,
                "Starts lightly sampling regular damage numbers when active particles reach this amount.");

            hardParticleCap = config.Bind(
                damageNumbersSection,
                "Hard Particle Cap",
                192,
                "Uses stronger sampling when active particles reach this amount.");

            absoluteParticleCap = config.Bind(
                damageNumbersSection,
                "Absolute Particle Cap",
                256,
                "Uses the strongest sampling when active particles reach this amount.");

            softSpawnsPerFrame = config.Bind(
                damageNumbersSection,
                "Soft Spawns Per Frame",
                24,
                "Starts lightly sampling regular damage numbers when this many spawns happen in one frame.");

            hardSpawnsPerFrame = config.Bind(
                damageNumbersSection,
                "Hard Spawns Per Frame",
                40,
                "Uses stronger sampling when this many spawns happen in one frame.");

            absoluteSpawnsPerFrame = config.Bind(
                damageNumbersSection,
                "Absolute Spawns Per Frame",
                56,
                "Uses the strongest sampling when this many spawns happen in one frame.");

            softPeakDamageFraction = config.Bind(
                damageNumbersSection,
                "Soft Peak Damage Fraction",
                0.03f,
                "During soft load shedding, regular hits at or above this fraction of the recent peak damage are preserved.");

            hardPeakDamageFraction = config.Bind(
                damageNumbersSection,
                "Hard Peak Damage Fraction",
                0.08f,
                "During hard load shedding, regular hits at or above this fraction of the recent peak damage are preserved.");

            absolutePeakDamageFraction = config.Bind(
                damageNumbersSection,
                "Absolute Peak Damage Fraction",
                0.16f,
                "During the strongest load shedding, regular hits at or above this fraction of the recent peak damage are preserved.");

            softMinimumDamage = config.Bind(
                damageNumbersSection,
                "Soft Minimum Damage",
                1f,
                "During soft load shedding, regular hits at or above this raw damage value are preserved even if recent peak damage is low.");

            hardMinimumDamage = config.Bind(
                damageNumbersSection,
                "Hard Minimum Damage",
                2f,
                "During hard load shedding, regular hits at or above this raw damage value are preserved even if recent peak damage is low.");

            absoluteMinimumDamage = config.Bind(
                damageNumbersSection,
                "Absolute Minimum Damage",
                4f,
                "During the strongest load shedding, regular hits at or above this raw damage value are preserved even if recent peak damage is low.");

            peakDamageHalfLifeSeconds = config.Bind(
                damageNumbersSection,
                "Peak Damage Half Life Seconds",
                1.5f,
                "How quickly the recent peak damage estimate decays back down after large hits stop happening.");

            preserveCriticalHits = config.Bind(
                damageNumbersSection,
                "Preserve Critical Hits",
                true,
                "Always allow critical hit damage numbers through the load shedding filter.");

            preserveImportantColors = config.Bind(
                damageNumbersSection,
                "Preserve Important Colors",
                true,
                "Always allow important color categories like heal and weak point through the load shedding filter.");
        }
    }

    [BepInPlugin(Modguid, ModName, ModVersion)]
    public sealed class RORO : BaseUnityPlugin
    {
        public static ManualLogSource? GlobalLogger { get; private set; }

        private const string Modguid = "net.sixik.plugin.roro";
        private const string ModName = "ROR-O";
        private const string ModVersion = "1.0.0";

        private void Awake()
        {
            GlobalLogger = Logger;
            GlobalLogger.LogInfo($"Initialize ROR-O mod: {ModName} v{ModVersion}");
            ROROConfig.Bind(Config);

            var harmony = new Harmony(Modguid);
            harmony.PatchAll();
            GlobalLogger.LogInfo("Harmony patches applied successfully!");
        }
    }
}
