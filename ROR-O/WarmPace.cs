using System;
using System.IO;
using BepInEx.Configuration;

namespace ROR_O
{
    // One pace for the per-frame caps that used to be separate sliders: monster master
    // registration, effect prefab loads, and effect copy builds. Shader compiles during
    // play follow the same pace. Lazy is the pace the loops already ran. Faster is the
    // high end those sliders allowed, except effect loads, whose slider was not read:
    // the pin loop started one prefab per frame, and Faster raises that to the value
    // the slider's own performance preset stored.
    internal enum WarmPace
    {
        Lazy,
        Faster
    }

    internal static class WarmPaceSettings
    {
        internal const string Section = "General";

        // Masters Per Frame was clamped to 4. Instance Warms Per Frame was clamped to 2.
        // Asset Loads Per Frame was clamped to 8 and defaulted to 4, but nothing read it.
        // A pin is only an asset start now; four starts is the preset that slider stored,
        // and one start can still spend the frame, so Faster does not take the old maximum of 8.
        // Shader compiles had no slider. During play each prefab is compiled on its own,
        // and the burst stops once it has run long, so three is the faster cap.
        private const int FasterMasters = 4;
        private const int FasterLoads = 4;
        private const int FasterWarms = 2;
        private const int FasterShaders = 3;

        private static ConfigEntry<WarmPace>? pace;

        internal static ConfigEntry<WarmPace>? Entry => pace;

        internal static WarmPace Current => pace?.Value ?? WarmPace.Lazy;

        internal static int MastersPerFrame => Current == WarmPace.Faster ? FasterMasters : 1;

        internal static int EffectLoadsPerFrame => Current == WarmPace.Faster ? FasterLoads : 1;

        internal static int EffectWarmsPerFrame => Current == WarmPace.Faster ? FasterWarms : 1;

        internal static int ShaderPrefabsPerFrame => Current == WarmPace.Faster ? FasterShaders : 1;

        internal static void Bind(ConfigFile config)
        {
            pace = config.Bind(
                Section,
                "Warmup Pace",
                Migrated(config.ConfigFilePath),
                OptionText.Build(
                    null,
                    OptionText.Channels(
                        OptionText.Cost("CPU", "Medium", "Faster does more of the same work on one frame, which can stutter."),
                        OptionText.Save("CPU", "N/A", "It finishes sooner. It does not remove the work. The slowest frames can get worse. Not checked twice.")),
                    "A frame that already ran long still waits.",
                    "Lazy prepares one character, starts one effect prefab, builds one effect copy, and draws one effect shader on a frame. Faster does "
                        + FasterMasters + ", " + FasterLoads + ", " + FasterWarms + ", and " + FasterShaders
                        + ". On the loading screen, effect shader draws still run until about 10 ms either way. Takes effect immediately."));
        }

        // Bind keeps Warmup Pace once it is in the file, so this default matters only on the
        // first launch after the sliders are gone. Masters at 3 or 4, or copy warms at 2, were
        // past every preset (those stored at most 2 masters and 1 warm) and come back as Faster.
        // The effect-load slider is ignored: its saved value was never applied. A missing file
        // stays Lazy, which is the pace the loops already ran.
        private static WarmPace Migrated(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return WarmPace.Lazy;
            }

            bool paceSaved = false;
            int masters = 0;
            int warms = 0;
            string section = "";
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length >= 2 && line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }

                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator).Trim();
                string rawValue = line.Substring(separator + 1).Trim();
                if (string.Equals(section, Section, StringComparison.Ordinal) && key == "Warmup Pace")
                {
                    paceSaved = true;
                }
                else if (string.Equals(section, "Spawns", StringComparison.Ordinal) && key == "Masters Per Frame")
                {
                    int.TryParse(rawValue, out masters);
                }
                else if (string.Equals(section, "Effects", StringComparison.Ordinal) && key == "Instance Warms Per Frame")
                {
                    int.TryParse(rawValue, out warms);
                }
            }

            if (paceSaved || (masters <= 2 && warms <= 1))
            {
                return WarmPace.Lazy;
            }

            return WarmPace.Faster;
        }
    }
}
