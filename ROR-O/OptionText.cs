using System.Text;

namespace ROR_O
{
    // Risk of Options draws the description with rich text, so a newline is a new block
    // and a color tag is a plaque. The rule is ASCII so a missing glyph cannot eat it.
    internal static class OptionText
    {
        internal const string Experimental = "#FFDD44";
        internal const string Gameplay = "#F0A35A";
        internal const string Readout = "#7EB6FF";
        internal const string Restart = "#FF7A7A";

        private const string Rule = "<color=#66788A>--------</color>";
        private const string RiskColor = "#FF6B6B";

        internal static string Badge(string label, string color)
        {
            return "<color=" + color + ">[" + label + "]</color>";
        }

        internal static string Cpu(string text)
        {
            return "<color=#9EC9E0>CPU</color>  " + text;
        }

        internal static string Gpu(string text)
        {
            return "<color=#B7D99A>GPU</color>  " + text;
        }

        // cost spends frame time. save gives it back. N/A does not apply, N/D was not measured,
        // TBD has no reading yet, TBC has a reading that was not the same scene twice.
        internal static string Cost(string channel, string level, string? note = null)
        {
            return Side(channel, "cost", level, note);
        }

        internal static string Save(string channel, string level, string? note = null)
        {
            return Side(channel, "save", level, note);
        }

        private static string Side(string channel, string side, string level, string? note)
        {
            string channelColor = channel == "GPU" ? "#B7D99A" : "#9EC9E0";
            bool unknown = level == "N/A" || level == "N/D" || level == "TBD" || level == "TBC";
            string levelColor = unknown
                ? "#9AA4AD"
                : side == "save"
                    ? (level == "High" ? "#7DDEA0" : level == "Medium" ? "#C6E07A" : "#8FBF8F")
                    : (level == "High" ? "#E07A5F" : level == "Medium" ? "#E0C36A" : "#C4A882");
            string line = "<color=" + channelColor + ">" + channel + " " + side + "</color>  <color=" + levelColor + ">" + level + "</color>";
            if (!string.IsNullOrEmpty(note))
            {
                line += "  " + note;
            }

            return line;
        }

        internal static string Channels(params string[] lines)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i]))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(lines[i]);
            }

            return builder.ToString();
        }

        internal static string Build(string? badge, string? load, string? risk, string body)
        {
            var builder = new StringBuilder();
            Append(builder, badge);
            Append(builder, load);
            if (!string.IsNullOrEmpty(risk))
            {
                Append(builder, "<color=" + RiskColor + ">" + risk + "</color>");
            }

            Append(builder, body);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string? block)
        {
            if (string.IsNullOrEmpty(block))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n').Append(Rule).Append('\n');
            }

            builder.Append(block);
        }
    }

    internal static class OptionCopy
    {
        internal static string DamageProfile => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low"),
                OptionText.Save("CPU", "Medium", "average, only while a cap is crossed. ms N/D. 1% N/D."),
                OptionText.Save("GPU", "N/D", "fewer numbers drawn, size unknown.")),
            "Ordinary hits disappear. Soft keeps 1 of 2, hard 1 of 4, absolute 1 of 8.",
            "Lossless keeps more numbers. Potato starts dropping them earlier.");

        internal static string BoneProfile => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low"),
                OptionText.Save("CPU", "High", "average, busy stage. About 0.5-1 ms/frame before this block, about 0.1 ms/frame left on the main thread with jobs. Same-scene delta TBC. 1% N/D.")),
            "A bone with no renderer, and one marked never-optimize, is not throttled.",
            "Lossless keeps the motion smoother. Potato updates distant and offscreen bones less often.");

        internal static string IkProfile => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low"),
                OptionText.Save("CPU", "Medium", "average, offscreen rigs only. Rigs were about 0.1-0.8 ms/frame while updating. The offscreen share is N/D. 1% N/D.")),
            "A rig with no renderer is not throttled.",
            "Lossless stays close to every frame. Potato leaves an offscreen rig alone for longer.");

        internal static string SpawnProfile => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low", "a hitch while preloading, not a standing cost."),
                OptionText.Save("CPU", "N/A", "average. The work moves earlier, it is not removed. 1% at the real spawn TBC.")),
            null,
            "Lossless waits out a frame that already ran long. Performance and Potato do not.");

        internal static string EffectProfile => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low", "average, about 0.03-0.08 ms a call. On hitch frames where spawn was already over 0.5 ms, about 1.2 ms median, and one call has reached tens of ms. That sample is hitch rows, not the 1% of every frame. 1% N/D."),
                OptionText.Save("CPU", "Low", "average. The frames it actually helps are the ones where several effects, or a cold copy, land together. 1% N/D."),
                OptionText.Save("GPU", "TBD", "native particles were rarely the long part of those frames.")),
            "After 45 s idle the game destroys that pool, one pool per check. The next spawn pays Instantiate again. Scene unload of every pool was about 110 ms once, not per effect.",
            "Lossless culls less. Potato culls closer and more often, and builds a few more copies ahead.");

        internal static string Profile => OptionText.Build(
            null,
            null,
            "Choosing a preset overwrites the settings in this category. General is not part of a preset.",
            "Lossless should not be visible. Performance spends a little image quality. Potato pushes the visible settings. Changing a setting yourself selects Custom.");

        internal static string DamageShedding => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low"),
                OptionText.Save("CPU", "Medium", "average, while a cap is crossed. ms N/D. 1% N/D."),
                OptionText.Save("GPU", "N/D")),
            "Ordinary hits disappear. Soft keeps 1 of 2, hard 1 of 4, absolute 1 of 8.",
            "Runs on each damage-number spawn. Crits pass while Preserve Critical Hits is on. Heal, weak point, sniper, luminous, and knockback colors pass while Preserve Important Colors is on.");

        internal static string SoftParticleCap => Cap("Live damage-number particles at or above this count enter the soft sample. A later cap set under this one is raised to match.");
        internal static string HardParticleCap => Cap("Live particles at or above this count enter the hard sample.");
        internal static string AbsoluteParticleCap => Cap("Live particles at or above this count enter the strongest sample.");
        internal static string SoftSpawnsPerFrame => Cap("This many damage-number spawns in one frame enter the soft sample, even if the particle cap has not been reached.");
        internal static string HardSpawnsPerFrame => Cap("This many spawns in one frame enter the hard sample.");
        internal static string AbsoluteSpawnsPerFrame => Cap("This many spawns in one frame enter the strongest sample.");

        internal static string SoftPeakFraction => Tune("During the soft sample, a regular hit at or above this fraction of the recent peak is kept.");
        internal static string HardPeakFraction => Tune("During the hard sample, a regular hit at or above this fraction of the recent peak is kept. Raised if it sits under the soft fraction.");
        internal static string AbsolutePeakFraction => Tune("During the strongest sample, a regular hit at or above this fraction of the recent peak is kept. Raised if it sits under the hard fraction.");
        internal static string SoftMinimumDamage => Tune("During the soft sample, a regular hit at or above this raw damage is kept even when the recent peak is low.");
        internal static string HardMinimumDamage => Tune("During the hard sample, a regular hit at or above this raw damage is kept. Raised if it sits under the soft minimum.");
        internal static string AbsoluteMinimumDamage => Tune("During the strongest sample, a regular hit at or above this raw damage is kept. Raised if it sits under the hard minimum.");
        internal static string PeakHalfLife => Tune("After big hits stop, the remembered peak falls by half over this many seconds. Longer keeps more ordinary numbers.");
        internal static string PreserveCrits => OptionText.Build(null, TuneLoad, "Off, a crit is an ordinary number.", "On, a critical hit skips the sample.");
        internal static string PreserveColors => OptionText.Build(null, TuneLoad, null, "On, heal, crit heal, weak point, sniper, luminous, and knockback colors skip the sample.");

        internal static string BoneThrottle => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low"),
                OptionText.Save("CPU", "Medium", "average, offscreen and far bones. ms N/D. 1% N/D.")),
            "Visible bones inside the mid distance still update every frame.",
            "Off screen, past the grace, LateUpdate runs every N frames. Past the mid or far distance, that distance's own interval.");

        internal static string BoneInvisibleInterval => BoneStep("Offscreen, past the grace, LateUpdate runs once every N frames. The gap cannot go under 2.");
        internal static string BoneGrace => OptionText.Build(null, OptionText.Channels(OptionText.Cost("CPU", "Low", "full-rate updates during the grace."), OptionText.Save("CPU", "N/A")), "A short grace can snap a bone that just left the screen.", "After the renderers leave the screen, LateUpdate stays every frame for this many frames.");
        internal static string BoneMidDistance => BoneStep("Inside this distance a visible rig updates every frame. At or past it, the mid interval applies.");
        internal static string BoneFarDistance => BoneStep("At or past this, the far interval applies. Raised to the mid distance if it was set closer.");
        internal static string BoneMidInterval => BoneStep("Visible rigs between the mid and far distances run LateUpdate once every N frames.");
        internal static string BoneFarInterval => OptionText.Build(null, BoneStepLoad, "Raised if it is shorter than the mid interval.", "Visible rigs at or past the far distance run LateUpdate once every N frames.");

        internal static string BoneJobs => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low", "transform writes stay on the main thread."),
                OptionText.Save("CPU", "High", "average. About 0.1 ms/frame left on the main thread with jobs on. Delta vs vanilla TBC. 1% N/D.")),
            "If the bone layout is unexpected, this turns itself off and the vanilla update runs.",
            "Bones that update this frame are simulated together on worker threads after LateUpdate. Takes effect immediately.");

        internal static string IkThrottle => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low"),
                OptionText.Save("CPU", "Medium", "average, offscreen only. ms N/D. 1% N/D.")),
            null,
            "After the grace, an offscreen rig runs LateUpdate once every N frames. Anything visible still updates every frame.");

        internal static string IkInterval => OptionText.Build(null, OptionText.Channels(OptionText.Cost("CPU", "N/A"), OptionText.Save("CPU", "Medium", "average, offscreen. ms N/D. 1% N/D.")), "The gap cannot go under 2.", "Offscreen, past the grace, LateUpdate runs once every N frames.");
        internal static string IkGrace => OptionText.Build(null, OptionText.Channels(OptionText.Cost("CPU", "Low"), OptionText.Save("CPU", "N/A")), "A grace of one frame can snap a rig as it leaves the screen.", "After the character leaves the screen, LateUpdate stays every frame for this many frames.");

        internal static string SpawnPrewarm => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Medium", "hitch. Where a preload was already over 0.5 ms, about 8 ms median on those hitch frames, up to about 50. 1% of every frame N/D."),
                OptionText.Save("CPU", "N/A", "average. The work moves earlier. 1% at the real spawn TBC.")),
            "During the fade the game's loader refuses the call, so the loads start directly and are not waited on.",
            "Preloads animator, avatar, and skins a few masters at a time after the stage is in. Takes effect on the next stage.");

        internal static string WarmBosses => OptionText.Build(null, OptionText.Channels(OptionText.Cost("CPU", "Low"), OptionText.Save("CPU", "TBC", "1% at a champion's first spawn.")), null, "Champions are not in the game's own preload. This adds them from the stage list. Takes effect on the next stage.");
        internal static string SkipLongFrames => OptionText.Build(null, OptionText.Channels(OptionText.Cost("CPU", "N/A"), OptionText.Save("CPU", "TBC", "1%, only when the last frame was already over 33 ms. Average N/A.")), "The queue finishes later.", "Does not register a master on a frame that follows a long one.");

        internal static string EffectWarm => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Medium", "hitch. A new copy averages about 0.2-0.3 ms, and a warm that already cost over 0.5 ms sat near 1.6 ms on hitch frames, up to about 30. 1% of every frame N/D."),
                OptionText.Save("CPU", "TBC", "a later spawn skips that Instantiate. Whether the 1% moves is N/D.")),
            "The build is moved off the spawn onto another frame. Several on one frame add, and one cold call has been most of a hitch frame.",
            "Pins prefabs for the run and builds copies up to the observed peak while frames are short. A pooled spawn still restarts particles and walks the hierarchy. Unused effects are only pinned.");

        internal static string KeepPools => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low"),
                OptionText.Save("CPU", "Low", "the next spawn skips Instantiate, about 0.2-0.3 ms a call. 1% N/D.")),
            null,
            "The game destroys a pool 45 seconds after its last use, one such pool per check. The next spawn of that effect creates a copy again.");
        internal static string KeepCopiesActive => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low", "a few hundredths of a ms/frame at 96 copies. 1% TBC."),
                OptionText.Cost("GPU", "N/D", "particles and renderers are hidden. One run saw no rise. TBC."),
                OptionText.Save("CPU", "Low", "the next spawn skips SetActive. ms N/D.")),
            "Past the cap, further copies are deactivated.",
            "Frequent effects stay active while parked. Particles, renderers, and lights are hidden.");

        internal static string ParkLimit => OptionText.Build(null, OptionText.Channels(OptionText.Cost("CPU", "Low", "one script tick per active parked copy, every frame."), OptionText.Save("CPU", "N/A")), "Toward 192 that tick is paid for more copies.", "How many parked copies may stay active at once. 96 is what those runs were measured at.");
        internal static string WarmFullPeak => OptionText.Build(
            OptionText.Badge("Experimental", OptionText.Experimental),
            OptionText.Channels(
                OptionText.Cost("CPU", "Medium", "hitch. Each extra copy is an Instantiate, about 0.2-0.3 ms on average. They add on one frame. Over 1 ms each is TBC."),
                OptionText.Save("CPU", "TBC", "1% of a later burst. Average across a run stays low. 1% N/D.")),
            "A combat frame has reached several ms. More copies mean more memory.",
            "Builds copies up to the largest concurrency this run reached, at most 20, ignoring Warm Peak Copies.");

        internal static string WarmShaders => OptionText.Build(
            OptionText.Badge("Experimental", OptionText.Experimental),
            OptionText.Channels(
                OptionText.Cost("CPU", "Low", "tens of microseconds a call, about 10 ms for the catalog."),
                OptionText.Save("GPU", "TBD", "not shown to remove the first-draw hitch. 1% N/D.")),
            "This player does not report whether the driver built a GPU program.",
            "Compiles variants of pooled effects. Waits until the lobby. The black screen uses the frame budget, then Warmup Pace. Splash, intro, and title are left alone.");

        internal static string WarmPeakCopies => OptionText.Build(null, OptionText.Channels(OptionText.Cost("CPU", "Low", "about 0.2-0.3 ms per new copy, not every frame. Over 1 ms is TBC."), OptionText.Save("CPU", "TBC")), "Zero only pins. Ignored while Warm Full Observed Peak is on.", "Most inactive copies to build ahead for an effect this run uses.");
        internal static string CullSecondary => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low"),
                OptionText.Save("CPU", "N/D", "secondary simulation stopped past the distance. 1% N/D."),
                OptionText.Save("GPU", "N/D", "those particles and optional lights. Main particles stay.")),
            null,
            "Only during a stage. They resume closer than about three quarters of the distance.");

        internal static string CullDistance => OptionText.Build(null, OptionText.Channels(OptionText.Cost("CPU", "N/A"), OptionText.Save("GPU", "N/D")), null, "Meters. Secondary systems stop here and resume at about three quarters of this.");
        internal static string CullOffScreen => OptionText.Build(null, OptionText.Channels(OptionText.Cost("CPU", "N/A"), OptionText.Save("GPU", "N/D")), null, "Also stops secondary systems outside the camera. They resume once the effect is visible.");
        internal static string CullInterval => OptionText.Build(null, OptionText.Channels(OptionText.Cost("CPU", "Low", "the check, less often if the gap is longer."), OptionText.Save("CPU", "N/A")), "A longer gap leaves a system running after it has gone far.", "Frames between those distance checks.");

        internal static string MobStatus => Readout("Draws the spawn-prewarm counts. The text rebuilds twice a second.");
        internal static string MobLog => OptionText.Build(OptionText.Badge("Readout", OptionText.Readout), ReadoutLoad, "The log grows while this stays on.", "Writes the spawn-prewarm counts once a second.");
        internal static string EffectStatus => Readout("Draws the effect-pool counts. The text rebuilds twice a second.");
        internal static string ShaderStatus => OptionText.Build(OptionText.Badge("Readout", OptionText.Readout), ReadoutLoad, "A hit is not proof the driver built a GPU program.", "Draws shader-warmup progress and checks the variant on the first spawn of each pooled effect.");
        internal static string EffectLog => OptionText.Build(OptionText.Badge("Readout", OptionText.Readout), ReadoutLoad, "The log grows while this stays on.", "Writes the effect-pool counts once a second.");
        internal static string LateImmunity => OptionText.Build(OptionText.Badge("Gameplay", OptionText.Gameplay), null, "The host applies it. A joining client cannot. Revives already get 3 seconds, and this does not replace that.", "When a player other than the host spawns on a stage, the host grants them Immune for 5 seconds.");
        internal static string NetworkDebug => OptionText.Build(OptionText.Badge("Readout", OptionText.Readout), ReadoutLoad, "A client sees other players' links only when the host also has ROR-O.", "Shows each player's Steam connection in the lobby and on the ally card. Version is red when it is not yours.");

        internal static string SteamThreaded => OptionText.Build(
            null,
            OptionText.Channels(
                OptionText.Cost("CPU", "Low", "the main thread still takes packets once a frame."),
                OptionText.Save("CPU", "Medium", "average, multiplayer. Reads and sends were a few tenths of a ms/frame. 1% N/D.")),
            "If the worker fails, sends and reads fall back to the main thread.",
            "The packet format does not change. Takes effect immediately.");

        internal static string HitchWatch => OptionText.Build(OptionText.Badge("Readout", OptionText.Readout), OptionText.Channels(OptionText.Cost("CPU", "Low", "ms N/D."), OptionText.Save("CPU", "N/A")), "No managed call tree. With the Mono profiler, both overheads are in the times.", "Reads Unity's counters every frame and writes the slow tail. Applies immediately.");
        internal static string HitchFps => OptionText.Build(null, OptionText.Channels(OptionText.Cost("CPU", "N/A"), OptionText.Save("CPU", "N/A")), null, "The frame rate a stretch is marked against. 60 is a frame every 16.7 ms.");
        internal static string MonoProfiler => OptionText.Build(OptionText.Badge("Restart", OptionText.Restart), OptionText.Channels(OptionText.Cost("CPU", "High", "its overhead is inside every time it records. ms N/D. 1% N/D."), OptionText.Save("CPU", "N/A")), "A release build keeps this off. Nothing happens until the next launch.", "ROR-O runs normally when this is off or when the profiler is not installed.");

        private static string CapLoad => OptionText.Channels(
            OptionText.Cost("CPU", "N/A"),
            OptionText.Save("CPU", "Medium", "average, once this cap is crossed. ms N/D. 1% N/D."),
            OptionText.Save("GPU", "N/D"));

        private static string TuneLoad => OptionText.Channels(
            OptionText.Cost("CPU", "N/A"),
            OptionText.Save("CPU", "N/D", "chooses which hits survive, not a measured ms."));

        private static string BoneStepLoad => OptionText.Channels(
            OptionText.Cost("CPU", "N/A"),
            OptionText.Save("CPU", "Medium", "average, only the bones this step skips. ms N/D. 1% N/D."));

        private static string ReadoutLoad => OptionText.Channels(
            OptionText.Cost("CPU", "Low"),
            OptionText.Save("CPU", "N/A"));

        private static string Cap(string body) => OptionText.Build(null, CapLoad, null, body);
        private static string Tune(string body) => OptionText.Build(null, TuneLoad, null, body);
        private static string BoneStep(string body) => OptionText.Build(null, BoneStepLoad, null, body);
        private static string Readout(string body) => OptionText.Build(OptionText.Badge("Readout", OptionText.Readout), ReadoutLoad, null, body);
    }
}
