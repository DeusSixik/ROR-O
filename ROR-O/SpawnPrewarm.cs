using System.Collections;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace ROR_O
{
    // The first time a monster or pickup is created, its prefab pays Awake on the combat frame.
    // After the stage starts, each of those prefabs is created once away from the map and removed,
    // one per frame, so that cost does not land on a fight.
    internal sealed class SpawnPrewarm : MonoBehaviour
    {
        private static readonly Vector3 WarmPosition = new Vector3(0f, -4000f, 0f);
        private const int PrefabCap = 80;

        private Coroutine? running;

        private void OnEnable()
        {
            Stage.onStageStartGlobal += OnStageStart;
        }

        private void OnDisable()
        {
            Stage.onStageStartGlobal -= OnStageStart;
            if (running != null)
            {
                StopCoroutine(running);
                running = null;
            }
        }

        private void OnStageStart(Stage stage)
        {
            if (!ROROConfig.EnableSpawnPrewarm || stage == null)
            {
                return;
            }

            if (running != null)
            {
                StopCoroutine(running);
            }

            running = StartCoroutine(WarmStage());
        }

        private IEnumerator WarmStage()
        {
            yield return null;
            if (!ROROConfig.EnableSpawnPrewarm)
            {
                running = null;
                yield break;
            }

            var prefabs = new List<GameObject>();
            CollectStage(prefabs);
            AddPrefab(prefabs, GenericPickupController.pickupPrefab);
            AddPrefab(prefabs, GenericPickupController.pickupPrefabNoJuice);
            AddPrefab(prefabs, PickupDropletController.pickupDropletPrefab);
            if (prefabs.Count == 0)
            {
                running = null;
                yield break;
            }

            int warmed = 0;
            RORO.GlobalLogger?.LogInfo($"Stage spawn prewarm started: {prefabs.Count} prefabs.");
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (!ROROConfig.EnableSpawnPrewarm)
                {
                    break;
                }

                if (WarmPrefab(prefabs[i]))
                {
                    warmed++;
                }

                yield return null;
            }

            RORO.GlobalLogger?.LogInfo($"Stage spawn prewarm finished: {warmed} prefabs.");
            running = null;
        }

        private static void CollectStage(List<GameObject> prefabs)
        {
            ClassicStageInfo info = ClassicStageInfo.instance;
            if (!info)
            {
                return;
            }

            CollectPool(info.GetMonsterDccsPool, prefabs);
            ClassicStageInfo.MonsterFamily[] families = info.possibleMonsterFamilies;
            if (families == null)
            {
                return;
            }

            for (int i = 0; i < families.Length; i++)
            {
                CollectSelection(families[i].monsterFamilyCategories, prefabs);
            }
        }

        private static void CollectPool(DccsPool pool, List<GameObject> prefabs)
        {
            if (pool == null || pool.poolCategories == null)
            {
                return;
            }

            DccsPool.Category[] categories = pool.poolCategories;
            for (int i = 0; i < categories.Length; i++)
            {
                DccsPool.Category category = categories[i];
                CollectEntries(category.alwaysIncluded, prefabs);
                CollectEntries(category.includedIfNoConditionsMet, prefabs);
                DccsPool.ConditionalPoolEntry[] conditional = category.includedIfConditionsMet;
                if (conditional == null)
                {
                    continue;
                }

                for (int j = 0; j < conditional.Length; j++)
                {
                    CollectSelection(conditional[j].dccs, prefabs);
                }
            }
        }

        private static void CollectEntries(DccsPool.PoolEntry[] entries, List<GameObject> prefabs)
        {
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                CollectSelection(entries[i].dccs, prefabs);
            }
        }

        private static void CollectSelection(DirectorCardCategorySelection selection, List<GameObject> prefabs)
        {
            if (selection == null || selection.categories == null)
            {
                return;
            }

            DirectorCardCategorySelection.Category[] categories = selection.categories;
            for (int i = 0; i < categories.Length; i++)
            {
                DirectorCard[] cards = categories[i].cards;
                if (cards == null)
                {
                    continue;
                }

                for (int j = 0; j < cards.Length; j++)
                {
                    AddCard(cards[j], prefabs);
                }
            }
        }

        private static void AddCard(DirectorCard card, List<GameObject> prefabs)
        {
            if (card == null)
            {
                return;
            }

            SpawnCard spawnCard = card.spawnCard;
            if (!spawnCard || !spawnCard.prefab)
            {
                return;
            }

            AddPrefab(prefabs, spawnCard.prefab);
            CharacterMaster master = spawnCard.prefab.GetComponent<CharacterMaster>();
            if (!master)
            {
                return;
            }

            AddPrefab(prefabs, master.bodyPrefab);
            AddPrefab(prefabs, master.originalBodyPrefab);
        }

        private static void AddPrefab(List<GameObject> prefabs, GameObject prefab)
        {
            if (!prefab || prefabs.Count >= PrefabCap)
            {
                return;
            }

            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] == prefab)
                {
                    return;
                }
            }

            prefabs.Add(prefab);
        }

        private static bool WarmPrefab(GameObject prefab)
        {
            if (!prefab)
            {
                return false;
            }

            try
            {
                GameObject instance = Object.Instantiate(prefab, WarmPosition, Quaternion.identity);
                if (!instance.activeSelf)
                {
                    instance.SetActive(true);
                }

                instance.SetActive(false);
                Object.Destroy(instance);
                return true;
            }
            catch (System.Exception exception)
            {
                RORO.GlobalLogger?.LogWarning($"Stage spawn prewarm skipped {prefab.name}: {exception.Message}");
                return false;
            }
        }
    }
}
