using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace ROR_O
{
    internal sealed class ProfilerDumpNotice : MonoBehaviour
    {
        private const float FailureDelaySeconds = 0.75f;
        private const float VisibleSeconds = 5f;
        // Two seconds is soon enough for a saved-dump notice; polling the game directory
        // twice a second showed up in the profiler it reports on.
        private const float IdlePollSeconds = 2f;

        private readonly HashSet<string> knownDumps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private ConfigEntry<KeyboardShortcut>? dumpKey;
        private bool lookedForDumpKey;
        private float previousDumpTime = float.NaN;
        private float pendingPressTime = float.NaN;
        private float nextIdlePoll;
        private string message = string.Empty;
        private bool messageIsFailure;
        private float hideAt;
        private GUIStyle? style;

        private void Awake()
        {
            RememberExistingDumps();
            previousDumpTime = Time.unscaledTime;
        }

        private void Update()
        {
            if (IsDumpKeyDown())
            {
                pendingPressTime = Time.unscaledTime;
            }

            bool waitingForDump = !float.IsNaN(pendingPressTime);
            if (!waitingForDump && Time.unscaledTime < nextIdlePoll)
            {
                return;
            }

            nextIdlePoll = Time.unscaledTime + IdlePollSeconds;
            if (NoticeNewDumps())
            {
                pendingPressTime = float.NaN;
                return;
            }

            if (waitingForDump && Time.unscaledTime - pendingPressTime >= FailureDelaySeconds)
            {
                Show("Клавиша профайлера нажата, файл не создан", true);
                RORO.GlobalLogger?.LogWarning("Profiler dump key was pressed, but no MonoProfilerOutput file appeared.");
                pendingPressTime = float.NaN;
            }
        }

        private void OnGUI()
        {
            if (message.Length == 0 || Time.unscaledTime > hideAt)
            {
                return;
            }

            style ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = 26,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            style.normal.textColor = Color.white;
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = messageIsFailure
                ? new Color(0.55f, 0.12f, 0.1f, 0.94f)
                : new Color(0.08f, 0.38f, 0.16f, 0.94f);
            GUI.Box(new Rect(0f, 20f, Screen.width, 96f), message, style);
            GUI.backgroundColor = previous;
        }

        private bool NoticeNewDumps()
        {
            string? directory = Paths.GameRootPath;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            bool found = false;
            foreach (string path in Directory.GetFiles(directory, "MonoProfilerOutput_*.csv"))
            {
                if (!knownDumps.Add(path))
                {
                    continue;
                }

                float now = Time.unscaledTime;
                float elapsed = float.IsNaN(previousDumpTime) ? now : now - previousDumpTime;
                previousDumpTime = now;
                FileInfo info = new FileInfo(path);
                string text = $"Профайлер сохранён: {info.Name}\nОкно: {FormatDuration(elapsed)}, {info.Length / 1024} КБ";
                Show(text, false);
                RORO.GlobalLogger?.LogInfo(text.Replace('\n', ' '));
                found = true;
            }

            return found;
        }

        private void RememberExistingDumps()
        {
            string? directory = Paths.GameRootPath;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            foreach (string path in Directory.GetFiles(directory, "MonoProfilerOutput_*.csv"))
            {
                knownDumps.Add(path);
            }
        }

        private bool IsDumpKeyDown()
        {
            if (!lookedForDumpKey)
            {
                dumpKey = FindDumpKey();
                lookedForDumpKey = dumpKey != null || Chainloader.PluginInfos.ContainsKey("MonoProfiler");
            }

            return dumpKey != null && dumpKey.Value.IsDown();
        }

        private static ConfigEntry<KeyboardShortcut>? FindDumpKey()
        {
            if (!Chainloader.PluginInfos.TryGetValue("MonoProfiler", out PluginInfo? plugin) || plugin.Instance == null)
            {
                return null;
            }

            FieldInfo? field = plugin.Instance.GetType().GetField("_key", BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(plugin.Instance) as ConfigEntry<KeyboardShortcut>;
        }

        private void Show(string text, bool failure)
        {
            message = text;
            messageIsFailure = failure;
            hideAt = Time.unscaledTime + VisibleSeconds;
        }

        private static string FormatDuration(float seconds)
        {
            if (seconds < 0f)
            {
                seconds = 0f;
            }

            if (seconds < 90f)
            {
                return $"{seconds:0} с";
            }

            int minutes = (int)(seconds / 60f);
            int rest = (int)(seconds % 60f);
            return $"{minutes} мин {rest:00} с";
        }
    }
}
