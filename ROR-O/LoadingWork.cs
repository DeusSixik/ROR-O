using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;

namespace ROR_O
{
    // Shared view of the between-stage loading window and a place for preload producers to
    // publish plain counts. The window is the network scene load: FadeToBlack is up while a
    // peer is still loading or not ready, the run stopwatch is paused (no living players),
    // and a slow client does not hold the host's stage start. Only asset loads and global
    // caches survive scene activation; anything instantiated in the old scene is destroyed
    // when the new scene activates, and effect pools are killed on scene unload regardless.
    // Producers therefore load assets in the window and build copies after activation.
    internal static class LoadingPhase
    {
        // Generous per-frame budget for the window, not an unbounded burst: the loading
        // animation and network updates still need frames from the main thread.
        internal const float FrameBudgetMs = 10f;

        internal static bool IsLoading => NetworkManager.IsSceneLoadingAsynchronously;

        // The splash scene is the boot screen before the title menu. Pinning there stalled
        // the launch for seconds. The intro cutscene sits between that and the title: skipping
        // it changes the scene while a compile is on the main thread, and the game hangs.
        // The lobby is where a run is assembled, and the character select is that same scene.
        internal static bool IsStartupSplash(string sceneName)
        {
            return sceneName.Equals("splash", System.StringComparison.OrdinalIgnoreCase);
        }

        internal static bool BlocksWarmup(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return true;
            }

            if (IsStartupSplash(sceneName) || sceneName.StartsWith("loading", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return sceneName.Equals("intro", System.StringComparison.OrdinalIgnoreCase)
                || sceneName.Equals("title", System.StringComparison.OrdinalIgnoreCase);
        }

        internal static bool BudgetExpired(long startedTimestamp, int processed)
        {
            if (processed <= 0)
            {
                return false;
            }

            double elapsedMs = (Stopwatch.GetTimestamp() - startedTimestamp) * 1000d / Stopwatch.Frequency;
            return elapsedMs >= FrameBudgetMs;
        }
    }

    // One entry per preload kind, written by the producer that owns the numbers and read by
    // whatever draws progress. Producers only publish counts; no UI lives here, so progress
    // rings can render these entries later without touching the preload code.
    internal sealed class LoadingProgressEntry
    {
        internal string Label = "";
        internal int Done;
        internal int Total;
        internal bool Active;

        internal float Fraction => Total > 0 ? (float)Done / Total : 0f;
    }

    internal static class LoadingProgress
    {
        private static readonly Dictionary<string, LoadingProgressEntry> ByLabel = new Dictionary<string, LoadingProgressEntry>();
        private static readonly List<LoadingProgressEntry> entries = new List<LoadingProgressEntry>();

        internal static IReadOnlyList<LoadingProgressEntry> Entries => entries;

        internal static void Set(string label, int done, int total, bool active)
        {
            if (!ByLabel.TryGetValue(label, out LoadingProgressEntry? entry) || entry == null)
            {
                entry = new LoadingProgressEntry { Label = label };
                ByLabel[label] = entry;
                entries.Add(entry);
            }

            entry.Done = done;
            entry.Total = total;
            entry.Active = active;
        }
    }

    // One line per preload: ring, percent, name. A line eases in while its producer is
    // working and, once that work stops, holds the percent it reached and eases back out.
    // The block height follows the same curve, so the debug readouts below it move with it.
    internal sealed class LoadingProgressView : MonoBehaviour
    {
        private const float RowHeight = 28f;
        private const float RingSize = 22f;
        private const float AppearSeconds = 0.18f;
        private const float FadeSeconds = 0.42f;
        private const float FractionSeconds = 0.14f;

        private static readonly string[] Percents = BuildPercents();
        private static readonly List<RowFade> fades = new List<RowFade>();
        private static Texture2D?[] rings = new Texture2D?[51];
        private static int ringPixels;
        private static int motionFrame = -1;

        private static GUIStyle? labelStyle;

        internal static float BlockHeight { get; private set; }

        private void OnGUI()
        {
            IReadOnlyList<LoadingProgressEntry> entries = LoadingProgress.Entries;
            Advance(entries);

            float height = 0f;
            float open = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                RowFade row = fades[i];
                if (row.Presence <= 0.004f)
                {
                    continue;
                }

                height += RowHeight * row.Presence;
                if (row.Presence > open)
                {
                    open = row.Presence;
                }
            }

            BlockHeight = height <= 0.01f ? 0f : 8f * open + height;
            if (height <= 0.01f || Event.current.type != EventType.Repaint)
            {
                return;
            }

            long started = HitchProbes.Armed ? Stopwatch.GetTimestamp() : 0L;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15,
                    alignment = TextAnchor.MiddleLeft
                };
                labelStyle.normal.textColor = Color.white;
            }

            Color previous = GUI.color;
            float y = 12f;
            for (int i = 0; i < entries.Count; i++)
            {
                RowFade row = fades[i];
                if (row.Presence <= 0.004f)
                {
                    continue;
                }

                // The slot under the row still grows and shrinks with the row, so the debug
                // readouts keep sliding. The row itself is not clipped to that slot.
                float slot = RowHeight * row.Presence;
                float settle = (1f - row.Presence) * 8f - row.Roll * 14f;
                GUI.color = new Color(1f, 1f, 1f, row.Alpha);
                Rect ring = RingRect(new Vector2(16f, y + 2f - settle));
                GUI.DrawTexture(ring, RingTexture(row.Shown, ringPixels), ScaleMode.StretchToFill, true);

                int percent = (int)(row.Shown * 100f + 0.5f);
                if (percent < 0)
                {
                    percent = 0;
                }
                else if (percent > 100)
                {
                    percent = 100;
                }

                GUI.Label(new Rect(44f, y - settle, 56f, 26f), Percents[percent], labelStyle);
                GUI.Label(new Rect(104f, y - settle, 520f, 26f), row.Label, labelStyle);
                y += slot;
            }

            GUI.color = previous;
            HitchProbes.Note(HitchProbes.Id.ProgressRings, started);
        }

        // Exponential approach is reversible: a line that has started fading and whose producer
        // reports work again (a prefab whose count fell) turns around from wherever it is.
        private static void Advance(IReadOnlyList<LoadingProgressEntry> entries)
        {
            if (Time.frameCount == motionFrame)
            {
                return;
            }

            motionFrame = Time.frameCount;
            float dt = Time.unscaledDeltaTime;
            if (dt < 0f)
            {
                dt = 0f;
            }
            else if (dt > 0.1f)
            {
                dt = 0.1f;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                LoadingProgressEntry entry = entries[i];
                bool live = entry.Active && entry.Total > 0;
                float fraction = entry.Fraction;
                if (fraction < 0f)
                {
                    fraction = 0f;
                }
                else if (fraction > 1f)
                {
                    fraction = 1f;
                }

                RowFade row;
                if (i < fades.Count && fades[i].Label == entry.Label)
                {
                    row = fades[i];
                }
                else
                {
                    // Start on the real percent. Only the row itself eases in; the number
                    // must not count up from zero while the line is appearing.
                    row = new RowFade { Label = entry.Label, Shown = fraction };
                    fades.Insert(i, row);
                }

                if (live || fraction < 0.999f)
                {
                    // Still working, or the producer paused before the end. Either way the
                    // row stays. A rollback during the exit turns the row back from here.
                    row.Leaving = false;
                    row.HoldLeft = 0f;
                    row.Shown = Approach(row.Shown, fraction, dt, FractionSeconds);
                    row.Presence = Approach(row.Presence, 1f, dt, AppearSeconds);
                    row.Alpha = Approach(row.Alpha, 1f, dt, AppearSeconds);
                    row.Roll = Approach(row.Roll, 0f, dt, AppearSeconds);
                }
                else
                {
                    // Finished. The number was easing and may still be short of the real
                    // percent, so snap it: the row must not leave while it reads 46%.
                    row.Shown = 1f;
                    if (!row.Leaving)
                    {
                        row.Leaving = true;
                        row.HoldLeft = 0.35f;
                    }

                    if (row.HoldLeft > 0f)
                    {
                        row.HoldLeft -= dt;
                        row.Presence = 1f;
                        row.Roll = 0f;
                        row.Alpha = Approach(row.Alpha, 0.65f, dt, 0.35f);
                    }
                    else
                    {
                        row.Alpha = Approach(row.Alpha, 0f, dt, FadeSeconds);
                        row.Roll = Approach(row.Roll, 1f, dt, FadeSeconds);
                        row.Presence = Approach(row.Presence, 0f, dt, FadeSeconds);
                    }
                }

                if (row.Leaving && row.Presence < 0.004f && row.Alpha < 0.004f)
                {
                    row.Presence = 0f;
                    row.Alpha = 0f;
                }
                else if (!row.Leaving && row.Presence > 0.996f)
                {
                    row.Presence = 1f;
                    row.Alpha = 1f;
                }
            }
        }

        private static float Approach(float current, float target, float dt, float seconds)
        {
            if (seconds <= 0f)
            {
                return target;
            }

            float blend = 1f - Mathf.Exp(-dt / seconds);
            return current + (target - current) * blend;
        }

        private sealed class RowFade
        {
            internal string Label = "";
            internal float Presence;
            internal float Alpha;
            internal float Roll;
            internal float Shown;
            internal float HoldLeft;
            internal bool Leaving;
        }

        // One GUI unit, measured in screen pixels, including the window scale. The ring is
        // rasterized at exactly that many pixels, so nothing scales the bitmap afterwards.
        private static Rect RingRect(Vector2 guiPosition)
        {
            Vector2 origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
            Vector2 unit = GUIUtility.GUIToScreenPoint(Vector2.right) - origin;
            float screenPerGui = unit.magnitude;
            if (screenPerGui < 0.01f)
            {
                screenPerGui = 1f;
            }

            int pixels = Mathf.RoundToInt(RingSize * screenPerGui);
            if (pixels < 16)
            {
                pixels = 16;
            }
            else if (pixels > 512)
            {
                pixels = 512;
            }

            if (pixels != ringPixels)
            {
                for (int i = 0; i < rings.Length; i++)
                {
                    if (rings[i] != null)
                    {
                        UnityEngine.Object.Destroy(rings[i]);
                        rings[i] = null;
                    }
                }

                ringPixels = pixels;
            }

            Vector2 screen = GUIUtility.GUIToScreenPoint(guiPosition);
            var snapped = new Vector2(Mathf.Round(screen.x), Mathf.Round(screen.y));
            Vector2 aligned = GUIUtility.ScreenToGUIPoint(snapped);
            float guiSize = pixels / screenPerGui;
            return new Rect(aligned.x, aligned.y, guiSize, guiSize);
        }

        private static Texture2D RingTexture(float fraction, int size)
        {
            int step = (int)(fraction * 50f + 0.5f);
            if (step < 0)
            {
                step = 0;
            }
            else if (step > 50)
            {
                step = 50;
            }

            Texture2D? cached = rings[step];
            if (cached != null)
            {
                return cached;
            }

            // Point sampling: each texel is one screen pixel. Bilinear would blur the coverage
            // that the samples below already computed, which is the grayscale edge a font draws.
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            var pixels = new Color32[size * size];
            float limit = step / 50f * Mathf.PI * 2f;
            float center = size * 0.5f;
            // Stroke widths snap to whole pixels, the way a hinter snaps a stem to the pixel grid.
            int outer = Mathf.RoundToInt(size * (14.5f / 32f));
            int stroke = Mathf.RoundToInt(size * (6f / 32f));
            if (stroke < 2)
            {
                stroke = 2;
            }

            int inner = outer - stroke;
            if (inner < 1)
            {
                inner = 1;
            }

            float outerSqr = outer * outer;
            float innerSqr = inner * inner;
            var track = new Color(1f, 1f, 1f, 56f / 255f);
            var fill = new Color(140f / 255f, 220f / 255f, 1f, 235f / 255f);
            const int samples = 4;
            float sampleStep = 1f / samples;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int fillCount = 0;
                    int trackCount = 0;
                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float dx = x + (sx + 0.5f) * sampleStep - center;
                            float dy = y + (sy + 0.5f) * sampleStep - center;
                            float distanceSqr = dx * dx + dy * dy;
                            if (distanceSqr > outerSqr || distanceSqr < innerSqr)
                            {
                                continue;
                            }

                            float angle = Mathf.Atan2(dx, dy);
                            if (angle < 0f)
                            {
                                angle += Mathf.PI * 2f;
                            }

                            if (angle <= limit)
                            {
                                fillCount++;
                            }
                            else
                            {
                                trackCount++;
                            }
                        }
                    }

                    int covered = fillCount + trackCount;
                    if (covered == 0)
                    {
                        continue;
                    }

                    Color color = ((fill * fillCount) + (track * trackCount)) / covered;
                    color.a *= covered / (float)(samples * samples);
                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            rings[step] = texture;
            return texture;
        }

        private static string[] BuildPercents()
        {
            var percents = new string[101];
            for (int i = 0; i < percents.Length; i++)
            {
                percents[i] = i.ToString() + "%";
            }

            return percents;
        }
    }
}
