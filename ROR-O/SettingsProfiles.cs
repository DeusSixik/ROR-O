using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace ROR_O
{
    internal static class SettingsProfiles
    {
        internal enum Profile
        {
            Lossless,
            Performance,
            Potato,
            Custom
        }

        private static int suppress;
        private static Group? current;
        private static readonly Dictionary<string, Group> finished = new Dictionary<string, Group>();

        private sealed class Group
        {
            public string Section = "";
            public bool Existed;
            public ConfigEntry<Profile> ProfileEntry = null!;
            public readonly List<Option> Options = new List<Option>();
        }

        private sealed class Option
        {
            public ConfigEntryBase Entry = null!;
            public object Lossless = null!;
            public object Performance = null!;
            public object Potato = null!;
            public Action<EventHandler> Subscribe = null!;

            public object For(Profile profile)
            {
                switch (profile)
                {
                    case Profile.Lossless:
                        return Lossless;
                    case Profile.Performance:
                        return Performance;
                    case Profile.Potato:
                        return Potato;
                    default:
                        return Entry.BoxedValue;
                }
            }
        }

        public static ConfigEntry<Profile> Begin(ConfigFile config, string section)
        {
            if (current != null)
            {
                throw new InvalidOperationException("A settings profile group is already being built.");
            }

            bool existed = HasKey(config.ConfigFilePath, section, "Profile");
            current = new Group
            {
                Section = section,
                Existed = existed,
                ProfileEntry = config.Bind(
                    section,
                    "Profile",
                    Profile.Custom,
                    "Preset for this category. Lossless is a gain that should not be visible, Performance spends a little image quality for a larger gain, and Potato pushes the visible settings without getting in the way of playing. Changing any setting yourself selects Custom.")
            };
            return current.ProfileEntry;
        }

        public static void Add(ConfigEntry<bool>? entry, bool lossless, bool performance, bool potato)
        {
            AddCore(entry, lossless, performance, potato);
        }

        public static void Add(ConfigEntry<int>? entry, int lossless, int performance, int potato)
        {
            AddCore(entry, lossless, performance, potato);
        }

        public static void Add(ConfigEntry<float>? entry, float lossless, float performance, float potato)
        {
            AddCore(entry, lossless, performance, potato);
        }

        // A later feature can join a group that has already been closed, without editing the shared setup.
        public static void AddLate(string section, ConfigEntry<bool>? entry, bool lossless, bool performance, bool potato)
        {
            if (entry == null || !finished.TryGetValue(section, out Group group))
            {
                return;
            }

            var option = new Option
            {
                Entry = entry,
                Lossless = lossless,
                Performance = performance,
                Potato = potato,
                Subscribe = handler => entry.SettingChanged += handler
            };
            group.Options.Add(option);
            option.Subscribe((_, __) =>
            {
                if (suppress > 0)
                {
                    return;
                }

                Profile detected = Detect(group);
                if (group.ProfileEntry.Value != detected)
                {
                    AssignProfile(group, detected);
                }
            });

            if (group.ProfileEntry.Value == Profile.Custom)
            {
                return;
            }

            suppress++;
            try
            {
                Assign(option.Entry, option.For(group.ProfileEntry.Value));
            }
            finally
            {
                suppress--;
            }
        }

        public static void End()
        {
            Group group = current ?? throw new InvalidOperationException("No settings profile group is being built.");
            current = null;
            finished[group.Section] = group;
            Sync(group);

            group.ProfileEntry.SettingChanged += (_, __) =>
            {
                if (suppress > 0 || group.ProfileEntry.Value == Profile.Custom)
                {
                    return;
                }

                Apply(group, group.ProfileEntry.Value);
            };

            foreach (Option option in group.Options)
            {
                Option captured = option;
                captured.Subscribe((_, __) =>
                {
                    if (suppress > 0)
                    {
                        return;
                    }

                    Profile detected = Detect(group);
                    if (group.ProfileEntry.Value != detected)
                    {
                        AssignProfile(group, detected);
                    }
                });
            }
        }

        private static void AddCore<T>(ConfigEntry<T>? entry, object lossless, object performance, object potato)
        {
            if (entry == null || current == null)
            {
                return;
            }

            current.Options.Add(new Option
            {
                Entry = entry,
                Lossless = lossless,
                Performance = performance,
                Potato = potato,
                Subscribe = handler => entry.SettingChanged += handler
            });
        }

        private static void Sync(Group group)
        {
            if (!group.Existed || group.ProfileEntry.Value == Profile.Custom)
            {
                AssignProfile(group, Detect(group));
                return;
            }

            Apply(group, group.ProfileEntry.Value);
        }

        private static void Apply(Group group, Profile profile)
        {
            suppress++;
            try
            {
                foreach (Option option in group.Options)
                {
                    Assign(option.Entry, option.For(profile));
                }
            }
            finally
            {
                suppress--;
            }

            RefreshOpenControls();
        }

        private static void AssignProfile(Group group, Profile profile)
        {
            if (group.ProfileEntry.Value == profile)
            {
                return;
            }

            suppress++;
            try
            {
                group.ProfileEntry.Value = profile;
            }
            finally
            {
                suppress--;
            }

            RefreshOpenControls();
        }

        private static Type? modSettingType;
        private static readonly Dictionary<Type, MethodInfo?> UpdateControlsByType = new Dictionary<Type, MethodInfo?>();

        // Risk of Options copies a setting into its slider only when the row is enabled. A preset changes the values
        // while the row stays open, so the controls keep showing the previous values until the category is rebuilt.
        private static void RefreshOpenControls()
        {
            if (modSettingType == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name != "RiskOfOptions")
                    {
                        continue;
                    }

                    modSettingType = assembly.GetType("RiskOfOptions.Components.Options.ModSetting");
                    break;
                }
            }

            if (modSettingType == null)
            {
                return;
            }

            UnityEngine.Object[] controls = UnityEngine.Object.FindObjectsOfType(modSettingType);
            foreach (UnityEngine.Object control in controls)
            {
                Type type = control.GetType();
                if (!UpdateControlsByType.TryGetValue(type, out MethodInfo? updateControls))
                {
                    updateControls = FindUpdateControls(type);
                    UpdateControlsByType.Add(type, updateControls);
                }

                updateControls?.Invoke(control, null);
            }
        }

        private static MethodInfo? FindUpdateControls(Type type)
        {
            while (type != null)
            {
                MethodInfo? method = type.GetMethod(
                    "UpdateControls",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null)
                {
                    return method;
                }

                type = type.BaseType;
            }

            return null;
        }

        private static void Assign(ConfigEntryBase entry, object value)
        {
            if (ValuesEqual(entry.BoxedValue, value))
            {
                return;
            }

            entry.BoxedValue = value;
        }

        private static Profile Detect(Group group)
        {
            if (Matches(group, Profile.Lossless))
            {
                return Profile.Lossless;
            }

            if (Matches(group, Profile.Performance))
            {
                return Profile.Performance;
            }

            if (Matches(group, Profile.Potato))
            {
                return Profile.Potato;
            }

            return Profile.Custom;
        }

        private static bool Matches(Group group, Profile profile)
        {
            foreach (Option option in group.Options)
            {
                if (!ValuesEqual(option.Entry.BoxedValue, option.For(profile)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValuesEqual(object? current, object expected)
        {
            if (current is float currentFloat && expected is float expectedFloat)
            {
                return Math.Abs(currentFloat - expectedFloat) <= 0.0001f;
            }

            return Equals(current, expected);
        }

        private static bool HasKey(string? path, string section, string key)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return false;
            }

            bool inSection = false;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length >= 2 && line[0] == '[' && line[line.Length - 1] == ']')
                {
                    inSection = string.Equals(line.Substring(1, line.Length - 2), section, StringComparison.Ordinal);
                    continue;
                }

                if (!inSection || line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator > 0 && string.Equals(line.Substring(0, separator).Trim(), key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
