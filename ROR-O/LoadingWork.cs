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

    // One line per active preload: ring, percent, name. Drawn only while something is
    // actually loading, from the counts producers already publish.
    internal sealed class LoadingProgressView : MonoBehaviour
    {
        private const float RowHeight = 28f;
        private const float RingSize = 22f;

        private static readonly string[] Percents = BuildPercents();
        private static readonly Texture2D?[] Rings = new Texture2D?[51];

        private static GUIStyle? labelStyle;

        internal static float BlockHeight { get; private set; }

        private void OnGUI()
        {
            IReadOnlyList<LoadingProgressEntry> entries = LoadingProgress.Entries;
            int visible = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                LoadingProgressEntry entry = entries[i];
                if (entry.Active && entry.Total > 0)
                {
                    visible++;
                }
            }

            BlockHeight = visible == 0 ? 0f : 8f + visible * RowHeight;
            if (visible == 0)
            {
                return;
            }

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15,
                    alignment = TextAnchor.MiddleLeft
                };
                labelStyle.normal.textColor = Color.white;
            }

            float y = 12f;
            for (int i = 0; i < entries.Count; i++)
            {
                LoadingProgressEntry entry = entries[i];
                if (!entry.Active || entry.Total <= 0)
                {
                    continue;
                }

                float fraction = entry.Fraction;
                if (fraction < 0f)
                {
                    fraction = 0f;
                }
                else if (fraction > 1f)
                {
                    fraction = 1f;
                }

                var ring = new Rect(16f, y + 2f, RingSize, RingSize);
                if (Event.current.type == EventType.Repaint)
                {
                    GUI.DrawTexture(ring, RingTexture(fraction));
                }

                int percent = (int)(fraction * 100f + 0.5f);
                if (percent > 100)
                {
                    percent = 100;
                }

                GUI.Label(new Rect(44f, y, 56f, 26f), Percents[percent], labelStyle);
                GUI.Label(new Rect(104f, y, 520f, 26f), entry.Label, labelStyle);
                y += RowHeight;
            }
        }

        private static Texture2D RingTexture(float fraction)
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

            Texture2D? cached = Rings[step];
            if (cached != null)
            {
                return cached;
            }

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            float limit = step / 50f * Mathf.PI * 2f;
            var track = new Color32(255, 255, 255, 56);
            var fill = new Color32(140, 220, 255, 235);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - 15.5f;
                    float dy = y - 15.5f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distance < 8.5f || distance > 14.5f)
                    {
                        continue;
                    }

                    float angle = Mathf.Atan2(dx, dy);
                    if (angle < 0f)
                    {
                        angle += Mathf.PI * 2f;
                    }

                    pixels[y * size + x] = angle <= limit ? fill : track;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Rings[step] = texture;
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
