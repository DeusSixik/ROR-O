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

        private static ConfigEntry<SettingsProfiles.Profile>? damageNumbersProfile;
        private static ConfigEntry<SettingsProfiles.Profile>? dynamicBonesProfile;
        private static ConfigEntry<SettingsProfiles.Profile>? genericIkProfile;

        public static void Bind(ConfigFile config)
        {
            WarmPaceSettings.Bind(config);

            const string damageNumbersSection = "Damage Numbers";

            enableDamageNumberLoadShedding = config.Bind(
                damageNumbersSection,
                "Enable Load Shedding",
                true,
                OptionCopy.DamageShedding);

            softParticleCap = config.Bind(
                damageNumbersSection,
                "Soft Particle Cap",
                96,
                OptionCopy.SoftParticleCap);

            hardParticleCap = config.Bind(
                damageNumbersSection,
                "Hard Particle Cap",
                144,
                OptionCopy.HardParticleCap);

            absoluteParticleCap = config.Bind(
                damageNumbersSection,
                "Absolute Particle Cap",
                208,
                OptionCopy.AbsoluteParticleCap);

            softSpawnsPerFrame = config.Bind(
                damageNumbersSection,
                "Soft Spawns Per Frame",
                18,
                OptionCopy.SoftSpawnsPerFrame);

            hardSpawnsPerFrame = config.Bind(
                damageNumbersSection,
                "Hard Spawns Per Frame",
                32,
                OptionCopy.HardSpawnsPerFrame);

            absoluteSpawnsPerFrame = config.Bind(
                damageNumbersSection,
                "Absolute Spawns Per Frame",
                44,
                OptionCopy.AbsoluteSpawnsPerFrame);

            softPeakDamageFraction = config.Bind(
                damageNumbersSection,
                "Soft Peak Damage Fraction",
                0.04f,
                OptionCopy.SoftPeakFraction);

            hardPeakDamageFraction = config.Bind(
                damageNumbersSection,
                "Hard Peak Damage Fraction",
                0.10f,
                OptionCopy.HardPeakFraction);

            absolutePeakDamageFraction = config.Bind(
                damageNumbersSection,
                "Absolute Peak Damage Fraction",
                0.20f,
                OptionCopy.AbsolutePeakFraction);

            softMinimumDamage = config.Bind(
                damageNumbersSection,
                "Soft Minimum Damage",
                1f,
                OptionCopy.SoftMinimumDamage);

            hardMinimumDamage = config.Bind(
                damageNumbersSection,
                "Hard Minimum Damage",
                2f,
                OptionCopy.HardMinimumDamage);

            absoluteMinimumDamage = config.Bind(
                damageNumbersSection,
                "Absolute Minimum Damage",
                5f,
                OptionCopy.AbsoluteMinimumDamage);

            peakDamageHalfLifeSeconds = config.Bind(
                damageNumbersSection,
                "Peak Damage Half Life Seconds",
                1.75f,
                OptionCopy.PeakHalfLife);

            preserveCriticalHits = config.Bind(
                damageNumbersSection,
                "Preserve Critical Hits",
                true,
                OptionCopy.PreserveCrits);

            preserveImportantColors = config.Bind(
                damageNumbersSection,
                "Preserve Important Colors",
                true,
                OptionCopy.PreserveColors);

            damageNumbersProfile = SettingsProfiles.Begin(config, damageNumbersSection, OptionCopy.DamageProfile);
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
                OptionCopy.BoneThrottle);

            dynamicBoneInvisibleUpdateInterval = config.Bind(
                dynamicBonesSection,
                "Invisible Update Interval",
                8,
                OptionCopy.BoneInvisibleInterval);

            dynamicBoneRecentlyVisibleGraceFrames = config.Bind(
                dynamicBonesSection,
                "Recently Visible Grace Frames",
                8,
                OptionCopy.BoneGrace);

            dynamicBoneVisibleMidDistance = config.Bind(
                dynamicBonesSection,
                "Visible Mid Distance",
                18f,
                OptionCopy.BoneMidDistance);

            dynamicBoneVisibleFarDistance = config.Bind(
                dynamicBonesSection,
                "Visible Far Distance",
                40f,
                OptionCopy.BoneFarDistance);

            dynamicBoneVisibleMidUpdateInterval = config.Bind(
                dynamicBonesSection,
                "Visible Mid Update Interval",
                2,
                OptionCopy.BoneMidInterval);

            dynamicBoneVisibleFarUpdateInterval = config.Bind(
                dynamicBonesSection,
                "Visible Far Update Interval",
                4,
                OptionCopy.BoneFarInterval);

            dynamicBonesProfile = SettingsProfiles.Begin(config, dynamicBonesSection, OptionCopy.BoneProfile);
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
                OptionCopy.IkThrottle);

            genericIkInvisibleUpdateInterval = config.Bind(
                genericIkSection,
                "Invisible Update Interval",
                4,
                OptionCopy.IkInterval);

            genericIkRecentlyVisibleGraceFrames = config.Bind(
                genericIkSection,
                "Recently Visible Grace Frames",
                4,
                OptionCopy.IkGrace);

            genericIkProfile = SettingsProfiles.Begin(config, genericIkSection, OptionCopy.IkProfile);
            SettingsProfiles.Add(enableGenericIkInvisibleThrottling, true, true, true);
            SettingsProfiles.Add(genericIkInvisibleUpdateInterval, 2, 4, 15);
            SettingsProfiles.Add(genericIkRecentlyVisibleGraceFrames, 20, 4, 1);
            SettingsProfiles.End();

            SpawnPrewarm.Bind(config);
            EffectRuntime.Bind(config);

            RiskOfOptionsIntegration.Hold(new RiskOfOptionsCatalog
            {
                WarmupPace = WarmPaceSettings.Entry,
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
                SpawnsProfile = SpawnPrewarm.Profile,
                EnableSpawnPrewarm = SpawnPrewarm.EnableEntry,
                WarmStageBosses = SpawnPrewarm.WarmBossesEntry,
                SkipLongSpawnFrames = SpawnPrewarm.SkipLongFramesEntry,
                ShowMobStatus = SpawnPrewarm.ShowStatusEntry,
                LogMobStatus = SpawnPrewarm.LogStatusEntry,
                LateSpawnImmunity = SpawnPrewarm.LateSpawnImmunityEntry,
                EffectsProfile = EffectRuntime.Profile,
                EnableEffectWarmup = EffectRuntime.WarmEffects,
                KeepEffectPools = EffectRuntime.KeepPoolsEntry,
                KeepEffectCopiesActive = EffectRuntime.KeepCopiesActiveEntry,
                ActiveParkLimit = EffectRuntime.ActiveParkLimitEntry,
                WarmFullPeak = EffectRuntime.WarmFullPeakEntry,
                WarmShaders = EffectRuntime.WarmShadersEntry,
                WarmPeakCopies = EffectRuntime.WarmPeakCopiesEntry,
                // WarmAllEffectCopies = EffectRuntime.WarmAllCopiesEntry,
                CullSecondaryEffects = EffectRuntime.CullSecondary,
                EffectCullDistance = EffectRuntime.CullDistanceEntry,
                CullEffectsOffScreen = EffectRuntime.CullOffScreenEntry,
                EffectCullInterval = EffectRuntime.CullIntervalEntry,
                ShowEffectStatus = EffectRuntime.ShowStatusEntry,
                ShowShaderStatus = EffectRuntime.ShowShaderStatusEntry,
                LogEffectStatus = EffectRuntime.LogStatusEntry,
            });
        }
    }

    [BepInPlugin(Modguid, ModName, ModVersion)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("droppod.lookingglass", BepInDependency.DependencyFlags.SoftDependency)]
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
            patches.mods.OptimizeLookingGlassPatch.Register(harmony);
            patches.OptimizeSphereSearch.Install(harmony);
            EffectPools.Install(harmony);
            GlobalLogger.LogInfo("Harmony patches applied successfully!");
            patches.SkipUnchangedBuffTimerPatch.Register();
            HitchWatchStartup.Register(this);
            patches.RegisterDynamicBoneJobsPatch.Register(this);
            patches.RegisterSteamP2PPatch.Register(this);
            RiskOfOptionsIntegration.Publish();
            gameObject.AddComponent<SpawnPrewarm>();
            gameObject.AddComponent<EffectRuntime>();
            gameObject.AddComponent<LoadingProgressView>();
        }
    }
}
