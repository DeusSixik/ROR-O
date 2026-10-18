#if DEBUG
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using BepInEx;

namespace ROR_O
{
    internal static class ProfilerToggle
    {
        private const string LoaderName = "MonoProfilerLoader.dll";
        private const string ControllerName = "MonoProfilerController.dll";

        public static bool ConfigHasEnableSetting(string configPath)
        {
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            {
                return false;
            }

            try
            {
                return File.ReadAllText(configPath).Contains("Enable Mono Profiler =");
            }
            catch (IOException)
            {
                return false;
            }
        }

        public static bool IsLoaderInstalledInPatchers()
        {
            return File.Exists(ActiveLoaderPath());
        }

        public static void Apply(bool enable)
        {
            string disabledDirectory = DisabledDirectory();
            string activeLoader = ActiveLoaderPath();
            string activeController = ActiveControllerPath();
            string disabledLoader = Path.Combine(disabledDirectory, LoaderName);
            string disabledController = Path.Combine(disabledDirectory, ControllerName);
            bool anyInstalled = File.Exists(activeLoader)
                || File.Exists(activeController)
                || File.Exists(disabledLoader)
                || File.Exists(disabledController);
            if (!anyInstalled)
            {
                if (enable)
                {
                    RORO.GlobalLogger?.LogInfo("Mono Profiler is not installed in this profile. ROR-O keeps running without it.");
                }

                return;
            }

            bool moved = enable
                ? MoveIntoPlace(disabledLoader, activeLoader) && MoveIntoPlace(disabledController, activeController)
                : MoveIntoPlace(activeLoader, disabledLoader) && MoveIntoPlace(activeController, disabledController);
            if (moved)
            {
                RORO.GlobalLogger?.LogInfo(enable
                    ? "Mono Profiler will load on the next launch."
                    : "Mono Profiler will stay unloaded on the next launch. This session keeps profiling until you restart.");
                return;
            }

            RORO.GlobalLogger?.LogWarning("Mono Profiler files are in use. The switch will be applied after the game closes.");
            ScheduleMoveAfterExit(enable, activeLoader, activeController, disabledLoader, disabledController);
        }

        private static string ActiveLoaderPath()
        {
            return Path.Combine(Paths.PatcherPluginPath, LoaderName);
        }

        private static string ActiveControllerPath()
        {
            return Path.Combine(Paths.PluginPath, ControllerName);
        }

        private static string DisabledDirectory()
        {
            return Path.Combine(Paths.BepInExRootPath, "disabled", "MonoProfiler");
        }

        private static bool MoveIntoPlace(string source, string destination)
        {
            if (!File.Exists(source))
            {
                return true;
            }

            try
            {
                string? directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(source, destination);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                RORO.GlobalLogger?.LogWarning($"Could not move {source} yet: {exception.Message}");
                return false;
            }
        }

        private static void ScheduleMoveAfterExit(
            bool enable,
            string activeLoader,
            string activeController,
            string disabledLoader,
            string disabledController)
        {
            string directory = DisabledDirectory();
            Directory.CreateDirectory(directory);
            string scriptPath = Path.Combine(directory, "apply-profiler-toggle.cmd");
            int processId = Process.GetCurrentProcess().Id;
            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine(":wait");
            script.AppendLine($"tasklist /FI \"PID eq {processId}\" | find \"{processId}\" >nul");
            script.AppendLine("if not errorlevel 1 (");
            script.AppendLine("  ping -n 2 127.0.0.1 >nul");
            script.AppendLine("  goto wait");
            script.AppendLine(")");
            if (enable)
            {
                AppendMove(script, disabledLoader, activeLoader);
                AppendMove(script, disabledController, activeController);
            }
            else
            {
                AppendMove(script, activeLoader, disabledLoader);
                AppendMove(script, activeController, disabledController);
            }

            script.AppendLine($"del \"{scriptPath}\"");
            File.WriteAllText(scriptPath, script.ToString(), Encoding.ASCII);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + scriptPath + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private static void AppendMove(StringBuilder script, string source, string destination)
        {
            string? directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                script.AppendLine($"if not exist \"{directory}\" mkdir \"{directory}\"");
            }

            script.AppendLine($"if exist \"{source}\" move /Y \"{source}\" \"{destination}\" >nul");
        }
    }
}
#endif
