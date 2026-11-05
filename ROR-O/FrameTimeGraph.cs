using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ROR_O
{
    // One bar per completed frame. Blue means the main thread finished inside the refresh and then
    // waited for the OS to flip the back buffer, or waited longer than it worked. That wait is
    // TimeUpdate.WaitForLastPresentationAndUpdateTime, the gap after PostLateUpdate. The other
    // bars are green at the display refresh and red once the frame is three refreshes late.
    internal sealed class FrameTimeGraph : MonoBehaviour
    {
        private const int Capacity = 160;
        private const float BarWidth = 3f;
        private const float BarGap = 1f;
        private const float PlotHeight = 86f;
        private const float PanelPad = 8f;
        private const float HeaderHeight = 20f;
        private const float LegendHeight = 16f;
        private const float WaitFloorMs = 1f;
        private const float ScaleCapMs = 100f;

        private static readonly Color Cool = new Color(0.20f, 0.78f, 0.34f, 1f);
        private static readonly Color Warm = new Color(0.96f, 0.78f, 0.16f, 1f);
        private static readonly Color Hot = new Color(0.92f, 0.22f, 0.16f, 1f);
        private static readonly Color GpuBound = new Color(0.25f, 0.52f, 1f, 1f);
        private static readonly Color Panel = new Color(0f, 0f, 0f, 0.62f);
        private static readonly Color Plot = new Color(0.05f, 0.06f, 0.07f, 0.9f);
        private static readonly Color Line = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Sample[] samples = new Sample[Capacity];
        private static readonly float[] order = new float[Capacity];
        private static readonly StringBuilder headerBuilder = new StringBuilder(64);

        private static ConfigEntry<bool>? show;
        private static Texture2D? pixel;
        private static GUIStyle? headerStyle;
        private static GUIStyle? legendStyle;
        private static GUIStyle? scaleStyle;
        private static bool visible;
        private static bool hooked;
        private static bool hookLogged;
        private static int hookAttempts;
        private static double presentStamp;
        private static int write;
        private static int count;
        private static int refreshHz = 60;
        private static int statsFrame = -1;
        private static int headerFrame = -1;
        private static int shownHz = -1;
        private static int shownScaleMs = -1;
        private static float statLatest;
        private static float statAvg;
        private static float statP99;
        private static float statPeak;
        private static bool statGpu;
        private static string headerText = "";
        private static string refreshText = "60 Hz";
        private static string scaleText = "";

        internal static ConfigEntry<bool>? ShowEntry => show;

        internal static void Bind(ConfigFile config)
        {
            show = config.Bind(
                WarmPaceSettings.Section,
                "Show Frame Time",
                false,
                OptionCopy.FrameGraph);
        }

        private void Update()
        {
            visible = show?.Value == true;
            if (!visible)
            {
                return;
            }

            int hz = Screen.currentResolution.refreshRate;
            refreshHz = hz < 30 ? 60 : hz;
            if (!hooked && hookAttempts < 8)
            {
                hookAttempts++;
                hooked = Install();
            }
        }

        private void OnDestroy()
        {
            visible = false;
            if (!hooked)
            {
                return;
            }

            Remove();
            hooked = false;
        }

        private void OnGUI()
        {
            if (show?.Value != true || Event.current.type != EventType.Repaint)
            {
                return;
            }

            EnsureStats();
            BuildHeader();
            EnsureStyles();

            float plotWidth = Capacity * (BarWidth + BarGap) - BarGap;
            float panelWidth = plotWidth + PanelPad * 2f;
            float panelHeight = PanelPad + HeaderHeight + PlotHeight + LegendHeight + PanelPad;
            float x = Screen.width - panelWidth - 16f;
            float y = 16f + LoadingProgressView.BlockHeight;
            if (EffectRuntime.ShowShaderStatus)
            {
                y += EffectRuntime.ShaderStatusBlockHeight + 8f;
            }

            Rect panel = new Rect(x, y, panelWidth, panelHeight);
            Fill(panel, Panel);
            GUI.Label(new Rect(panel.x + PanelPad, panel.y + 4f, plotWidth, HeaderHeight), headerText, headerStyle);

            Rect plot = new Rect(panel.x + PanelPad, panel.y + PanelPad + HeaderHeight, plotWidth, PlotHeight);
            Fill(plot, Plot);
            float refreshMs = 1000f / refreshHz;
            float scale = statPeak > refreshMs * 2f ? statPeak : refreshMs * 2f;
            if (count > 0)
            {
                DrawBars(plot, scale);
            }

            float lineY = plot.yMax - plot.height * (refreshMs / scale);
            Fill(new Rect(plot.x, lineY, plot.width, 1f), Line);
            RememberRefresh(refreshHz);
            RememberScale(scale);
            GUI.Label(new Rect(plot.xMax - 64f, lineY - 14f, 62f, 14f), refreshText, scaleStyle);
            GUI.Label(new Rect(plot.x, plot.y, 64f, 14f), scaleText, legendStyle);

            float legendY = plot.yMax + 4f;
            Fill(new Rect(panel.x + PanelPad, legendY + 2f, 10f, 10f), GpuBound);
            GUI.Label(new Rect(panel.x + PanelPad + 14f, legendY - 2f, 48f, LegendHeight), "GPU", legendStyle);
        }

        private static void DrawBars(Rect plot, float scale)
        {
            int n = count;
            float stride = BarWidth + BarGap;
            float x = plot.xMax - n * stride + BarGap;
            int begin = n == Capacity ? write : 0;
            float refreshMs = 1000f / refreshHz;
            for (int i = 0; i < n; i++)
            {
                Sample sample = samples[(begin + i) % Capacity];
                float shown = sample.Ms > ScaleCapMs ? ScaleCapMs : sample.Ms;
                float height = plot.height * (shown / scale);
                if (height < 1f)
                {
                    height = 1f;
                }
                else if (height > plot.height)
                {
                    height = plot.height;
                }

                Fill(new Rect(x, plot.yMax - height, BarWidth, height), BarColor(sample.Ms, refreshMs, sample.Gpu));
                x += stride;
            }
        }

        private static void EnsureStats()
        {
            if (statsFrame == Time.frameCount)
            {
                return;
            }

            statsFrame = Time.frameCount;
            int n = count;
            if (n <= 0)
            {
                statLatest = 0f;
                statAvg = 0f;
                statP99 = 0f;
                statPeak = 0f;
                statGpu = false;
                return;
            }

            int begin = n == Capacity ? write : 0;
            float sum = 0f;
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float ms = samples[(begin + i) % Capacity].Ms;
                order[i] = ms;
                sum += ms;
                float capped = ms > ScaleCapMs ? ScaleCapMs : ms;
                if (capped > peak)
                {
                    peak = capped;
                }
            }

            Array.Sort(order, 0, n);
            int tail = (int)Math.Ceiling(n * 0.99d) - 1;
            if (tail < 0)
            {
                tail = 0;
            }
            else if (tail >= n)
            {
                tail = n - 1;
            }

            int latest = (write + Capacity - 1) % Capacity;
            statLatest = samples[latest].Ms;
            statGpu = samples[latest].Gpu;
            statAvg = sum / n;
            statP99 = order[tail];
            statPeak = peak;
        }

        private static void BuildHeader()
        {
            if (headerFrame == Time.frameCount)
            {
                return;
            }

            headerFrame = Time.frameCount;
            int fps = statLatest > 0.05f ? (int)(1000f / statLatest + 0.5f) : 0;
            headerBuilder.Clear();
            headerBuilder.Append(fps);
            headerBuilder.Append(" fps   ");
            AppendTenths(statLatest);
            headerBuilder.Append(" ms   avg ");
            AppendTenths(statAvg);
            headerBuilder.Append("   1% ");
            AppendTenths(statP99);
            headerText = headerBuilder.ToString();
        }

        private static void AppendTenths(float value)
        {
            if (value < 0f)
            {
                value = 0f;
            }

            int scaled = (int)(value * 10f + 0.5f);
            headerBuilder.Append(scaled / 10);
            headerBuilder.Append('.');
            headerBuilder.Append(scaled % 10);
        }

        private static void RememberRefresh(int hz)
        {
            if (hz == shownHz)
            {
                return;
            }

            shownHz = hz;
            refreshText = hz.ToString() + " Hz";
        }

        private static void RememberScale(float scale)
        {
            int ms = (int)(scale + 0.5f);
            if (ms == shownScaleMs)
            {
                return;
            }

            shownScaleMs = ms;
            scaleText = ms.ToString() + " ms";
        }

        private static Color BarColor(float frameMs, float refreshMs, bool gpu)
        {
            if (gpu)
            {
                return GpuBound;
            }

            float span = refreshMs > 0.01f ? refreshMs * 2f : 1f;
            float late = (frameMs - refreshMs) / span;
            if (late <= 0f)
            {
                return Cool;
            }

            if (late >= 1f)
            {
                return Hot;
            }

            if (late < 0.5f)
            {
                return Color.Lerp(Cool, Warm, late * 2f);
            }

            return Color.Lerp(Warm, Hot, (late - 0.5f) * 2f);
        }

        private static void OnPresentEnd()
        {
            presentStamp = Time.realtimeSinceStartupAsDouble;
        }

        private static void OnFrameBegin()
        {
            if (!visible || presentStamp <= 0d)
            {
                return;
            }

            float wait = (float)((Time.realtimeSinceStartupAsDouble - presentStamp) * 1000d);
            float frame = Time.unscaledDeltaTime * 1000f;
            if (frame < 0.05f || frame > 5000f)
            {
                return;
            }

            if (wait < 0f)
            {
                wait = 0f;
            }
            else if (wait > frame)
            {
                wait = frame;
            }

            float cpu = frame - wait;
            float refresh = 1000f / refreshHz;
            samples[write].Ms = frame;
            samples[write].Gpu = wait >= WaitFloorMs && (cpu <= refresh || wait >= cpu);
            write++;
            if (write == Capacity)
            {
                write = 0;
            }

            if (count < Capacity)
            {
                count++;
            }
        }

        private static bool Install()
        {
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[]? stages = root.subSystemList;
            if (stages == null)
            {
                LogHookOnce("Frame time graph found no player loop.");
                return false;
            }

            bool early = false;
            bool post = false;
            for (int i = 0; i < stages.Length; i++)
            {
                Type? stageType = stages[i].type;
                if (stageType == typeof(EarlyUpdate))
                {
                    stages[i] = Insert(stages[i], typeof(FrameTimeFrameBegin), OnFrameBegin, false);
                    early = true;
                }
                else if (stageType == typeof(PostLateUpdate))
                {
                    stages[i] = Insert(stages[i], typeof(FrameTimePresentEnd), OnPresentEnd, true);
                    post = true;
                }
            }

            if (!early || !post)
            {
                LogHookOnce("Frame time graph could not find EarlyUpdate and PostLateUpdate.");
                return false;
            }

            root.subSystemList = stages;
            PlayerLoop.SetPlayerLoop(root);
            return true;
        }

        private static void Remove()
        {
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[]? stages = root.subSystemList;
            if (stages == null)
            {
                return;
            }

            for (int i = 0; i < stages.Length; i++)
            {
                stages[i] = Strip(stages[i]);
            }

            root.subSystemList = stages;
            PlayerLoop.SetPlayerLoop(root);
        }

        private static PlayerLoopSystem Insert(PlayerLoopSystem stage, Type marker, PlayerLoopSystem.UpdateFunction action, bool atEnd)
        {
            PlayerLoopSystem[]? list = stage.subSystemList;
            if (list != null)
            {
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i].type == marker)
                    {
                        return stage;
                    }
                }
            }

            var systems = new List<PlayerLoopSystem>(list == null ? 1 : list.Length + 1);
            if (list != null)
            {
                systems.AddRange(list);
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

        private static PlayerLoopSystem Strip(PlayerLoopSystem node)
        {
            PlayerLoopSystem[]? list = node.subSystemList;
            if (list == null)
            {
                return node;
            }

            var systems = new List<PlayerLoopSystem>(list.Length);
            for (int i = 0; i < list.Length; i++)
            {
                if (IsMarker(list[i].type))
                {
                    continue;
                }

                systems.Add(Strip(list[i]));
            }

            node.subSystemList = systems.ToArray();
            return node;
        }

        private static bool IsMarker(Type? type)
        {
            return type == typeof(FrameTimeFrameBegin) || type == typeof(FrameTimePresentEnd);
        }

        private static void LogHookOnce(string message)
        {
            if (hookLogged)
            {
                return;
            }

            hookLogged = true;
            RORO.GlobalLogger?.LogWarning(message);
        }

        private static void EnsureStyles()
        {
            if (headerStyle != null)
            {
                return;
            }

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                richText = false
            };
            headerStyle.normal.textColor = Color.white;
            legendStyle = new GUIStyle(headerStyle)
            {
                fontSize = 12
            };
            legendStyle.normal.textColor = new Color(0.82f, 0.86f, 0.9f, 1f);
            scaleStyle = new GUIStyle(legendStyle)
            {
                alignment = TextAnchor.MiddleRight
            };
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Pixel(), ScaleMode.StretchToFill, false);
            GUI.color = previous;
        }

        private static Texture2D Pixel()
        {
            if (pixel != null)
            {
                return pixel;
            }

            pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply(false, true);
            return pixel;
        }

        private struct Sample
        {
            public float Ms;
            public bool Gpu;
        }

        private struct FrameTimeFrameBegin
        {
        }

        private struct FrameTimePresentEnd
        {
        }
    }
}
