using System;
using System.Diagnostics;
using System.Text;

namespace ROR_O
{
    // Native player-loop phases and the methods we open in the decompiler.
    // detail column: n.phys:120 means 120 microseconds of that phase this frame (or per frame in a summary).
    // SphereSearch.RefreshCandidates:40/12.00 means 40 microseconds and 12.00 calls per frame.
    internal static class HitchProbes
    {
        internal enum Phase
        {
            Physics,
            Particles,
            Anim,
            Canvas,
            Skin,
            Count
        }

        internal enum Id
        {
            SphereRefresh,
            SphereSort,
            SphereDistinct,
            SphereTeam,
            RadialSearch,
            RadialPull,
            WarCry,
            SpawnEffect,
            AiTick,
            PathAgent,
            Prewarm,
            DynBone,
            Count
        }

        private static readonly string[] PhaseNames =
        {
            "n.phys",
            "n.part",
            "n.anim",
            "n.canvas",
            "n.skin"
        };

        private static readonly string[] MethodNames =
        {
            "SphereSearch.RefreshCandidates",
            "SphereSearch.OrderByDistance",
            "SphereSearch.DistinctHurtBoxes",
            "SphereSearch.FilterTeam",
            "RadialForce.SearchForTargets",
            "RadialForce.ApplyPull",
            "EquipmentSlot.FireTeamWarCry",
            "EffectManager.SpawnEffect",
            "BaseAI.ManagedFixedUpdate",
            "NodeGraphNavigation.UpdateAgent",
            "SpawnPrewarm.PreloadAssets",
            "DynamicBone.LateUpdate"
        };

        private static readonly long[] PhaseLive = new long[(int)Phase.Count];
        private static readonly long[] MethodLive = new long[(int)Id.Count];
        private static readonly int[] CallsLive = new int[(int)Id.Count];

        private static readonly long[] PhaseFrame = new long[(int)Phase.Count];
        private static readonly long[] MethodFrame = new long[(int)Id.Count];
        private static readonly int[] CallsFrame = new int[(int)Id.Count];

        private static readonly long[] PhaseWindow = new long[(int)Phase.Count];
        private static readonly long[] MethodWindow = new long[(int)Id.Count];
        private static readonly int[] CallsWindow = new int[(int)Id.Count];

        private static readonly long[] PhaseHeld = new long[(int)Phase.Count];
        private static readonly long[] MethodHeld = new long[(int)Id.Count];
        private static readonly int[] CallsHeld = new int[(int)Id.Count];

        internal static bool Armed { get; set; }

        internal static void AddPhase(Phase phase, long ticks)
        {
            if (ticks > 0)
            {
                PhaseLive[(int)phase] += ticks;
            }
        }

        internal static void Add(Id id, long ticks)
        {
            int index = (int)id;
            CallsLive[index]++;
            if (ticks > 0)
            {
                MethodLive[index] += ticks;
            }
        }

        internal static void PublishFrame()
        {
            for (int i = 0; i < PhaseLive.Length; i++)
            {
                PhaseFrame[i] = PhaseLive[i];
                PhaseWindow[i] += PhaseLive[i];
                PhaseLive[i] = 0;
            }

            for (int i = 0; i < MethodLive.Length; i++)
            {
                MethodFrame[i] = MethodLive[i];
                CallsFrame[i] = CallsLive[i];
                MethodWindow[i] += MethodLive[i];
                CallsWindow[i] += CallsLive[i];
                MethodLive[i] = 0;
                CallsLive[i] = 0;
            }
        }

        internal static float PublishedPhaseMs(Phase phase)
        {
            return TicksToUs(PhaseFrame[(int)phase]) / 1000f;
        }

        internal static void Hold()
        {
            Array.Copy(PhaseFrame, PhaseHeld, PhaseHeld.Length);
            Array.Copy(MethodFrame, MethodHeld, MethodHeld.Length);
            Array.Copy(CallsFrame, CallsHeld, CallsHeld.Length);
        }

        internal static void AppendHeld(StringBuilder line)
        {
            AppendPhases(line, PhaseHeld, 1);
            AppendMethods(line, MethodHeld, CallsHeld, 1);
        }

        internal static void AppendWindow(StringBuilder line, int frames)
        {
            if (frames <= 0)
            {
                ClearWindow();
                return;
            }

            AppendPhases(line, PhaseWindow, frames);
            AppendMethods(line, MethodWindow, CallsWindow, frames);
            ClearWindow();
        }

        private static void ClearWindow()
        {
            Array.Clear(PhaseWindow, 0, PhaseWindow.Length);
            Array.Clear(MethodWindow, 0, MethodWindow.Length);
            Array.Clear(CallsWindow, 0, CallsWindow.Length);
        }

        private static void AppendPhases(StringBuilder line, long[] ticks, int frames)
        {
            bool any = false;
            for (int i = 0; i < ticks.Length; i++)
            {
                long us = TicksToUs(ticks[i]) / frames;
                if (us <= 0)
                {
                    continue;
                }

                if (any)
                {
                    line.Append(';');
                }

                any = true;
                line.Append(PhaseNames[i]);
                line.Append(':');
                line.Append(us);
            }

            if (any)
            {
                line.Append('|');
            }
        }

        private static void AppendMethods(StringBuilder line, long[] ticks, int[] calls, int frames)
        {
            bool any = false;
            for (int i = 0; i < ticks.Length; i++)
            {
                if (calls[i] <= 0 && ticks[i] <= 0)
                {
                    continue;
                }

                long us = TicksToUs(ticks[i]) / frames;
                long hundredths = ((long)calls[i] * 100L) / frames;
                if (us <= 0 && hundredths <= 0)
                {
                    continue;
                }

                if (any)
                {
                    line.Append(';');
                }

                any = true;
                line.Append(MethodNames[i]);
                line.Append(':');
                line.Append(us);
                line.Append('/');
                line.Append(hundredths / 100L);
                line.Append('.');
                long frac = hundredths % 100L;
                if (frac < 10L)
                {
                    line.Append('0');
                }

                line.Append(frac);
            }
        }

        private static long TicksToUs(long ticks)
        {
            if (ticks <= 0)
            {
                return 0;
            }

            return ticks * 1000000L / Stopwatch.Frequency;
        }
    }
}
