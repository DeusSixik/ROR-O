using System;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Events;

namespace ROR_O
{
    internal sealed class RiskOfOptionsCatalog
    {
        public ConfigEntry<SettingsProfiles.Profile>? DamageNumbersProfile { get; set; }
        public ConfigEntry<bool>? EnableDamageNumberLoadShedding { get; set; }
        public ConfigEntry<int>? SoftParticleCap { get; set; }
        public ConfigEntry<int>? HardParticleCap { get; set; }
        public ConfigEntry<int>? AbsoluteParticleCap { get; set; }
        public ConfigEntry<int>? SoftSpawnsPerFrame { get; set; }
        public ConfigEntry<int>? HardSpawnsPerFrame { get; set; }
        public ConfigEntry<int>? AbsoluteSpawnsPerFrame { get; set; }
        public ConfigEntry<float>? SoftPeakDamageFraction { get; set; }
        public ConfigEntry<float>? HardPeakDamageFraction { get; set; }
        public ConfigEntry<float>? AbsolutePeakDamageFraction { get; set; }
        public ConfigEntry<float>? SoftMinimumDamage { get; set; }
        public ConfigEntry<float>? HardMinimumDamage { get; set; }
        public ConfigEntry<float>? AbsoluteMinimumDamage { get; set; }
        public ConfigEntry<float>? PeakDamageHalfLifeSeconds { get; set; }
        public ConfigEntry<bool>? PreserveCriticalHits { get; set; }
        public ConfigEntry<bool>? PreserveImportantColors { get; set; }
        public ConfigEntry<SettingsProfiles.Profile>? DynamicBonesProfile { get; set; }
        public ConfigEntry<bool>? EnableDynamicBoneInvisibleThrottling { get; set; }
        public ConfigEntry<int>? DynamicBoneInvisibleUpdateInterval { get; set; }
        public ConfigEntry<int>? DynamicBoneRecentlyVisibleGraceFrames { get; set; }
        public ConfigEntry<float>? DynamicBoneVisibleMidDistance { get; set; }
        public ConfigEntry<float>? DynamicBoneVisibleFarDistance { get; set; }
        public ConfigEntry<int>? DynamicBoneVisibleMidUpdateInterval { get; set; }
        public ConfigEntry<int>? DynamicBoneVisibleFarUpdateInterval { get; set; }
        public ConfigEntry<bool>? EnableDynamicBoneJobs { get; set; }
        public ConfigEntry<SettingsProfiles.Profile>? GenericIkProfile { get; set; }
        public ConfigEntry<bool>? EnableGenericIkInvisibleThrottling { get; set; }
        public ConfigEntry<int>? GenericIkInvisibleUpdateInterval { get; set; }
        public ConfigEntry<int>? GenericIkRecentlyVisibleGraceFrames { get; set; }
        public ConfigEntry<bool>? EnableThreadedSteamP2P { get; set; }
        public ConfigEntry<bool>? EnableNetworkDebug { get; set; }
        public ConfigEntry<bool>? EnableSpawnPrewarm { get; set; }
        public ConfigEntry<SettingsProfiles.Profile>? EffectsProfile { get; set; }
        public ConfigEntry<bool>? EnableEffectWarmup { get; set; }
        public ConfigEntry<bool>? KeepEffectPools { get; set; }
        public ConfigEntry<int>? WarmInstances { get; set; }
        public ConfigEntry<int>? WarmPrefabLimit { get; set; }
        public ConfigEntry<int>? EffectLoadsPerFrame { get; set; }
        public ConfigEntry<int>? EffectWarmsPerFrame { get; set; }
        public ConfigEntry<bool>? CullSecondaryEffects { get; set; }
        public ConfigEntry<float>? EffectCullDistance { get; set; }
        public ConfigEntry<bool>? CullEffectsOffScreen { get; set; }
        public ConfigEntry<int>? EffectCullInterval { get; set; }
        public ConfigEntry<bool>? ShowEffectStatus { get; set; }
        public ConfigEntry<bool>? EnableHitchWatch { get; set; }
        public ConfigEntry<int>? HitchWatchTargetFps { get; set; }
        public ConfigEntry<bool>? EnableMonoProfiler { get; set; }
    }

    internal static class RiskOfOptionsIntegration
    {
        private const string PluginGuid = "com.rune580.riskofoptions";
        private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy;

        private static readonly RiskOfOptionsCatalog Pending = new RiskOfOptionsCatalog();
        private static bool published;

        public static void Hold(RiskOfOptionsCatalog catalog)
        {
            Absorb(catalog);
        }

        public static void Publish()
        {
            if (published)
            {
                return;
            }

            published = true;
            Write(Pending);
        }

        private static void Absorb(RiskOfOptionsCatalog catalog)
        {
            foreach (PropertyInfo property in typeof(RiskOfOptionsCatalog).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                object? value = property.GetValue(catalog);
                if (value != null)
                {
                    property.SetValue(Pending, value);
                }
            }
        }

        private static void Write(RiskOfOptionsCatalog catalog)
        {
            if (!Chainloader.PluginInfos.ContainsKey(PluginGuid))
            {
                RORO.GlobalLogger?.LogInfo("Risk of Options is not loaded. Settings stay in the BepInEx config file.");
                return;
            }

            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => candidate.GetName().Name == "RiskOfOptions");
            if (assembly == null)
            {
                RORO.GlobalLogger?.LogWarning("Risk of Options is listed as loaded, but its assembly was not found.");
                return;
            }

            try
            {
                var menu = new Menu(assembly);
#if DEBUG
                menu.SetDescription("Performance options for effects, damage numbers, Dynamic Bones, Generic IK, and Steam networking. Those apply during play. Hitch Watch applies immediately. The Mono Profiler switch applies on the next launch.");
#else
                menu.SetDescription("Performance options for effects, damage numbers, Dynamic Bones, Generic IK, and Steam networking. Those apply during play. Hitch Watch applies immediately.");
#endif
                menu.SetIcon(LoadIcon());
                AddEntries(menu, catalog);
                RORO.GlobalLogger?.LogInfo("ROR-O settings were added to Risk of Options.");
            }
            catch (Exception exception)
            {
                RORO.GlobalLogger?.LogError($"Failed to add ROR-O settings to Risk of Options: {exception}");
            }
        }

        private static void AddEntries(Menu menu, RiskOfOptionsCatalog catalog)
        {
            menu.Choice(catalog.DamageNumbersProfile);
            Func<bool> damageNumbersDisabled = () => catalog.EnableDamageNumberLoadShedding?.Value != true;
            menu.CheckBox(catalog.EnableDamageNumberLoadShedding);
            menu.IntSlider(catalog.SoftParticleCap, 1, 800, damageNumbersDisabled);
            menu.IntSlider(catalog.HardParticleCap, 1, 800, damageNumbersDisabled);
            menu.IntSlider(catalog.AbsoluteParticleCap, 1, 800, damageNumbersDisabled);
            menu.IntSlider(catalog.SoftSpawnsPerFrame, 1, 200, damageNumbersDisabled);
            menu.IntSlider(catalog.HardSpawnsPerFrame, 1, 200, damageNumbersDisabled);
            menu.IntSlider(catalog.AbsoluteSpawnsPerFrame, 1, 200, damageNumbersDisabled);
            menu.StepSlider(catalog.SoftPeakDamageFraction, 0f, 1f, 0.01f, "0.00", damageNumbersDisabled);
            menu.StepSlider(catalog.HardPeakDamageFraction, 0f, 1f, 0.01f, "0.00", damageNumbersDisabled);
            menu.StepSlider(catalog.AbsolutePeakDamageFraction, 0f, 1f, 0.01f, "0.00", damageNumbersDisabled);
            menu.StepSlider(catalog.SoftMinimumDamage, 0f, 100f, 0.5f, "0.0", damageNumbersDisabled);
            menu.StepSlider(catalog.HardMinimumDamage, 0f, 100f, 0.5f, "0.0", damageNumbersDisabled);
            menu.StepSlider(catalog.AbsoluteMinimumDamage, 0f, 100f, 0.5f, "0.0", damageNumbersDisabled);
            menu.StepSlider(catalog.PeakDamageHalfLifeSeconds, 0.05f, 10f, 0.05f, "0.00", damageNumbersDisabled);
            menu.CheckBox(catalog.PreserveCriticalHits, damageNumbersDisabled);
            menu.CheckBox(catalog.PreserveImportantColors, damageNumbersDisabled);

            menu.Choice(catalog.DynamicBonesProfile);
            Func<bool> dynamicBonesDisabled = () => catalog.EnableDynamicBoneInvisibleThrottling?.Value != true;
            menu.CheckBox(catalog.EnableDynamicBoneInvisibleThrottling);
            menu.IntSlider(catalog.DynamicBoneInvisibleUpdateInterval, 2, 30, dynamicBonesDisabled);
            menu.IntSlider(catalog.DynamicBoneRecentlyVisibleGraceFrames, 0, 120, dynamicBonesDisabled);
            menu.StepSlider(catalog.DynamicBoneVisibleMidDistance, 0f, 200f, 1f, "0", dynamicBonesDisabled);
            menu.StepSlider(catalog.DynamicBoneVisibleFarDistance, 0f, 400f, 1f, "0", dynamicBonesDisabled);
            menu.IntSlider(catalog.DynamicBoneVisibleMidUpdateInterval, 1, 30, dynamicBonesDisabled);
            menu.IntSlider(catalog.DynamicBoneVisibleFarUpdateInterval, 1, 30, dynamicBonesDisabled);
            menu.CheckBox(catalog.EnableDynamicBoneJobs);

            menu.Choice(catalog.GenericIkProfile);
            Func<bool> genericIkDisabled = () => catalog.EnableGenericIkInvisibleThrottling?.Value != true;
            menu.CheckBox(catalog.EnableGenericIkInvisibleThrottling);
            menu.IntSlider(catalog.GenericIkInvisibleUpdateInterval, 2, 30, genericIkDisabled);
            menu.IntSlider(catalog.GenericIkRecentlyVisibleGraceFrames, 0, 120, genericIkDisabled);
            menu.CheckBox(catalog.EnableThreadedSteamP2P);
            menu.CheckBox(catalog.EnableNetworkDebug);
            menu.CheckBox(catalog.EnableSpawnPrewarm);

            menu.Choice(catalog.EffectsProfile);
            Func<bool> effectWarmupOff = () => catalog.EnableEffectWarmup?.Value != true;
            Func<bool> effectCullOff = () => catalog.CullSecondaryEffects?.Value != true;
            menu.CheckBox(catalog.EnableEffectWarmup);
            menu.CheckBox(catalog.KeepEffectPools);
            menu.IntSlider(catalog.WarmInstances, 0, 4, effectWarmupOff);
            menu.IntSlider(catalog.WarmPrefabLimit, 0, 2000, effectWarmupOff);
            menu.IntSlider(catalog.EffectLoadsPerFrame, 1, 8, effectWarmupOff);
            menu.IntSlider(catalog.EffectWarmsPerFrame, 1, 2, effectWarmupOff);
            menu.CheckBox(catalog.CullSecondaryEffects);
            menu.StepSlider(catalog.EffectCullDistance, 10f, 200f, 5f, "0", effectCullOff);
            menu.CheckBox(catalog.CullEffectsOffScreen, effectCullOff);
            menu.IntSlider(catalog.EffectCullInterval, 1, 20, effectCullOff);
            menu.CheckBox(catalog.ShowEffectStatus);
            menu.Button("Effects", "Log Effect Status", "Writes the current effect catalog, pool, and cull counts to the BepInEx log.", "Log", EffectRuntime.LogStatus);

            menu.CheckBox(catalog.EnableHitchWatch);
            menu.IntSlider(catalog.HitchWatchTargetFps, 30, 240, () => catalog.EnableHitchWatch?.Value != true);
#if DEBUG
            menu.CheckBox(catalog.EnableMonoProfiler);
#else
            menu.CheckBox(catalog.EnableMonoProfiler, () => true);
#endif
        }

        private static Sprite? LoadIcon()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ROR_O.icon.png");
            if (stream == null)
            {
                return null;
            }

            var bytes = new byte[stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes))
            {
                return null;
            }

            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        private sealed class Menu
        {
            private readonly Assembly assembly;
            private readonly MethodInfo addOption;
            private readonly MethodInfo setDescription;
            private readonly MethodInfo setIcon;
            private readonly Type disabledDelegateType;

            public Menu(Assembly riskOfOptionsAssembly)
            {
                assembly = riskOfOptionsAssembly;
                Type manager = RequireType("RiskOfOptions.ModSettingsManager");
                addOption = RequireMethod(manager, "AddOption", 1);
                setDescription = RequireMethod(manager, "SetModDescription", 1);
                setIcon = RequireMethod(manager, "SetModIcon", 1);
                disabledDelegateType = RequireType("RiskOfOptions.OptionConfigs.BaseOptionConfig")
                    .GetNestedType("IsDisabledDelegate", BindingFlags.Public)
                    ?? throw new MissingMemberException("Risk of Options is missing IsDisabledDelegate.");
            }

            public void SetDescription(string description)
            {
                setDescription.Invoke(null, new object[] { description });
            }

            public void SetIcon(Sprite? icon)
            {
                if (icon != null)
                {
                    setIcon.Invoke(null, new object[] { icon });
                }
            }

            public void Choice(ConfigEntryBase? entry)
            {
                if (entry == null)
                {
                    return;
                }

                object config = CreateConfig("RiskOfOptions.OptionConfigs.ChoiceConfig", entry, null);
                Set(config, "name", entry.Definition.Key);
                Set(config, "description", entry.Description.Description);
                Add("RiskOfOptions.Options.ChoiceOption", entry, config);
            }

            public void CheckBox(ConfigEntry<bool>? entry, Func<bool>? disabledWhen = null)
            {
                if (entry == null)
                {
                    return;
                }

                object? config = disabledWhen == null ? null : CreateConfig("RiskOfOptions.OptionConfigs.CheckBoxConfig", entry, disabledWhen);
                Add("RiskOfOptions.Options.CheckBoxOption", entry, config);
            }

            public void IntSlider(ConfigEntry<int>? entry, int min, int max, Func<bool>? disabledWhen = null)
            {
                if (entry == null)
                {
                    return;
                }

                object config = CreateConfig("RiskOfOptions.OptionConfigs.IntSliderConfig", entry, disabledWhen);
                Set(config, "min", min);
                Set(config, "max", max);
                Add("RiskOfOptions.Options.IntSliderOption", entry, config);
            }

            public void Button(string category, string name, string description, string buttonText, UnityAction onPressed)
            {
                Type optionType = RequireType("RiskOfOptions.Options.GenericButtonOption");
                object option = Activator.CreateInstance(optionType, name, category, description, buttonText, onPressed)
                    ?? throw new MissingMethodException(optionType.FullName);
                addOption.Invoke(null, new[] { option });
            }

            public void StepSlider(ConfigEntry<float>? entry, float min, float max, float increment, string format, Func<bool>? disabledWhen = null)
            {
                if (entry == null)
                {
                    return;
                }

                object config = CreateConfig("RiskOfOptions.OptionConfigs.StepSliderConfig", entry, disabledWhen);
                Set(config, "min", min);
                Set(config, "max", max);
                Set(config, "increment", increment);
                Set(config, "formatString", format);
                config.GetType().GetProperty("FormatString", FieldFlags)?.SetValue(config, format);
                Add("RiskOfOptions.Options.StepSliderOption", entry, config);
            }

            private void Add(string optionTypeName, ConfigEntryBase entry, object? config)
            {
                Type optionType = RequireType(optionTypeName);
                object option = config == null
                    ? Activator.CreateInstance(optionType, entry) ?? throw new MissingMethodException(optionType.FullName)
                    : Activator.CreateInstance(optionType, entry, config) ?? throw new MissingMethodException(optionType.FullName);
                addOption.Invoke(null, new[] { option });
            }

            private object CreateConfig(string configTypeName, ConfigEntryBase entry, Func<bool>? disabledWhen)
            {
                object config = Activator.CreateInstance(RequireType(configTypeName))
                    ?? throw new MissingMethodException(configTypeName);
                Set(config, "category", entry.Definition.Section);
                if (disabledWhen != null)
                {
                    Delegate callback = Delegate.CreateDelegate(disabledDelegateType, disabledWhen.Target, disabledWhen.Method);
                    RequireField(config.GetType(), "checkIfDisabled").SetValue(config, callback);
                }

                return config;
            }

            private Type RequireType(string fullName)
            {
                return assembly.GetType(fullName) ?? throw new MissingMemberException(fullName);
            }

            private static MethodInfo RequireMethod(Type type, string name, int parameterCount)
            {
                MethodInfo? method = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == parameterCount);
                return method ?? throw new MissingMemberException(type.FullName + "." + name);
            }

            private static void Set(object target, string fieldName, object value)
            {
                RequireField(target.GetType(), fieldName).SetValue(target, value);
            }

            private static FieldInfo RequireField(Type type, string fieldName)
            {
                return type.GetField(fieldName, FieldFlags) ?? throw new MissingMemberException(type.FullName + "." + fieldName);
            }
        }
    }
}
