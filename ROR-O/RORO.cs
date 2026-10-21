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

        private static ConfigEntry<bool>? enableDynamicBoneInvisibleThrottling;
        private static ConfigEntry<int>? dynamicBoneInvisibleUpdateInterval;
        private static ConfigEntry<int>? dynamicBoneRecentlyVisibleGraceFrames;
        private static ConfigEntry<float>? dynamicBoneVisibleMidDistance;
        private static ConfigEntry<float>? dynamicBoneVisibleFarDistance;
        private static ConfigEntry<int>? dynamicBoneVisibleMidUpdateInterval;
        private static ConfigEntry<int>? dynamicBoneVisibleFarUpdateInterval;
        private static ConfigEntry<bool>? enableGenericIkInvisibleThrottling;
        private static ConfigEntry<int>? genericIkInvisibleUpdateInterval;
        private static ConfigEntry<int>? genericIkRecentlyVisibleGraceFrames;

        public static bool EnableDamageNumberLoadShedding => enableDamageNumberLoadShedding?.Value ?? true;
        public static int SoftParticleCap => softParticleCap?.Value ?? 96;
        public static int HardParticleCap => hardParticleCap?.Value ?? 144;
        public static int AbsoluteParticleCap => absoluteParticleCap?.Value ?? 208;

        public static int SoftSpawnsPerFrame => softSpawnsPerFrame?.Value ?? 18;
        public static int HardSpawnsPerFrame => hardSpawnsPerFrame?.Value ?? 32;
        public static int AbsoluteSpawnsPerFrame => absoluteSpawnsPerFrame?.Value ?? 44;

        public static float SoftPeakDamageFraction => softPeakDamageFraction?.Value ?? 0.04f;
        public static float HardPeakDamageFraction => hardPeakDamageFraction?.Value ?? 0.10f;
        public static float AbsolutePeakDamageFraction => absolutePeakDamageFraction?.Value ?? 0.20f;

        public static float SoftMinimumDamage => softMinimumDamage?.Value ?? 1f;
        public static float HardMinimumDamage => hardMinimumDamage?.Value ?? 2f;
        public static float AbsoluteMinimumDamage => absoluteMinimumDamage?.Value ?? 5f;

        public static float PeakDamageHalfLifeSeconds => peakDamageHalfLifeSeconds?.Value ?? 1.75f;

        public static bool PreserveCriticalHits => preserveCriticalHits?.Value ?? true;
        public static bool PreserveImportantColors => preserveImportantColors?.Value ?? true;

        public static bool EnableDynamicBoneInvisibleThrottling => enableDynamicBoneInvisibleThrottling?.Value ?? true;
        public static int DynamicBoneInvisibleUpdateInterval => dynamicBoneInvisibleUpdateInterval?.Value ?? 8;
        public static int DynamicBoneRecentlyVisibleGraceFrames => dynamicBoneRecentlyVisibleGraceFrames?.Value ?? 8;
        public static float DynamicBoneVisibleMidDistance => dynamicBoneVisibleMidDistance?.Value ?? 18f;
        public static float DynamicBoneVisibleFarDistance => dynamicBoneVisibleFarDistance?.Value ?? 40f;
        public static int DynamicBoneVisibleMidUpdateInterval => dynamicBoneVisibleMidUpdateInterval?.Value ?? 2;
        public static int DynamicBoneVisibleFarUpdateInterval => dynamicBoneVisibleFarUpdateInterval?.Value ?? 4;
        public static bool EnableGenericIkInvisibleThrottling => enableGenericIkInvisibleThrottling?.Value ?? true;
        public static int GenericIkInvisibleUpdateInterval => genericIkInvisibleUpdateInterval?.Value ?? 4;
        public static int GenericIkRecentlyVisibleGraceFrames => genericIkRecentlyVisibleGraceFrames?.Value ?? 4;
        public static bool EnableSpawnPrewarm => enableSpawnPrewarm?.Value ?? true;

        private static ConfigEntry<bool>? enableSpawnPrewarm;

        private static ConfigEntry<SettingsProfiles.Profile>? damageNumbersProfile;
        private static ConfigEntry<SettingsProfiles.Profile>? dynamicBonesProfile;
        private static ConfigEntry<SettingsProfiles.Profile>? genericIkProfile;

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
                96,
                "Starts lightly sampling regular damage numbers when active particles reach this amount.");

            hardParticleCap = config.Bind(
                damageNumbersSection,
                "Hard Particle Cap",
                144,
                "Uses stronger sampling when active particles reach this amount.");

            absoluteParticleCap = config.Bind(
                damageNumbersSection,
                "Absolute Particle Cap",
                208,
                "Uses the strongest sampling when active particles reach this amount.");

            softSpawnsPerFrame = config.Bind(
                damageNumbersSection,
                "Soft Spawns Per Frame",
                18,
                "Starts lightly sampling regular damage numbers when this many spawns happen in one frame.");

            hardSpawnsPerFrame = config.Bind(
                damageNumbersSection,
                "Hard Spawns Per Frame",
                32,
                "Uses stronger sampling when this many spawns happen in one frame.");

            absoluteSpawnsPerFrame = config.Bind(
                damageNumbersSection,
                "Absolute Spawns Per Frame",
                44,
                "Uses the strongest sampling when this many spawns happen in one frame.");

            softPeakDamageFraction = config.Bind(
                damageNumbersSection,
                "Soft Peak Damage Fraction",
                0.04f,
                "During soft load shedding, regular hits at or above this fraction of the recent peak damage are preserved.");

            hardPeakDamageFraction = config.Bind(
                damageNumbersSection,
                "Hard Peak Damage Fraction",
                0.10f,
                "During hard load shedding, regular hits at or above this fraction of the recent peak damage are preserved.");

            absolutePeakDamageFraction = config.Bind(
                damageNumbersSection,
                "Absolute Peak Damage Fraction",
                0.20f,
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
                5f,
                "During the strongest load shedding, regular hits at or above this raw damage value are preserved even if recent peak damage is low.");

            peakDamageHalfLifeSeconds = config.Bind(
                damageNumbersSection,
                "Peak Damage Half Life Seconds",
                1.75f,
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

            damageNumbersProfile = SettingsProfiles.Begin(config, damageNumbersSection);
            SettingsProfiles.Add(enableDamageNumberLoadShedding, true, true, true);
            SettingsProfiles.Add(softParticleCap, 256, 96, 32);
            SettingsProfiles.Add(hardParticleCap, 384, 144, 64);
            SettingsProfiles.Add(absoluteParticleCap, 512, 208, 96);
            SettingsProfiles.Add(softSpawnsPerFrame, 48, 18, 8);
            SettingsProfiles.Add(hardSpawnsPerFrame, 72, 32, 14);
            SettingsProfiles.Add(absoluteSpawnsPerFrame, 96, 44, 22);
            SettingsProfiles.Add(softPeakDamageFraction, 0.02f, 0.04f, 0.12f);
            SettingsProfiles.Add(hardPeakDamageFraction, 0.05f, 0.10f, 0.30f);
            SettingsProfiles.Add(absolutePeakDamageFraction, 0.10f, 0.20f, 0.55f);
            SettingsProfiles.Add(softMinimumDamage, 1f, 1f, 4f);
            SettingsProfiles.Add(hardMinimumDamage, 1f, 2f, 12f);
            SettingsProfiles.Add(absoluteMinimumDamage, 2f, 5f, 25f);
            SettingsProfiles.Add(peakDamageHalfLifeSeconds, 1.0f, 1.75f, 2.0f);
            SettingsProfiles.Add(preserveCriticalHits, true, true, false);
            SettingsProfiles.Add(preserveImportantColors, true, true, true);
            SettingsProfiles.End();

            const string dynamicBonesSection = "Dynamic Bones";

            enableDynamicBoneInvisibleThrottling = config.Bind(
                dynamicBonesSection,
                "Enable Invisible Update Throttling",
                true,
                "Throttles DynamicBone LateUpdate calls when the affected renderers are offscreen.");

            dynamicBoneInvisibleUpdateInterval = config.Bind(
                dynamicBonesSection,
                "Invisible Update Interval",
                8,
                "When a DynamicBone is offscreen for a while, only let it fully update once every N frames.");

            dynamicBoneRecentlyVisibleGraceFrames = config.Bind(
                dynamicBonesSection,
                "Recently Visible Grace Frames",
                8,
                "Keeps DynamicBone updating normally for a short time after it leaves the screen to reduce visible popping.");

            dynamicBoneVisibleMidDistance = config.Bind(
                dynamicBonesSection,
                "Visible Mid Distance",
                18f,
                "When a visible DynamicBone rig is at least this far from the camera, it can be updated less often.");

            dynamicBoneVisibleFarDistance = config.Bind(
                dynamicBonesSection,
                "Visible Far Distance",
                40f,
                "When a visible DynamicBone rig is at least this far from the camera, it can be updated even less often.");

            dynamicBoneVisibleMidUpdateInterval = config.Bind(
                dynamicBonesSection,
                "Visible Mid Update Interval",
                2,
                "When a visible DynamicBone rig is beyond the mid distance, only run a full LateUpdate once every N frames.");

            dynamicBoneVisibleFarUpdateInterval = config.Bind(
                dynamicBonesSection,
                "Visible Far Update Interval",
                4,
                "When a visible DynamicBone rig is beyond the far distance, only run a full LateUpdate once every N frames.");

            dynamicBonesProfile = SettingsProfiles.Begin(config, dynamicBonesSection);
            SettingsProfiles.Add(enableDynamicBoneInvisibleThrottling, true, true, true);
            SettingsProfiles.Add(dynamicBoneInvisibleUpdateInterval, 6, 8, 20);
            SettingsProfiles.Add(dynamicBoneRecentlyVisibleGraceFrames, 18, 8, 2);
            SettingsProfiles.Add(dynamicBoneVisibleMidDistance, 80f, 18f, 12f);
            SettingsProfiles.Add(dynamicBoneVisibleFarDistance, 130f, 40f, 22f);
            SettingsProfiles.Add(dynamicBoneVisibleMidUpdateInterval, 1, 2, 3);
            SettingsProfiles.Add(dynamicBoneVisibleFarUpdateInterval, 2, 4, 8);
            SettingsProfiles.End();

            const string genericIkSection = "Generic IK";

            enableGenericIkInvisibleThrottling = config.Bind(
                genericIkSection,
                "Enable Invisible Update Throttling",
                true,
                "Throttles GenericIK LateUpdate calls when the animated character has been offscreen for a short time.");

            genericIkInvisibleUpdateInterval = config.Bind(
                genericIkSection,
                "Invisible Update Interval",
                4,
                "When a GenericIK rig is offscreen, only let it run a full LateUpdate once every N frames.");

            genericIkRecentlyVisibleGraceFrames = config.Bind(
                genericIkSection,
                "Recently Visible Grace Frames",
                4,
                "Keeps GenericIK updating normally for a short time after it leaves the screen to reduce visible snapping.");

            genericIkProfile = SettingsProfiles.Begin(config, genericIkSection);
            SettingsProfiles.Add(enableGenericIkInvisibleThrottling, true, true, true);
            SettingsProfiles.Add(genericIkInvisibleUpdateInterval, 2, 4, 15);
            SettingsProfiles.Add(genericIkRecentlyVisibleGraceFrames, 20, 4, 1);
            SettingsProfiles.End();

            enableSpawnPrewarm = config.Bind(
                "Spawns",
                "Prewarm Stage Spawns",
                true,
                "Preloads the animator, avatar, and skins of monsters this stage can spawn, one monster per frame. Uses the game's own master preload and also includes bosses from that list. Takes effect on the next stage.");

            RiskOfOptionsIntegration.Hold(new RiskOfOptionsCatalog
            {
                DamageNumbersProfile = damageNumbersProfile,
                EnableDamageNumberLoadShedding = enableDamageNumberLoadShedding,
                SoftParticleCap = softParticleCap,
                HardParticleCap = hardParticleCap,
                AbsoluteParticleCap = absoluteParticleCap,
                SoftSpawnsPerFrame = softSpawnsPerFrame,
                HardSpawnsPerFrame = hardSpawnsPerFrame,
                AbsoluteSpawnsPerFrame = absoluteSpawnsPerFrame,
                SoftPeakDamageFraction = softPeakDamageFraction,
                HardPeakDamageFraction = hardPeakDamageFraction,
                AbsolutePeakDamageFraction = absolutePeakDamageFraction,
                SoftMinimumDamage = softMinimumDamage,
                HardMinimumDamage = hardMinimumDamage,
                AbsoluteMinimumDamage = absoluteMinimumDamage,
                PeakDamageHalfLifeSeconds = peakDamageHalfLifeSeconds,
                PreserveCriticalHits = preserveCriticalHits,
                PreserveImportantColors = preserveImportantColors,
                DynamicBonesProfile = dynamicBonesProfile,
                EnableDynamicBoneInvisibleThrottling = enableDynamicBoneInvisibleThrottling,
                DynamicBoneInvisibleUpdateInterval = dynamicBoneInvisibleUpdateInterval,
                DynamicBoneRecentlyVisibleGraceFrames = dynamicBoneRecentlyVisibleGraceFrames,
                DynamicBoneVisibleMidDistance = dynamicBoneVisibleMidDistance,
                DynamicBoneVisibleFarDistance = dynamicBoneVisibleFarDistance,
                DynamicBoneVisibleMidUpdateInterval = dynamicBoneVisibleMidUpdateInterval,
                DynamicBoneVisibleFarUpdateInterval = dynamicBoneVisibleFarUpdateInterval,
                GenericIkProfile = genericIkProfile,
                EnableGenericIkInvisibleThrottling = enableGenericIkInvisibleThrottling,
                GenericIkInvisibleUpdateInterval = genericIkInvisibleUpdateInterval,
                GenericIkRecentlyVisibleGraceFrames = genericIkRecentlyVisibleGraceFrames,
                EnableSpawnPrewarm = enableSpawnPrewarm,
            });
        }
    }

    [BepInPlugin(Modguid, ModName, ModVersion)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class RORO : BaseUnityPlugin
    {
        public static ManualLogSource? GlobalLogger { get; private set; }

        private const string Modguid = "net.sixik.plugin.roro";
        private const string ModName = "ROR-O";
        private const string ModVersion = ModVersionInfo.Value;

        private void Awake()
        {
            GlobalLogger = Logger;
            GlobalLogger.LogInfo($"Initialize ROR-O mod: {ModName} v{ModVersion}");
            ROROConfig.Bind(Config);

            var harmony = new Harmony(Modguid);
            harmony.PatchAll();
            patches.OptimizeSphereSearch.Install(harmony);
            GlobalLogger.LogInfo("Harmony patches applied successfully!");
            patches.SkipUnchangedBuffTimerPatch.Register();
            HitchWatchStartup.Register(this);
            patches.RegisterDynamicBoneJobsPatch.Register(this);
            patches.RegisterSteamP2PPatch.Register(this);
            RiskOfOptionsIntegration.Publish();
            gameObject.AddComponent<SpawnPrewarm>();
        }
    }
}
