using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using RoR2;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Networking;
using UnityEngine.PlayerLoop;
using UnityEngine.SceneManagement;

namespace ROR_O
{
    // Cheap frame watch. Phase durations come from a few player-loop timestamps, GPU time from FrameTimingManager.
    // Release builds do not emit the detailed Unity markers. Rows are written for the slow tail
    // (1% and 0.1%) and for 10s stretches under the recent median or the target FPS.
    // The gap after the frame is wait time, so a capped framerate is not a hitch.
    internal sealed class HitchWatch : MonoBehaviour
    {
        private const int RingSize = 4096;
        private const int WarmupFrames = 180;
        private const int ThresholdInterval = 120;
        private const float SummarySeconds = 10f;
        private const float LogSeconds = 30f;
        private const float FileFlushSeconds = 15f;
        private const float HitchFlushSeconds = 0.5f;
        private const float StallMs = 100f;
        private const int NoiseUs = 2000;
        private const float MedianDip = 1.10f;

        private enum Metric
        {
            Scripts,
            Late,
            Fixed,
            Physics,
            Render,
            Canvas,
            Anim,
            Particles,
            Gc,
            Wait,
            GcBytes,
            Count
        }

        private struct FrameSnapshot
        {
            public float FrameMs;
            public float CpuMs;
            public float RenderMs;
            public float ScriptsMs;
            public float LateMs;
            public float FixedMs;
            public float PhysicsMs;
            public float CanvasMs;
            public float AnimMs;
            public float ParticlesMs;
            public float GcMs;
            public float WaitMs;
            public long GcBytes;
        }

        private struct Totals
        {
            public int Frames;
            public int Hitches;
            public double FrameMs;
            public double CpuMs;
            public double RenderMs;
            public double ScriptsMs;
            public double LateMs;
            public double FixedMs;
            public double PhysicsMs;
            public double CanvasMs;
            public double AnimMs;
            public double ParticlesMs;
            public double GcMs;
            public double WaitMs;
            public long GcBytes;
            public FrameSnapshot Worst;
            public bool HasWorst;
        }

        private static readonly ProfilerCategory[] Categories =
        {
            ProfilerCategory.Internal,
            ProfilerCategory.Scripts,
            ProfilerCategory.Render,
            ProfilerCategory.Physics,
            ProfilerCategory.Animation,
            ProfilerCategory.Particles,
            ProfilerCategory.Gui,
            ProfilerCategory.Memory
        };

        private readonly ProfilerRecorder[] recorders = new ProfilerRecorder[(int)Metric.Count];
        private readonly bool[] recorderOn = new bool[(int)Metric.Count];
        private readonly ProfilerMarkerDataUnit[] units = new ProfilerMarkerDataUnit[(int)Metric.Count];
        private readonly string[] recorderNames = new string[(int)Metric.Count];
        private readonly int[] frameUs = new int[RingSize];
        private readonly int[] cpuUs = new int[RingSize];
        private readonly int[] renderUs = new int[RingSize];
        private readonly int[] scratch = new int[RingSize];
        private readonly StringBuilder line = new StringBuilder(640);

        private bool running;
        private bool skipSample;
        private bool resumeSkip;
        private bool writeFailed;
        private StreamWriter? writer;
        private string? filePath;
        private int ringWrite;
        private int ringCount;
        private int sinceThreshold;
        private int frameMedianUs = int.MaxValue;
        private int frameP95Us = int.MaxValue;
        private int frameP99Us = int.MaxValue;
        private int frameP999Us = int.MaxValue;
        private int renderMedianUs = int.MaxValue;
        private int renderP95Us = int.MaxValue;
        private int renderP99Us = int.MaxValue;
        private int renderP999Us = int.MaxValue;
        private int cpuMedianUs = int.MaxValue;
        private int cpuP99Us = int.MaxValue;
        private int cpuP999Us = int.MaxValue;
        private float startedAt;
        private float summaryStart;
        private float logStart;
        private float hitchWindowStart;
        private float nextFileFlush;
        private bool haveHitch;
        private int hitchRank;
        private string hitchReason = string.Empty;
        private FrameSnapshot hitchFrame;
        private Totals summary;
        private Totals logWindow;

        private void Update()
        {
            if (!HitchWatchSettings.Enabled)
            {
                if (running)
                {
                    StopWatch();
                }

                return;
            }

            if (!running)
            {
                StartWatch();
            }

            if (Time.timeScale <= 0f)
            {
                resumeSkip = true;
                return;
            }

            if (resumeSkip || skipSample)
            {
                resumeSkip = false;
                skipSample = false;
                return;
            }

            float frameMs = Time.unscaledDeltaTime * 1000f;
            if (frameMs < 0.05f)
            {
                return;
            }

            FrameSnapshot frame = ReadFrame(frameMs);
            if (frame.FrameMs >= StallMs)
            {
                Add(ref summary, frame);
                Add(ref logWindow, frame);
                summary.Hitches++;
                logWindow.Hitches++;
                KeepHitch(frame, "stall", 3);
                FlushPendingHitch(force: false);
                TickClocks();
                return;
            }

            ConsiderHitch(frame);
            Push(frame);
            Add(ref summary, frame);
            Add(ref logWindow, frame);
            if (++sinceThreshold >= ThresholdInterval)
            {
                sinceThreshold = 0;
                RecomputeThresholds();
            }

            FlushPendingHitch(force: false);
            TickClocks();
            if (!gpuMissingLogged && ringCount >= WarmupFrames)
            {
                gpuMissingLogged = true;
                RORO.GlobalLogger?.LogInfo(gpuSeen
                    ? "Hitch watch render time is GPU frame time."
                    : "Hitch watch has no GPU timings. render_ms is CPU time after LateUpdate.");
            }
        }

        private void OnDestroy()
        {
            if (running)
            {
                StopWatch();
            }
        }

        private void StartWatch()
        {
            Attach(Metric.Gc, ProfilerCategory.Memory, "GC.Collect");
            InstallPhases();
            InstallProbes();
            HitchProbes.Armed = true;
            ResetPhases();
            writeFailed = false;
            filePath = CreateFile();
            running = true;
            skipSample = true;
            startedAt = Time.unscaledTime;
            summaryStart = startedAt;
            logStart = startedAt;
            hitchWindowStart = startedAt;
            nextFileFlush = startedAt + FileFlushSeconds;
            ringWrite = 0;
            ringCount = 0;
            sinceThreshold = 0;
            summary = default;
            logWindow = default;
            haveHitch = false;
            LogRecorders();
        }

        private void StopWatch()
        {
            HitchProbes.Armed = false;
            RemoveProbes();
            RemovePhases();
            FlushPendingHitch(force: true);
            if (summary.Frames > 0)
            {
                WriteTotals("summary", summary, SummaryReason(summary), summary.Hitches);
            }

            if (logWindow.Frames > 0)
            {
                LogWindow();
            }

            for (int i = 0; i < recorders.Length; i++)
            {
                if (recorderOn[i])
                {
                    recorders[i].Dispose();
                    recorderOn[i] = false;
                }
            }

            if (writer != null)
            {
                FlushWriter();
                writer.Dispose();
                writer = null;
            }

            running = false;
            RORO.GlobalLogger?.LogInfo("Hitch watch stopped.");
        }

        private void TickClocks()
        {
            float now = Time.unscaledTime;
            if (now - summaryStart >= SummarySeconds)
            {
                if (summary.Frames > 0)
                {
                    WriteTotals("summary", summary, SummaryReason(summary), summary.Hitches);
                }

                summary = default;
                summaryStart = now;
            }

            if (now - logStart >= LogSeconds)
            {
                if (logWindow.Frames > 0)
                {
                    LogWindow();
                }

                logWindow = default;
                logStart = now;
            }

            if (writer != null && now >= nextFileFlush)
            {
                FlushWriter();
                nextFileFlush = now + FileFlushSeconds;
            }
        }

        private void FlushWriter()
        {
            if (writer == null || writeFailed)
            {
                return;
            }

            try
            {
                writer.Flush();
            }
            catch (IOException exception)
            {
                writeFailed = true;
                RORO.GlobalLogger?.LogError($"Hitch watch stopped writing: {exception.Message}");
            }
        }

        private void ConsiderHitch(FrameSnapshot frame)
        {
            if (ringCount < WarmupFrames || frameMedianUs == int.MaxValue)
            {
                return;
            }

            int render = Us(frame.RenderMs);
            int cpu = Us(frame.CpuMs);
            bool renderP999 = render >= renderP999Us && render >= renderMedianUs + NoiseUs;
            bool renderP99 = render >= renderP99Us && render >= renderMedianUs + NoiseUs;
            bool cpuP999 = cpu >= cpuP999Us && cpu >= cpuMedianUs + NoiseUs;
            bool cpuP99 = cpu >= cpuP99Us && cpu >= cpuMedianUs + NoiseUs;
            if (!renderP99 && !cpuP99 && !renderP999 && !cpuP999)
            {
                return;
            }

            summary.Hitches++;
            logWindow.Hitches++;
            int rank = renderP999 || cpuP999 ? 2 : 1;
            KeepHitch(frame, ReasonFor(renderP999, cpuP999, renderP99, cpuP99), rank);
        }

        private void KeepHitch(FrameSnapshot frame, string reason, int rank)
        {
            if (!haveHitch || rank > hitchRank || (rank == hitchRank && frame.FrameMs > hitchFrame.FrameMs))
            {
                haveHitch = true;
                hitchRank = rank;
                hitchReason = reason;
                hitchFrame = frame;
                HitchProbes.Hold();
            }
        }

        private void FlushPendingHitch(bool force)
        {
            if (!haveHitch)
            {
                return;
            }

            if (!force && Time.unscaledTime - hitchWindowStart < HitchFlushSeconds)
            {
                return;
            }

            WriteFrame(hitchRank >= 3 ? "stall" : "hitch", hitchFrame, hitchReason, 1);
            haveHitch = false;
            hitchRank = 0;
            hitchWindowStart = Time.unscaledTime;
        }

        private FrameSnapshot ReadFrame(float frameMs)
        {
            float wait = publishedWait;
            float cpu = frameMs - wait;
            if (cpu < 0f)
            {
                cpu = 0f;
            }

            return new FrameSnapshot
            {
                FrameMs = frameMs,
                CpuMs = cpu,
                RenderMs = gpuSeen ? publishedGpu : publishedRenderCpu,
                ScriptsMs = publishedScripts,
                LateMs = publishedLate,
                FixedMs = publishedFixed,
                PhysicsMs = HitchProbes.PublishedPhaseMs(HitchProbes.Phase.Physics),
                CanvasMs = HitchProbes.PublishedPhaseMs(HitchProbes.Phase.Canvas),
                AnimMs = HitchProbes.PublishedPhaseMs(HitchProbes.Phase.Anim),
                ParticlesMs = HitchProbes.PublishedPhaseMs(HitchProbes.Phase.Particles),
                GcMs = ReadMs(Metric.Gc),
                WaitMs = wait,
                GcBytes = ReadManagedBytes()
            };
        }

        private static long ReadManagedBytes()
        {
            long managed = GC.GetTotalMemory(false);
            long delta = lastManagedBytes > 0 && managed > lastManagedBytes ? managed - lastManagedBytes : 0L;
            lastManagedBytes = managed;
            return delta;
        }

        private float ReadMs(Metric metric)
        {
            int index = (int)metric;
            if (!recorderOn[index])
            {
                return 0f;
            }

            ProfilerMarkerDataUnit unit = units[index];
            if (unit != ProfilerMarkerDataUnit.TimeNanoseconds && unit != ProfilerMarkerDataUnit.Undefined)
            {
                return 0f;
            }

            return recorders[index].LastValue / 1000000f;
        }

        private long ReadBytes(Metric metric)
        {
            int index = (int)metric;
            if (!recorderOn[index])
            {
                return 0L;
            }

            return recorders[index].LastValue;
        }

        private void Push(FrameSnapshot frame)
        {
            frameUs[ringWrite] = Us(frame.FrameMs);
            cpuUs[ringWrite] = Us(frame.CpuMs);
            renderUs[ringWrite] = Us(frame.RenderMs);
            ringWrite++;
            if (ringWrite >= RingSize)
            {
                ringWrite = 0;
            }

            if (ringCount < RingSize)
            {
                ringCount++;
            }
        }

        private void RecomputeThresholds()
        {
            if (ringCount < 64)
            {
                return;
            }

            frameMedianUs = Percentile(frameUs, 0.50f);
            frameP95Us = Percentile(frameUs, 0.95f);
            frameP99Us = Percentile(frameUs, 0.99f);
            frameP999Us = Percentile(frameUs, 0.999f);
            cpuMedianUs = Percentile(cpuUs, 0.50f);
            cpuP99Us = Percentile(cpuUs, 0.99f);
            cpuP999Us = Percentile(cpuUs, 0.999f);
            renderMedianUs = Percentile(renderUs, 0.50f);
            renderP95Us = Percentile(renderUs, 0.95f);
            renderP99Us = Percentile(renderUs, 0.99f);
            renderP999Us = Percentile(renderUs, 0.999f);
        }

        private int Percentile(int[] source, float fraction)
        {
            Array.Copy(source, scratch, ringCount);
            int k = (int)Math.Ceiling((ringCount - 1) * fraction);
            if (k < 0)
            {
                k = 0;
            }

            if (k >= ringCount)
            {
                k = ringCount - 1;
            }

            return Select(scratch, 0, ringCount - 1, k);
        }

        private static int Select(int[] data, int left, int right, int k)
        {
            while (left < right)
            {
                int pivotIndex = Partition(data, left, right);
                if (k == pivotIndex)
                {
                    return data[k];
                }

                if (k < pivotIndex)
                {
                    right = pivotIndex - 1;
                }
                else
                {
                    left = pivotIndex + 1;
                }
            }

            return data[left];
        }

        private static int Partition(int[] data, int left, int right)
        {
            int mid = (left + right) >> 1;
            int pivot = data[mid];
            data[mid] = data[right];
            data[right] = pivot;
            int store = left;
            for (int i = left; i < right; i++)
            {
                if (data[i] <= pivot)
                {
                    int swap = data[store];
                    data[store] = data[i];
                    data[i] = swap;
                    store++;
                }
            }

            data[right] = data[store];
            data[store] = pivot;
            return store;
        }

        private static void Add(ref Totals totals, FrameSnapshot frame)
        {
            totals.Frames++;
            totals.FrameMs += frame.FrameMs;
            totals.CpuMs += frame.CpuMs;
            totals.RenderMs += frame.RenderMs;
            totals.ScriptsMs += frame.ScriptsMs;
            totals.LateMs += frame.LateMs;
            totals.FixedMs += frame.FixedMs;
            totals.PhysicsMs += frame.PhysicsMs;
            totals.CanvasMs += frame.CanvasMs;
            totals.AnimMs += frame.AnimMs;
            totals.ParticlesMs += frame.ParticlesMs;
            totals.GcMs += frame.GcMs;
            totals.WaitMs += frame.WaitMs;
            totals.GcBytes += frame.GcBytes;
            if (!totals.HasWorst || frame.FrameMs > totals.Worst.FrameMs)
            {
                totals.Worst = frame;
                totals.HasWorst = true;
            }
        }

        private string SummaryReason(Totals totals)
        {
            if (totals.Frames <= 0 || ringCount < WarmupFrames || frameMedianUs == int.MaxValue)
            {
                return "warmup";
            }

            float average = (float)(totals.FrameMs / totals.Frames);
            float median = frameMedianUs / 1000f;
            float target = TargetMs();
            bool belowMedian = average > median * MedianDip;
            bool belowTarget = average > target;
            if (belowMedian && belowTarget)
            {
                return "below-median+target";
            }

            if (belowMedian)
            {
                return "below-median";
            }

            if (belowTarget)
            {
                return "below-target";
            }

            return "ok";
        }

        private void WriteTotals(string kind, Totals totals, string reason, int hitches)
        {
            if (totals.Frames <= 0)
            {
                return;
            }

            double scale = 1d / totals.Frames;
            var average = new FrameSnapshot
            {
                FrameMs = (float)(totals.FrameMs * scale),
                CpuMs = (float)(totals.CpuMs * scale),
                RenderMs = (float)(totals.RenderMs * scale),
                ScriptsMs = (float)(totals.ScriptsMs * scale),
                LateMs = (float)(totals.LateMs * scale),
                FixedMs = (float)(totals.FixedMs * scale),
                PhysicsMs = (float)(totals.PhysicsMs * scale),
                CanvasMs = (float)(totals.CanvasMs * scale),
                AnimMs = (float)(totals.AnimMs * scale),
                ParticlesMs = (float)(totals.ParticlesMs * scale),
                GcMs = (float)(totals.GcMs * scale),
                WaitMs = (float)(totals.WaitMs * scale),
                GcBytes = totals.GcBytes
            };
            WriteRow(kind, average, totals.HasWorst ? totals.Worst.FrameMs : average.FrameMs, totals.Frames, hitches, reason, totals.HasWorst ? Top(totals.Worst) : Top(average));
        }

        private void WriteFrame(string kind, FrameSnapshot frame, string reason, int hitches)
        {
            WriteRow(kind, frame, frame.FrameMs, 1, hitches, reason, Top(frame));
        }

        private void WriteRow(string kind, FrameSnapshot frame, float maxMs, int frames, int hitches, string reason, string top)
        {
            if (writer == null || writeFailed)
            {
                return;
            }

            float medianMs = frameMedianUs == int.MaxValue ? 0f : frameMedianUs / 1000f;
            float p95Ms = frameP95Us == int.MaxValue ? 0f : frameP95Us / 1000f;
            float p99Ms = frameP99Us == int.MaxValue ? 0f : frameP99Us / 1000f;
            float p999Ms = frameP999Us == int.MaxValue ? 0f : frameP999Us / 1000f;
            float renderP95Ms = renderP95Us == int.MaxValue ? 0f : renderP95Us / 1000f;
            float renderP99Ms = renderP99Us == int.MaxValue ? 0f : renderP99Us / 1000f;
            float renderP999Ms = renderP999Us == int.MaxValue ? 0f : renderP999Us / 1000f;
            line.Clear();
            line.Append(kind);
            Comma();
            AppendFixed(Time.unscaledTime - startedAt, 2);
            Comma();
            line.Append(PlayerCount());
            Comma();
            line.Append(BodyCount());
            Comma();
            line.Append(NetworkServer.active ? 1 : 0);
            Comma();
            line.Append(Sanitize(StageName()));
            Comma();
            AppendFixed(RunSeconds(), 1);
            Comma();
            line.Append(frames);
            Comma();
            AppendFixed(frame.FrameMs, 2);
            Comma();
            AppendFixed(maxMs, 2);
            Comma();
            AppendFixed(frame.CpuMs, 2);
            Comma();
            AppendFixed(frame.RenderMs, 2);
            Comma();
            AppendFixed(frame.ScriptsMs, 2);
            Comma();
            AppendFixed(frame.LateMs, 2);
            Comma();
            AppendFixed(frame.FixedMs, 2);
            Comma();
            AppendFixed(frame.PhysicsMs, 2);
            Comma();
            AppendFixed(frame.CanvasMs, 2);
            Comma();
            AppendFixed(frame.AnimMs, 2);
            Comma();
            AppendFixed(frame.ParticlesMs, 2);
            Comma();
            AppendFixed(frame.GcMs, 2);
            Comma();
            AppendFixed(frame.WaitMs, 2);
            Comma();
            line.Append(frame.GcBytes);
            Comma();
            AppendFixed(medianMs, 2);
            Comma();
            AppendFixed(p95Ms, 2);
            Comma();
            AppendFixed(p99Ms, 2);
            Comma();
            AppendFixed(p999Ms, 2);
            Comma();
            AppendFixed(renderP95Ms, 2);
            Comma();
            AppendFixed(renderP99Ms, 2);
            Comma();
            AppendFixed(renderP999Ms, 2);
            Comma();
            AppendFixed(TargetMs(), 2);
            Comma();
            line.Append(hitches);
            Comma();
            line.Append(reason);
            Comma();
            line.Append(top);
            Comma();
            if (kind == "hitch" || kind == "stall")
            {
                HitchProbes.AppendHeld(line);
            }
            else
            {
                HitchProbes.AppendWindow(line, frames);
            }

            try
            {
                writer.WriteLine(line.ToString());
            }
            catch (IOException exception)
            {
                writeFailed = true;
                RORO.GlobalLogger?.LogError($"Hitch watch stopped writing: {exception.Message}");
            }
        }

        private void LogWindow()
        {
            if (logWindow.Frames <= 0)
            {
                return;
            }

            float average = (float)(logWindow.FrameMs / logWindow.Frames);
            float median = frameMedianUs == int.MaxValue ? 0f : frameMedianUs / 1000f;
            float p95 = frameP95Us == int.MaxValue ? 0f : frameP95Us / 1000f;
            float p99 = frameP99Us == int.MaxValue ? 0f : frameP99Us / 1000f;
            float p999 = frameP999Us == int.MaxValue ? 0f : frameP999Us / 1000f;
            float renderP99 = renderP99Us == int.MaxValue ? 0f : renderP99Us / 1000f;
            float max = logWindow.HasWorst ? logWindow.Worst.FrameMs : average;
            string top = logWindow.HasWorst ? Top(logWindow.Worst) : "none";
            RORO.GlobalLogger?.LogInfo(
                $"Hitch watch {LogSeconds:0}s: players {PlayerCount()}, bodies {BodyCount()}, stage {StageName()}, " +
                $"avg {average:0.0} ms, median {median:0.0}, p95 {p95:0.0}, p99 {p99:0.0}, p99.9 {p999:0.0}, " +
                $"render p99 {renderP99:0.0}, max {max:0.0} ({top}), hitches {logWindow.Hitches}, " +
                $"gc {logWindow.GcBytes / 1048576f:0.0} MB, target {TargetMs():0.0} ms, {SummaryReason(logWindow)}");
        }

        private void LogRecorders()
        {
            RORO.GlobalLogger?.LogInfo(
                "Hitch watch phases: update, late update, fixed update, render, wait. "
                + "detail column: n.phys/n.part/n.anim/n.canvas/n.skin are microseconds of those native player-loop steps; "
                + "a method is microseconds/calls per frame. GC.Collect recorder="
                + (recorderOn[(int)Metric.Gc] ? "on" : "missing"));
            if (Chainloader.PluginInfos.ContainsKey("MonoProfiler"))
            {
                RORO.GlobalLogger?.LogWarning("Hitch watch is running beside the Mono profiler, so the times include profiler overhead.");
            }

            RORO.GlobalLogger?.LogInfo($"Hitch watch file: {filePath}");
        }

        private string? CreateFile()
        {
            try
            {
                string directory = System.IO.Path.Combine(Paths.BepInExRootPath, "HitchWatch");
                Directory.CreateDirectory(directory);
                string name = "HitchWatch_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".csv";
                string path = System.IO.Path.Combine(directory, name);
                writer = new StreamWriter(path, false, new UTF8Encoding(false));
                writer.WriteLine("kind,t_s,players,bodies,host,stage,run_s,frames,frame_ms,max_ms,cpu_ms,render_ms,scripts_ms,late_ms,fixed_ms,physics_ms,canvas_ms,anim_ms,particles_ms,gc_ms,wait_ms,gc_bytes,median_ms,p95_ms,p99_ms,p999_ms,render_p95_ms,render_p99_ms,render_p999_ms,target_ms,hitches,reason,top,detail");
                writer.Flush();
                return path;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                writeFailed = true;
                RORO.GlobalLogger?.LogError($"Hitch watch could not open its file: {exception.Message}");
                return null;
            }
        }

        private void Attach(Metric metric, ProfilerCategory preferred, params string[] names)
        {
            if (TryAttach(metric, preferred, names, bytes: false))
            {
                return;
            }

            for (int i = 0; i < Categories.Length; i++)
            {
                if (TryAttach(metric, Categories[i], names, bytes: false))
                {
                    return;
                }
            }

            recorderNames[(int)metric] = "missing";
        }

        private bool TryAttach(Metric metric, ProfilerCategory category, string[] names, bool bytes)
        {
            ProfilerRecorderOptions options = ProfilerRecorderOptions.Default;
            if (!bytes)
            {
                options |= ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            }

            for (int i = 0; i < names.Length; i++)
            {
                ProfilerRecorder recorder = ProfilerRecorder.StartNew(category, names[i], 1, options);
                if (!recorder.Valid)
                {
                    recorder.Dispose();
                    continue;
                }

                if (bytes && recorder.UnitType != ProfilerMarkerDataUnit.Bytes && recorder.UnitType != ProfilerMarkerDataUnit.Count && recorder.UnitType != ProfilerMarkerDataUnit.Undefined)
                {
                    recorder.Dispose();
                    continue;
                }

                int index = (int)metric;
                recorders[index] = recorder;
                recorderOn[index] = true;
                units[index] = recorder.UnitType;
                recorderNames[index] = names[i] + ":" + recorder.UnitType;
                return true;
            }

            return false;
        }

        private void Comma()
        {
            line.Append(',');
        }

        private void AppendFixed(double value, int decimals)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            {
                value = 0d;
            }

            double scale = decimals <= 0 ? 1d : decimals == 1 ? 10d : 100d;
            long scaled = (long)Math.Round(value * scale);
            long whole = decimals <= 0 ? scaled : scaled / (long)scale;
            line.Append(whole);
            if (decimals <= 0)
            {
                return;
            }

            long fraction = scaled % (long)scale;
            line.Append('.');
            if (decimals == 2 && fraction < 10)
            {
                line.Append('0');
            }

            line.Append(fraction);
        }

        private static int Us(float milliseconds)
        {
            int value = (int)(milliseconds * 1000f);
            return value < 0 ? 0 : value;
        }

        private static float TargetMs()
        {
            return 1000f / HitchWatchSettings.TargetFps;
        }

        private static string ReasonFor(bool renderP999, bool cpuP999, bool renderP99, bool cpuP99)
        {
            if (renderP999 && cpuP999)
            {
                return "p999";
            }

            if (renderP999)
            {
                return "p999-render";
            }

            if (cpuP999)
            {
                return "p999-cpu";
            }

            if (renderP99 && cpuP99)
            {
                return "p99";
            }

            if (renderP99)
            {
                return "p99-render";
            }

            return cpuP99 ? "p99-cpu" : "p99";
        }

        private static string Top(FrameSnapshot frame)
        {
            float best = 0.05f;
            string name = "frame";
            Consider(frame.ScriptsMs, "scripts", ref best, ref name);
            Consider(frame.LateMs, "late", ref best, ref name);
            Consider(frame.FixedMs, "fixed", ref best, ref name);
            Consider(frame.RenderMs, "render", ref best, ref name);
            Consider(frame.GcMs, "gc", ref best, ref name);
            Consider(frame.WaitMs, "wait", ref best, ref name);
            return name;
        }

        private static void Consider(float value, string candidate, ref float best, ref string name)
        {
            if (value > best)
            {
                best = value;
                name = candidate;
            }
        }

        private static int PlayerCount()
        {
            return NetworkUser.readOnlyInstancesList != null ? NetworkUser.readOnlyInstancesList.Count : 0;
        }

        private static int BodyCount()
        {
            return CharacterBody.readOnlyInstancesList != null ? CharacterBody.readOnlyInstancesList.Count : 0;
        }

        private static string StageName()
        {
            Stage stage = Stage.instance;
            if (stage && stage.sceneDef && !string.IsNullOrEmpty(stage.sceneDef.baseSceneName))
            {
                return stage.sceneDef.baseSceneName;
            }

            string scene = SceneManager.GetActiveScene().name;
            return string.IsNullOrEmpty(scene) ? "none" : scene;
        }

        private static float RunSeconds()
        {
            Run run = Run.instance;
            return run ? run.GetRunStopwatch() : 0f;
        }

        private static string Sanitize(string value)
        {
            return value.Replace(',', ' ').Replace('\n', ' ').Replace('\r', ' ');
        }

        private struct HitchFrameBegin
        {
        }

        private struct HitchUpdateStart
        {
        }

        private struct HitchUpdateEnd
        {
        }

        private struct HitchLateEnd
        {
        }

        private struct HitchFixedStart
        {
        }

        private struct HitchFixedEnd
        {
        }

        private struct HitchPostLateEnd
        {
        }

        private static readonly FrameTiming[] timingSlot = new FrameTiming[1];
        private static bool phasesInstalled;
        private static bool frameClosed;
        private static bool gpuSeen;
        private static bool gpuMissingLogged;
        private static int fixedState;
        private static long lastManagedBytes;
        private static double stampUpdateStart;
        private static double stampUpdateEnd;
        private static double stampLateEnd;
        private static double stampFixedStart;
        private static double stampFixedEnd;
        private static double stampPostLateEnd;
        private static float publishedScripts;
        private static float publishedLate;
        private static float publishedFixed;
        private static float publishedRenderCpu;
        private static float publishedWait;
        private static float publishedGpu;

        private static void ResetPhases()
        {
            frameClosed = false;
            fixedState = 0;
            gpuSeen = false;
            gpuMissingLogged = false;
            lastManagedBytes = 0;
            stampUpdateStart = 0;
            stampUpdateEnd = 0;
            stampLateEnd = 0;
            stampFixedStart = 0;
            stampFixedEnd = 0;
            stampPostLateEnd = 0;
            publishedScripts = 0;
            publishedLate = 0;
            publishedFixed = 0;
            publishedRenderCpu = 0;
            publishedWait = 0;
            publishedGpu = 0;
        }

        private static void InstallPhases()
        {
            if (phasesInstalled)
            {
                return;
            }

            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] stages = root.subSystemList;
            for (int i = 0; i < stages.Length; i++)
            {
                Type stageType = stages[i].type;
                if (stageType == typeof(EarlyUpdate))
                {
                    stages[i] = WithMarker(stages[i], typeof(HitchFrameBegin), OnFrameBegin, false);
                }
                else if (stageType == typeof(FixedUpdate))
                {
                    stages[i] = WithMarker(stages[i], typeof(HitchFixedStart), OnFixedStart, false);
                    stages[i] = WithMarker(stages[i], typeof(HitchFixedEnd), OnFixedEnd, true);
                }
                else if (stageType == typeof(Update))
                {
                    stages[i] = WithMarker(stages[i], typeof(HitchUpdateStart), OnUpdateStart, false);
                    stages[i] = WithMarker(stages[i], typeof(HitchUpdateEnd), OnUpdateEnd, true);
                }
                else if (stageType == typeof(PreLateUpdate))
                {
                    stages[i] = WithMarker(stages[i], typeof(HitchLateEnd), OnLateEnd, true);
                }
                else if (stageType == typeof(PostLateUpdate))
                {
                    stages[i] = WithMarker(stages[i], typeof(HitchPostLateEnd), OnPostLateEnd, true);
                }
            }

            InstallNativeBrackets(stages);
            root.subSystemList = stages;
            PlayerLoop.SetPlayerLoop(root);
            phasesInstalled = true;
        }

        private static void InstallNativeBrackets(PlayerLoopSystem[] stages)
        {
            bool physics = false;
            bool particlesBegin = false;
            bool particlesEnd = false;
            bool animBegin = false;
            bool animEnd = false;
            bool canvas = false;
            bool skin = false;
            for (int i = 0; i < stages.Length; i++)
            {
                physics |= Bracket(ref stages[i], typeof(FixedUpdate.PhysicsFixedUpdate), typeof(HitchPhysicsBegin), OnPhysicsBegin, typeof(HitchPhysicsEnd), OnPhysicsEnd);
                particlesBegin |= Bracket(ref stages[i], typeof(PreLateUpdate.ParticleSystemBeginUpdateAll), typeof(HitchParticlesBegin), OnParticlesBegin, typeof(HitchParticlesEnd), OnParticlesEnd);
                particlesEnd |= Bracket(ref stages[i], typeof(PostLateUpdate.ParticleSystemEndUpdateAll), typeof(HitchParticlesSyncBegin), OnParticlesBegin, typeof(HitchParticlesSyncEnd), OnParticlesEnd);
                animBegin |= Bracket(ref stages[i], typeof(PreLateUpdate.DirectorUpdateAnimationBegin), typeof(HitchAnimBegin), OnAnimBegin, typeof(HitchAnimEnd), OnAnimEnd);
                animEnd |= Bracket(ref stages[i], typeof(PreLateUpdate.DirectorUpdateAnimationEnd), typeof(HitchAnimSyncBegin), OnAnimBegin, typeof(HitchAnimSyncEnd), OnAnimEnd);
                canvas |= Bracket(ref stages[i], typeof(PostLateUpdate.PlayerUpdateCanvases), typeof(HitchCanvasBegin), OnCanvasBegin, typeof(HitchCanvasEnd), OnCanvasEnd);
                skin |= Bracket(ref stages[i], typeof(PostLateUpdate.UpdateAllSkinnedMeshes), typeof(HitchSkinBegin), OnSkinBegin, typeof(HitchSkinEnd), OnSkinEnd);
            }

            RORO.GlobalLogger?.LogInfo(
                "Hitch watch native steps: physics " + (physics ? "on" : "missing")
                + ", particles " + (particlesBegin && particlesEnd ? "on" : "missing")
                + ", animation " + (animBegin && animEnd ? "on" : "missing")
                + ", canvas " + (canvas ? "on" : "missing")
                + ", skinned meshes " + (skin ? "on" : "missing")
                + ".");
        }

        private static bool Bracket(
            ref PlayerLoopSystem node,
            Type child,
            Type beginType,
            PlayerLoopSystem.UpdateFunction begin,
            Type endType,
            PlayerLoopSystem.UpdateFunction end)
        {
            PlayerLoopSystem[] list = node.subSystemList;
            if (list == null || list.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].type != child)
                {
                    continue;
                }

                var systems = new List<PlayerLoopSystem>(list.Length + 2);
                for (int j = 0; j < list.Length; j++)
                {
                    if (j == i)
                    {
                        systems.Add(new PlayerLoopSystem { type = beginType, updateDelegate = begin });
                    }

                    systems.Add(list[j]);
                    if (j == i)
                    {
                        systems.Add(new PlayerLoopSystem { type = endType, updateDelegate = end });
                    }
                }

                node.subSystemList = systems.ToArray();
                return true;
            }

            for (int i = 0; i < list.Length; i++)
            {
                PlayerLoopSystem childNode = list[i];
                if (!Bracket(ref childNode, child, beginType, begin, endType, end))
                {
                    continue;
                }

                list[i] = childNode;
                node.subSystemList = list;
                return true;
            }

            return false;
        }

        private static void RemovePhases()
        {
            if (!phasesInstalled)
            {
                return;
            }

            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] stages = root.subSystemList;
            for (int i = 0; i < stages.Length; i++)
            {
                stages[i] = StripMarkers(stages[i]);
            }

            root.subSystemList = stages;
            PlayerLoop.SetPlayerLoop(root);
            phasesInstalled = false;
        }

        private static PlayerLoopSystem WithMarker(PlayerLoopSystem stage, Type marker, PlayerLoopSystem.UpdateFunction action, bool atEnd)
        {
            var systems = new List<PlayerLoopSystem>(stage.subSystemList ?? Array.Empty<PlayerLoopSystem>());
            if (systems.Exists(system => system.type == marker))
            {
                return stage;
            }

            var added = new PlayerLoopSystem
            {
                type = marker,
                updateDelegate = action
            };
            if (atEnd)
            {
                systems.Add(added);
            }
            else
            {
                systems.Insert(0, added);
            }

            stage.subSystemList = systems.ToArray();
            return stage;
        }

        private static bool IsHitchMarker(Type type)
        {
            return type == typeof(HitchFrameBegin)
                || type == typeof(HitchUpdateStart)
                || type == typeof(HitchUpdateEnd)
                || type == typeof(HitchLateEnd)
                || type == typeof(HitchFixedStart)
                || type == typeof(HitchFixedEnd)
                || type == typeof(HitchPostLateEnd)
                || type == typeof(HitchPhysicsBegin)
                || type == typeof(HitchPhysicsEnd)
                || type == typeof(HitchParticlesBegin)
                || type == typeof(HitchParticlesEnd)
                || type == typeof(HitchParticlesSyncBegin)
                || type == typeof(HitchParticlesSyncEnd)
                || type == typeof(HitchAnimBegin)
                || type == typeof(HitchAnimEnd)
                || type == typeof(HitchAnimSyncBegin)
                || type == typeof(HitchAnimSyncEnd)
                || type == typeof(HitchCanvasBegin)
                || type == typeof(HitchCanvasEnd)
                || type == typeof(HitchSkinBegin)
                || type == typeof(HitchSkinEnd);
        }

        private static PlayerLoopSystem StripMarkers(PlayerLoopSystem node)
        {
            if (node.subSystemList == null)
            {
                return node;
            }

            var systems = new List<PlayerLoopSystem>(node.subSystemList.Length);
            for (int i = 0; i < node.subSystemList.Length; i++)
            {
                if (IsHitchMarker(node.subSystemList[i].type))
                {
                    continue;
                }

                systems.Add(StripMarkers(node.subSystemList[i]));
            }

            node.subSystemList = systems.ToArray();
            return node;
        }

        private static Harmony? probeHarmony;

        private static void InstallProbes()
        {
            probeHarmony = new Harmony("net.sixik.plugin.roro.hitchprobes");
            HitchProbeHooks.Install(probeHarmony);
        }

        private static void RemoveProbes()
        {
            if (probeHarmony == null)
            {
                return;
            }

            HitchProbeHooks.Uninstall(probeHarmony);
            probeHarmony = null;
        }

        private static long physicsStamp;
        private static long particleStamp;
        private static long animStamp;
        private static long canvasStamp;
        private static long skinStamp;

        private static void OnPhysicsBegin()
        {
            physicsStamp = Stopwatch.GetTimestamp();
        }

        private static void OnPhysicsEnd()
        {
            HitchProbes.AddPhase(HitchProbes.Phase.Physics, Stopwatch.GetTimestamp() - physicsStamp);
        }

        private static void OnParticlesBegin()
        {
            particleStamp = Stopwatch.GetTimestamp();
        }

        private static void OnParticlesEnd()
        {
            HitchProbes.AddPhase(HitchProbes.Phase.Particles, Stopwatch.GetTimestamp() - particleStamp);
        }

        private static void OnAnimBegin()
        {
            animStamp = Stopwatch.GetTimestamp();
        }

        private static void OnAnimEnd()
        {
            HitchProbes.AddPhase(HitchProbes.Phase.Anim, Stopwatch.GetTimestamp() - animStamp);
        }

        private static void OnCanvasBegin()
        {
            canvasStamp = Stopwatch.GetTimestamp();
        }

        private static void OnCanvasEnd()
        {
            HitchProbes.AddPhase(HitchProbes.Phase.Canvas, Stopwatch.GetTimestamp() - canvasStamp);
        }

        private static void OnSkinBegin()
        {
            skinStamp = Stopwatch.GetTimestamp();
        }

        private static void OnSkinEnd()
        {
            HitchProbes.AddPhase(HitchProbes.Phase.Skin, Stopwatch.GetTimestamp() - skinStamp);
        }

        private struct HitchPhysicsBegin { }
        private struct HitchPhysicsEnd { }
        private struct HitchParticlesBegin { }
        private struct HitchParticlesEnd { }
        private struct HitchParticlesSyncBegin { }
        private struct HitchParticlesSyncEnd { }
        private struct HitchAnimBegin { }
        private struct HitchAnimEnd { }
        private struct HitchAnimSyncBegin { }
        private struct HitchAnimSyncEnd { }
        private struct HitchCanvasBegin { }
        private struct HitchCanvasEnd { }
        private struct HitchSkinBegin { }
        private struct HitchSkinEnd { }

        private static void OnFrameBegin()
        {
            HitchProbes.PublishFrame();
            double now = Time.realtimeSinceStartupAsDouble;
            if (frameClosed && stampUpdateEnd >= stampUpdateStart && stampLateEnd >= stampUpdateEnd && stampPostLateEnd >= stampLateEnd)
            {
                publishedScripts = PhaseMs(stampUpdateEnd - stampUpdateStart);
                publishedLate = PhaseMs(stampLateEnd - stampUpdateEnd);
                publishedFixed = fixedState == 2 ? PhaseMs(stampFixedEnd - stampFixedStart) : 0f;
                publishedRenderCpu = PhaseMs(stampPostLateEnd - stampLateEnd);
                publishedWait = PhaseMs(now - stampPostLateEnd);
                uint timings = FrameTimingManager.GetLatestTimings(1, timingSlot);
                if (timings > 0 && timingSlot[0].gpuFrameTime > 0.05 && timingSlot[0].gpuFrameTime < 1000d)
                {
                    publishedGpu = (float)timingSlot[0].gpuFrameTime;
                    gpuSeen = true;
                }
                else
                {
                    publishedGpu = 0f;
                }
            }

            fixedState = 0;
            frameClosed = false;
        }

        private static void OnFixedStart()
        {
            if (fixedState == 0)
            {
                stampFixedStart = Time.realtimeSinceStartupAsDouble;
            }

            fixedState = 1;
        }

        private static void OnFixedEnd()
        {
            stampFixedEnd = Time.realtimeSinceStartupAsDouble;
            fixedState = 2;
        }

        private static void OnUpdateStart()
        {
            stampUpdateStart = Time.realtimeSinceStartupAsDouble;
        }

        private static void OnUpdateEnd()
        {
            stampUpdateEnd = Time.realtimeSinceStartupAsDouble;
        }

        private static void OnLateEnd()
        {
            stampLateEnd = Time.realtimeSinceStartupAsDouble;
        }

        private static void OnPostLateEnd()
        {
            stampPostLateEnd = Time.realtimeSinceStartupAsDouble;
            frameClosed = true;
            FrameTimingManager.CaptureFrameTimings();
        }

        private static float PhaseMs(double seconds)
        {
            if (seconds < 0d || seconds > 5d)
            {
                return 0f;
            }

            return (float)(seconds * 1000d);
        }
    }

    internal static class HitchWatchSettings
    {
        private const string MonoProfilerDescription =
            "On the next launch, load the separately installed BepInEx.Debug Mono Profiler. ROR-O runs normally when this is off or when the profiler is not installed. The switch does not take effect until the game is restarted. (The profiler is only built in Dev builds)";

        private static ConfigEntry<bool>? enabled;
        private static ConfigEntry<int>? targetFps;
        private static ConfigEntry<bool>? monoProfiler;

        public static bool Enabled => enabled?.Value ?? false;

        public static ConfigEntry<bool>? EnabledEntry => enabled;

        public static ConfigEntry<int>? TargetFpsEntry => targetFps;

        public static ConfigEntry<bool>? MonoProfilerEntry => monoProfiler;

        public static int TargetFps
        {
            get
            {
                int value = targetFps?.Value ?? 60;
                if (value < 30)
                {
                    return 30;
                }

                return value > 240 ? 240 : value;
            }
        }

        public static void Bind(ConfigFile config)
        {
#if DEBUG
            bool profilerSettingExists = ProfilerToggle.ConfigHasEnableSetting(config.ConfigFilePath);
            monoProfiler = config.Bind(
                "Profiler",
                "Enable Mono Profiler",
                ProfilerToggle.IsLoaderInstalledInPatchers(),
                MonoProfilerDescription);
            if (profilerSettingExists)
            {
                ProfilerToggle.Apply(monoProfiler.Value);
            }

            monoProfiler.SettingChanged += (_, __) => ProfilerToggle.Apply(monoProfiler.Value);
#else
            monoProfiler = config.Bind(
                "Profiler",
                "Enable Mono Profiler",
                false,
                MonoProfilerDescription);
#endif

            enabled = config.Bind(
                "Profiler",
                "Hitch Watch",
                false,
                "Records frame time without the Mono profiler. Reads Unity's own counters every frame and writes the slow tail (1% and 0.1% of CPU and render time) plus any 10 second stretch under the recent median or the target FPS. Applies immediately.");

            targetFps = config.Bind(
                "Profiler",
                "Hitch Watch Target FPS",
                60,
                "Playable frame rate. Hitch Watch marks a stretch when its average FPS stays under this, or under the recent median. 60 is a frame every 16.7 ms.");
        }
    }

    internal static class HitchWatchStartup
    {
        // Called from RORO.Awake after PatchAll. A postfix on Awake never runs for that call,
        // because the patch is installed only once Awake is already on the stack.
        public static void Register(RORO plugin)
        {
            HitchWatchSettings.Bind(plugin.Config);
            RiskOfOptionsIntegration.Hold(new RiskOfOptionsCatalog
            {
                EnableHitchWatch = HitchWatchSettings.EnabledEntry,
                HitchWatchTargetFps = HitchWatchSettings.TargetFpsEntry,
                EnableMonoProfiler = HitchWatchSettings.MonoProfilerEntry,
            });
            plugin.gameObject.AddComponent<ProfilerDumpNotice>();
            plugin.gameObject.AddComponent<HitchWatch>();
        }
    }
}
