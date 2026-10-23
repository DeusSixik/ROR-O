using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace ROR_O
{
    // The first spawn of a monster pays CharacterBody.PreloadAssets: animator, avatar, and skins.
    // The game already does that for the selected non-champion masters, all inside one network message.
    // This spreads the same call across frames. Champions are not in that message; the stage list adds them.
    // Instantiating a prefab and destroying it does not keep that load, and Awake runs again on the real spawn.
    internal sealed class SpawnPrewarm : MonoBehaviour
    {
        // Same gate EffectRuntime uses: a frame that already ran long does not also start a preload.
        private const float HitchFrameSeconds = 0.033f;

        private static readonly List<CharacterMaster> pending = new List<CharacterMaster>();
        private static readonly HashSet<CharacterMaster> queued = new HashSet<CharacterMaster>();
        private static readonly HashSet<CharacterMaster> done = new HashSet<CharacterMaster>();
        private static readonly StringBuilder statusBuilder = new StringBuilder(256);

        private static ConfigEntry<bool>? enable;
        private static ConfigEntry<int>? mastersPerFrame;
        private static ConfigEntry<bool>? warmBosses;
        private static ConfigEntry<bool>? skipLongFrames;
        private static ConfigEntry<bool>? showStatus;
        private static ConfigEntry<bool>? logStatus;
        private static float nextLogTime;

        private static int cursor;
        private static int warmedSinceLog;
        private static int stageListed;
        private static int stageQueued;
        private static int bossesListed;
        private static int bossesQueued;
        private static int networkListed;
        private static int networkQueued;
        private static int warmed;
        private static int missing;
        private static int longSkips;
        private static bool sceneHold;
        private static float lastMs;
        private static float maxMs;
        private static float totalMs;
        private static string lastName = "";
        private static string statusText = "Mob status is not ready yet.";
        private static GUIStyle? statusStyle;

        internal static ConfigEntry<SettingsProfiles.Profile>? Profile { get; private set; }
        internal static ConfigEntry<bool>? EnableEntry => enable;
        internal static ConfigEntry<int>? MastersPerFrameEntry => mastersPerFrame;
        internal static ConfigEntry<bool>? WarmBossesEntry => warmBosses;
        internal static ConfigEntry<bool>? SkipLongFramesEntry => skipLongFrames;
        internal static ConfigEntry<bool>? ShowStatusEntry => showStatus;
        internal static ConfigEntry<bool>? LogStatusEntry => logStatus;

        private static bool Enabled => enable?.Value ?? true;

        internal static void Bind(ConfigFile config)
        {
            const string section = "Spawns";

            enable = config.Bind(
                section,
                "Prewarm Stage Spawns",
                true,
                "Preloads the animator, avatar, and skins of monsters this stage can spawn, spread across frames. Uses the game's own master preload and can also include bosses from that list. Takes effect on the next stage.");

            mastersPerFrame = config.Bind(
                section,
                "Masters Per Frame",
                1,
                "How many monster masters to preload on one frame. Higher finishes the queue sooner and can hitch harder on that frame.");

            warmBosses = config.Bind(
                section,
                "Warm Stage Bosses",
                true,
                "Also preloads champion monsters from this stage's list. The game's own preload skips them. Takes effect on the next stage.");

            skipLongFrames = config.Bind(
                section,
                "Skip Long Frames",
                false,
                "Waits for another frame when the last one already took longer than 33 ms, so a preload is not stacked onto a hitch.");

            showStatus = config.Bind(
                section,
                "Show Mob Status",
                false,
                "Draws the live spawn-prewarm counts on screen.");

            logStatus = config.Bind(
                section,
                "Log Mob Status",
                false,
                "Writes the spawn-prewarm counts to the BepInEx log once a second while this is on.");

            Profile = SettingsProfiles.Begin(config, section);
            SettingsProfiles.Add(enable, true, true, true);
            SettingsProfiles.Add(mastersPerFrame, 1, 1, 2);
            SettingsProfiles.Add(warmBosses, true, true, true);
            SettingsProfiles.Add(skipLongFrames, true, false, false);
            SettingsProfiles.Add(showStatus, false, false, false);
            SettingsProfiles.Add(logStatus, false, false, false);
            SettingsProfiles.End();
        }

        private static void LogStatus()
        {
            RefreshStatus();
            RORO.GlobalLogger?.LogInfo(statusText.Replace('\n', ' '));
        }

        private static bool Enqueue(CharacterMaster master)
        {
            if (!master || done.Contains(master) || !queued.Add(master))
            {
                return false;
            }

            pending.Add(master);
            return true;
        }

        private void OnEnable()
        {
            Stage.onStageStartGlobal += OnStageStart;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            Stage.onStageStartGlobal -= OnStageStart;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ResetQueue();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
            {
                return;
            }

            // Fires before Start on the new scene, so the stage list and the network preload
            // message are still ahead of this. Drop the previous stage and preload the next one.
            ResetQueue();
        }

        private static void ResetQueue()
        {
            pending.Clear();
            queued.Clear();
            done.Clear();
            cursor = 0;
            warmedSinceLog = 0;
            stageListed = 0;
            stageQueued = 0;
            bossesListed = 0;
            bossesQueued = 0;
            networkListed = 0;
            networkQueued = 0;
            warmed = 0;
            missing = 0;
            longSkips = 0;
            sceneHold = false;
            lastMs = 0f;
            maxMs = 0f;
            totalMs = 0f;
            lastName = "";
        }

        private void OnStageStart(Stage stage)
        {
            if (!Enabled || stage == null)
            {
                return;
            }

            CollectSelectedMasters();
        }

        private void Update()
        {
            MaybeLogStatus();
            if (!Enabled || cursor >= pending.Count)
            {
                sceneHold = false;
                return;
            }

            if (NetworkManager.IsSceneLoadingAsynchronously)
            {
                sceneHold = true;
                return;
            }

            sceneHold = false;
            if (skipLongFrames?.Value == true && Time.unscaledDeltaTime > HitchFrameSeconds)
            {
                longSkips++;
                return;
            }

            int budget = Clamp(mastersPerFrame?.Value ?? 1, 1, 4);
            while (cursor < pending.Count && budget-- > 0)
            {
                WarmOne();
            }

            if (cursor < pending.Count)
            {
                return;
            }

            pending.Clear();
            cursor = 0;
            if (warmedSinceLog > 0)
            {
                RORO.GlobalLogger?.LogInfo($"Stage spawn prewarm finished: {warmedSinceLog} masters.");
                warmedSinceLog = 0;
            }
        }

        private static void MaybeLogStatus()
        {
            if (logStatus?.Value != true)
            {
                nextLogTime = 0f;
                return;
            }

            float now = Time.unscaledTime;
            if (nextLogTime != 0f && now < nextLogTime)
            {
                return;
            }

            nextLogTime = now + 1f;
            LogStatus();
        }

        private void OnGUI()
        {
            if (showStatus?.Value != true)
            {
                return;
            }

            if ((Time.frameCount & 15) == 0)
            {
                RefreshStatus();
            }

            if (statusStyle == null)
            {
                statusStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    wordWrap = false
                };
                statusStyle.normal.textColor = Color.white;
            }

            // The effect status occupies the top-left. Sit under it when both overlays are on.
            float top = EffectRuntime.ShowStatusEntry?.Value == true ? 120f : 16f;
            GUI.Label(new Rect(16f, top, 760f, 160f), statusText, statusStyle);
        }

        private static void WarmOne()
        {
            CharacterMaster master = pending[cursor];
            cursor++;
            queued.Remove(master);
            if (!master)
            {
                missing++;
                return;
            }

            long started = Stopwatch.GetTimestamp();
            done.Add(master);
            master.PreloadAssets();
            long elapsed = Stopwatch.GetTimestamp() - started;
            if (HitchProbes.Armed)
            {
                HitchProbes.Add(HitchProbes.Id.Prewarm, elapsed);
            }

            float ms = (float)(elapsed * 1000d / Stopwatch.Frequency);
            lastMs = ms;
            totalMs += ms;
            if (ms > maxMs)
            {
                maxMs = ms;
            }

            lastName = master.name;
            warmed++;
            warmedSinceLog++;
        }

        private static void CollectSelectedMasters()
        {
            ClassicStageInfo? info = ClassicStageInfo.instance;
            WeightedSelection<DirectorCard>? selection = info ? info.monsterSelection : null;
            if (selection == null)
            {
                return;
            }

            bool bosses = warmBosses?.Value ?? true;
            int count = selection.Count;
            for (int i = 0; i < count; i++)
            {
                DirectorCard? card = selection.choices[i].value;
                SpawnCard? spawnCard = card != null ? card.GetSpawnCard() : null;
                GameObject? prefab = spawnCard != null ? spawnCard.prefab : null;
                if (prefab == null || !prefab.TryGetComponent(out CharacterMaster? master) || master == null)
                {
                    continue;
                }

                stageListed++;
                if (IsChampion(master))
                {
                    bossesListed++;
                    if (bosses && Enqueue(master))
                    {
                        bossesQueued++;
                    }

                    continue;
                }

                if (Enqueue(master))
                {
                    stageQueued++;
                }
            }
        }

        // isChampion is the body flag the game's own master preload skips. On a prefab, isBoss is
        // false because that property reads the live master, which a prefab does not have.
        private static bool IsChampion(CharacterMaster master)
        {
            GameObject? bodyPrefab = master.bodyPrefab;
            if (!bodyPrefab || !bodyPrefab.TryGetComponent(out CharacterBody body))
            {
                return false;
            }

            return body.isChampion;
        }

        private static void RefreshStatus()
        {
            int left = pending.Count > cursor ? pending.Count - cursor : 0;
            int stageBodies = stageListed - bossesListed;
            if (stageBodies < 0)
            {
                stageBodies = 0;
            }

            statusBuilder.Clear();
            statusBuilder.Append(Enabled ? "Mobs on" : "Mobs off");
            statusBuilder.Append("  warmed ").Append(warmed);
            statusBuilder.Append("  left ").Append(left);
            statusBuilder.Append("  missing ").Append(missing);
            statusBuilder.Append("\nStage list ").Append(stageBodies).Append(" added ").Append(stageQueued);
            statusBuilder.Append("  bosses ").Append(bossesListed).Append(" added ").Append(bossesQueued);
            statusBuilder.Append("  network list ").Append(networkListed).Append(" added ").Append(networkQueued);
            statusBuilder.Append("\nPer frame ").Append(Clamp(mastersPerFrame?.Value ?? 1, 1, 4));
            statusBuilder.Append("  skip long ").Append(skipLongFrames?.Value == true ? "on" : "off");
            statusBuilder.Append("  long skips ").Append(longSkips);
            if (sceneHold)
            {
                statusBuilder.Append("  scene loading");
            }

            statusBuilder.Append("\nLast ");
            statusBuilder.Append(lastName.Length == 0 ? "none" : lastName);
            statusBuilder.Append(' ');
            AppendTenths(statusBuilder, lastMs);
            statusBuilder.Append(" ms  max ");
            AppendTenths(statusBuilder, maxMs);
            statusBuilder.Append("  avg ");
            AppendTenths(statusBuilder, warmed > 0 ? totalMs / warmed : 0f);
            if (left > 0 && pending[cursor])
            {
                statusBuilder.Append("  next ").Append(pending[cursor].name);
            }

            statusText = statusBuilder.ToString();
        }

        private static void AppendTenths(StringBuilder builder, float value)
        {
            int tenths = (int)(value * 10f + 0.5f);
            if (tenths < 0)
            {
                tenths = 0;
            }

            builder.Append(tenths / 10);
            builder.Append('.');
            builder.Append(tenths % 10);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        [HarmonyPatch(typeof(NetworkPreloadManager), "PreloadRequestedMastersCache")]
        private static class SliceNetworkMasterPreload
        {
            private static HashSet<MasterCatalog.MasterIndex>? requestedMasters;
            private static bool missingFieldLogged;

            private static bool Prefix()
            {
                if (!Enabled)
                {
                    return true;
                }

                HashSet<MasterCatalog.MasterIndex>? cache = RequestedMasters();
                if (cache == null)
                {
                    return true;
                }

                foreach (MasterCatalog.MasterIndex index in cache)
                {
                    networkListed++;
                    GameObject prefab = MasterCatalog.GetMasterPrefab(index);
                    if (prefab && prefab.TryGetComponent(out CharacterMaster master) && Enqueue(master))
                    {
                        networkQueued++;
                    }
                }

                cache.Clear();
                return false;
            }

            private static HashSet<MasterCatalog.MasterIndex>? RequestedMasters()
            {
                if (requestedMasters != null)
                {
                    return requestedMasters;
                }

                FieldInfo? field = AccessTools.Field(typeof(NetworkPreloadManager), "requestedMasterPreloadsCache");
                if (field == null)
                {
                    if (!missingFieldLogged)
                    {
                        missingFieldLogged = true;
                        RORO.GlobalLogger?.LogWarning("Stage spawn prewarm could not find the game's master preload list. The game will preload those masters in one frame.");
                    }

                    return null;
                }

                requestedMasters = (HashSet<MasterCatalog.MasterIndex>)field.GetValue(null);
                return requestedMasters;
            }
        }
    }
}
