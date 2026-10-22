using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace ROR_O
{
    // The first spawn of a monster pays CharacterBody.PreloadAssets: animator, avatar, and skins.
    // The game already does that for the selected non-boss masters, all inside one network message.
    // This spreads the same call across frames and includes bosses from the stage's resolved list.
    // Instantiating a prefab and destroying it does not keep that load, and Awake runs again on the real spawn.
    internal sealed class SpawnPrewarm : MonoBehaviour
    {
        private static readonly List<CharacterMaster> pending = new List<CharacterMaster>();
        private static readonly HashSet<CharacterMaster> queued = new HashSet<CharacterMaster>();
        private static readonly HashSet<CharacterMaster> done = new HashSet<CharacterMaster>();

        private static int cursor;
        private static int warmedSinceLog;

        internal static void Enqueue(CharacterMaster master)
        {
            if (!master || done.Contains(master) || !queued.Add(master))
            {
                return;
            }

            pending.Add(master);
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
        }

        private void OnStageStart(Stage stage)
        {
            if (!ROROConfig.EnableSpawnPrewarm || stage == null)
            {
                return;
            }

            CollectSelectedMasters();
        }

        private void Update()
        {
            if (!ROROConfig.EnableSpawnPrewarm || cursor >= pending.Count)
            {
                return;
            }

            if (NetworkManager.IsSceneLoadingAsynchronously)
            {
                return;
            }

            CharacterMaster master = pending[cursor];
            cursor++;
            queued.Remove(master);
            if (master)
            {
                long started = HitchProbes.Armed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
                done.Add(master);
                master.PreloadAssets();
                if (started != 0L)
                {
                    HitchProbes.Add(HitchProbes.Id.Prewarm, System.Diagnostics.Stopwatch.GetTimestamp() - started);
                }

                warmedSinceLog++;
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

        private static void CollectSelectedMasters()
        {
            ClassicStageInfo? info = ClassicStageInfo.instance;
            WeightedSelection<DirectorCard>? selection = info ? info.monsterSelection : null;
            if (selection == null)
            {
                return;
            }

            int count = selection.Count;
            for (int i = 0; i < count; i++)
            {
                DirectorCard? card = selection.choices[i].value;
                SpawnCard? spawnCard = card != null ? card.GetSpawnCard() : null;
                GameObject? prefab = spawnCard != null ? spawnCard.prefab : null;
                if (prefab != null && prefab.TryGetComponent(out CharacterMaster? master) && master != null)
                {
                    Enqueue(master);
                }
            }
        }

        [HarmonyPatch(typeof(NetworkPreloadManager), "PreloadRequestedMastersCache")]
        private static class SliceNetworkMasterPreload
        {
            private static HashSet<MasterCatalog.MasterIndex>? requestedMasters;
            private static bool missingFieldLogged;

            private static bool Prefix()
            {
                if (!ROROConfig.EnableSpawnPrewarm)
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
                    GameObject prefab = MasterCatalog.GetMasterPrefab(index);
                    if (prefab && prefab.TryGetComponent(out CharacterMaster master))
                    {
                        Enqueue(master);
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
