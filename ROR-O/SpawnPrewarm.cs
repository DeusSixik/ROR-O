using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.AddressableAssets;
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
        // Masters the server sent for the incoming stage while this peer was still under
        // FadeToBlack, plus the subset already preloaded in that window. Scene load would
        // normally reset the queue and throw that work away; OnSceneLoaded keeps both.
        private static readonly HashSet<CharacterMaster> incomingMasters = new HashSet<CharacterMaster>();
        private static readonly HashSet<CharacterMaster> incomingWarmed = new HashSet<CharacterMaster>();
        private static readonly HashSet<string> eliteDisplayPins = new HashSet<string>();
        private static readonly StringBuilder statusBuilder = new StringBuilder(256);

        private static ConfigEntry<bool>? enable;
        private static ConfigEntry<int>? mastersPerFrame;
        private static ConfigEntry<bool>? warmBosses;
        private static ConfigEntry<bool>? skipLongFrames;
        private static ConfigEntry<bool>? showStatus;
        private static ConfigEntry<bool>? logStatus;
        private static ConfigEntry<bool>? lateSpawnImmunity;
        private static float nextLogTime;

        private const float LateSpawnImmunitySeconds = 5f;

        private static int cursor;
        private static int warmedSinceLog;
        private static int stageListed;
        private static int stageQueued;
        private static int bossesListed;
        private static int bossesQueued;
        private static int networkListed;
        private static int networkQueued;
        private static int warmed;
        private static int eliteDisplays;
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
        internal static ConfigEntry<bool>? LateSpawnImmunityEntry => lateSpawnImmunity;

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

            lateSpawnImmunity = config.Bind(
                section,
                "Late Spawn Immunity",
                false,
                "<color=#FFDD44>[Gameplay tweak]</color> When a player other than the host spawns on a stage, the host grants them the Immune buff for 5 seconds. A stage entry otherwise has no spawn protection. Revives already get 3 seconds from the game, and this does not replace that. The joining client cannot apply it to itself.");

            Profile = SettingsProfiles.Begin(config, section);
            SettingsProfiles.Add(enable, true, true, true);
            SettingsProfiles.Add(mastersPerFrame, 1, 1, 2);
            SettingsProfiles.Add(warmBosses, true, true, true);
            SettingsProfiles.Add(skipLongFrames, true, false, false);
            SettingsProfiles.Add(showStatus, false, false, false);
            SettingsProfiles.Add(logStatus, false, false, false);
            SettingsProfiles.Add(lateSpawnImmunity, false, false, false);
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

            // Elite display pins are held to run end. Outside a run they may already be
            // released, so the next run must be allowed to request them again.
            if (!Run.instance)
            {
                eliteDisplayPins.Clear();
            }

            // Fires before Start on the new scene, so the stage list and the network preload
            // message are still ahead of this. Drop the previous stage and preload the next one.
            // Work already done in the loading window is the exception: masters sent for this
            // stage and preloaded under FadeToBlack stay done, and the unprocessed rest of
            // that list stays queued instead of being thrown away with the old stage.
            if (incomingMasters.Count == 0)
            {
                ResetQueue();
                return;
            }

            var keepPending = new List<CharacterMaster>();
            foreach (CharacterMaster master in incomingMasters)
            {
                if (!incomingWarmed.Contains(master))
                {
                    keepPending.Add(master);
                }
            }

            var keepDone = new List<CharacterMaster>(incomingWarmed);
            ResetQueue();
            foreach (CharacterMaster master in keepDone)
            {
                done.Add(master);
            }

            foreach (CharacterMaster master in keepPending)
            {
                Enqueue(master);
            }
        }

        private static void ResetQueue()
        {
            pending.Clear();
            queued.Clear();
            done.Clear();
            incomingMasters.Clear();
            incomingWarmed.Clear();
            cursor = 0;
            warmedSinceLog = 0;
            stageListed = 0;
            stageQueued = 0;
            bossesListed = 0;
            bossesQueued = 0;
            networkListed = 0;
            networkQueued = 0;
            warmed = 0;
            eliteDisplays = 0;
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
            if (!Enabled || cursor >= pending.Count || LoadingPhase.IsStartupSplash(SceneManager.GetActiveScene().name))
            {
                sceneHold = false;
                return;
            }

            // In the loading window a preload is only an Addressables load start, which
            // survives scene activation, so the queue keeps moving under FadeToBlack with
            // the shared loading budget instead of the in-game per-frame count. Masters
            // that arrive for the incoming stage are marked in OnSceneLoaded's keep sets,
            // so activation does not throw this work away.
            bool loading = LoadingPhase.IsLoading;
            sceneHold = loading;
            if (!loading && skipLongFrames?.Value == true && Time.unscaledDeltaTime > HitchFrameSeconds)
            {
                longSkips++;
                return;
            }

            int budget = Clamp(mastersPerFrame?.Value ?? 1, 1, 4);
            long frameStart = Stopwatch.GetTimestamp();
            int processed = 0;
            while (cursor < pending.Count)
            {
                if (loading)
                {
                    if (LoadingPhase.BudgetExpired(frameStart, processed))
                    {
                        break;
                    }
                }
                else if (processed >= budget)
                {
                    break;
                }

                WarmOne();
                processed++;
            }

            PublishProgress();

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

            float top = 16f + LoadingProgressView.BlockHeight;
            if (EffectRuntime.ShowStatusEntry?.Value == true)
            {
                top += 110f;
            }

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
            if (LoadingPhase.IsLoading)
            {
                incomingWarmed.Add(master);
            }

            master.PreloadAssets();
            long preloaded = Stopwatch.GetTimestamp();
            PinEliteDisplays(master);
            long finished = Stopwatch.GetTimestamp();
            if (HitchProbes.Armed)
            {
                HitchProbes.Add(HitchProbes.Id.Prewarm, preloaded - started);
                HitchProbes.Add(HitchProbes.Id.ElitePin, finished - preloaded);
            }

            float ms = (float)((finished - started) * 1000d / Stopwatch.Frequency);
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

        private static void PublishProgress()
        {
            int left = pending.Count > cursor ? pending.Count - cursor : 0;
            LoadingProgress.Set("Mobs", warmed, warmed + left, left > 0);
            LoadingProgress.Set("Elite displays", warmed, warmed + left, left > 0 && eliteDisplays > 0);
        }

        // An elite is equipment on an ordinary spawn, so there is no elite master to preload.
        // What the first elite spawn does pay is instantiating that body's display for the
        // elite equipment. Pinning the display prefabs in every available elite rule of this
        // body turns that spawn-time WaitForCompletion into a load that already happened.
        // Pins are run-scoped and each address is requested once per run.
        private static void PinEliteDisplays(CharacterMaster master)
        {
            if (!EliteCatalog.availability.available)
            {
                return;
            }

            GameObject? bodyPrefab = master.bodyPrefab;
            if (!bodyPrefab || !bodyPrefab.TryGetComponent(out ModelLocator? locator) || locator == null || !locator.modelTransform)
            {
                return;
            }

            if (!locator.modelTransform.TryGetComponent(out CharacterModel? model) || model == null || !model.itemDisplayRuleSet)
            {
                return;
            }

            ItemDisplayRuleSet ruleSet = model.itemDisplayRuleSet;
            foreach (EliteIndex eliteIndex in EliteCatalog.eliteList)
            {
                EliteDef? eliteDef = EliteCatalog.GetEliteDef(eliteIndex);
                if (eliteDef == null || !eliteDef.eliteEquipmentDef || !eliteDef.IsAvailable())
                {
                    continue;
                }

                DisplayRuleGroup group = ruleSet.GetEquipmentDisplayRuleGroup(eliteDef.eliteEquipmentDef.equipmentIndex);
                if (group.rules == null)
                {
                    continue;
                }

                for (int i = 0; i < group.rules.Length; i++)
                {
                    AssetReferenceGameObject? address = group.rules[i].followerPrefabAddress;
                    if (address == null || !address.RuntimeKeyIsValid())
                    {
                        continue;
                    }

                    if (!eliteDisplayPins.Add(address.RuntimeKey.ToString()))
                    {
                        continue;
                    }

                    EffectPools.PinAddress(new AssetReferenceT<GameObject>(address.AssetGUID));
                    eliteDisplays++;
                }
            }
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
            statusBuilder.Append("  elite displays pinned ").Append(eliteDisplays);
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
                        if (LoadingPhase.IsLoading)
                        {
                            incomingMasters.Add(master);
                        }
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

        // The game parks the master list while the scene is still loading and only calls
        // PreloadRequestedMastersCache once the load is done, which wastes the whole window.
        // During loading, run the cache through the slicing prefix above right away; the
        // parked subscription later finds an empty cache and does nothing.
        [HarmonyPatch(typeof(NetworkPreloadManager), "HandleMasterPreloadMessage")]
        private static class UseLoadingWindowForMasterList
        {
            private static MethodInfo? preloadCache;
            private static bool missingMethodLogged;

            private static void Postfix()
            {
                if (!Enabled || !LoadingPhase.IsLoading)
                {
                    return;
                }

                if (preloadCache == null)
                {
                    preloadCache = AccessTools.Method(typeof(NetworkPreloadManager), "PreloadRequestedMastersCache");
                    if (preloadCache == null)
                    {
                        if (!missingMethodLogged)
                        {
                            missingMethodLogged = true;
                            RORO.GlobalLogger?.LogWarning("Stage spawn prewarm could not start the master preload during loading; it will run after the stage loads.");
                        }

                        return;
                    }
                }

                preloadCache.Invoke(null, null);
            }
        }

        // Stage entry goes through RespawnCharacter and grants no buff. The 3 second Immune
        // buff exists only on revive respawns. This adds 5 seconds for every player whose
        // body is not the listen host's, and it runs on the server because AddTimedBuff
        // from the joining client would not be authoritative.
        [HarmonyPatch(typeof(Stage), nameof(Stage.RespawnCharacter))]
        private static class GrantLateSpawnImmunity
        {
            private static void Postfix(CharacterMaster characterMaster)
            {
                if (lateSpawnImmunity?.Value != true || !NetworkServer.active || !characterMaster)
                {
                    return;
                }

                PlayerCharacterMasterController? player = characterMaster.playerCharacterMasterController;
                if (!player || !player.networkUser || player.networkUser.isLocalPlayer)
                {
                    return;
                }

                CharacterBody? body = characterMaster.GetBody();
                if (!body)
                {
                    return;
                }

                long started = HitchProbes.Armed ? Stopwatch.GetTimestamp() : 0L;
                body.AddTimedBuff(RoR2Content.Buffs.Immune, LateSpawnImmunitySeconds);
                HitchProbes.Note(HitchProbes.Id.SpawnImmunity, started);
            }
        }
    }
}
