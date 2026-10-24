using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ROR_O
{
    internal static class EffectPools
    {
        private static readonly FieldInfo? ReferenceField = AccessTools.Field(typeof(EffectDef), "_prefabAssetOrDirectReference");
        private static readonly FieldInfo? MapField = AccessTools.Field(typeof(EffectManager), "_EffectPrefabMap");
        private static readonly MethodInfo? CollectMethod = AccessTools.Method(typeof(System.GC), nameof(System.GC.Collect), System.Type.EmptyTypes);
        private static readonly MethodInfo? CloneMethod = AccessTools.Method(typeof(EffectData), nameof(EffectData.Clone));
        private static readonly object[] LoadArgs = new object[2];

        private static MethodInfo? loadAsset;
        private static object? onRunEnd;
        private static bool loadResolved;

        internal static void Install(Harmony harmony)
        {
            MethodInfo? unload = AccessTools.Method(typeof(EffectManager), "UnloadOutdatedPools");
            if (unload == null)
            {
                RORO.GlobalLogger?.LogWarning("EffectManager.UnloadOutdatedPools was not found; idle effect pools still expire.");
            }
            else
            {
                harmony.Patch(unload, prefix: new HarmonyMethod(typeof(EffectPools), nameof(SkipIdlePoolUnload)));
            }

            MethodInfo? kill = AccessTools.Method(typeof(EffectManager), nameof(EffectManager.KillAllPools));
            if (kill != null)
            {
                harmony.Patch(kill, transpiler: new HarmonyMethod(typeof(EffectPools), nameof(DropForcedCollection)));
            }

            MethodInfo? spawn = AccessTools.Method(
                typeof(EffectManager),
                nameof(EffectManager.SpawnEffect),
                new[] { typeof(EffectIndex), typeof(EffectData), typeof(bool) });
            if (spawn == null)
            {
                RORO.GlobalLogger?.LogWarning("EffectManager.SpawnEffect(EffectIndex) was not found; effect spawns keep the extra copy.");
            }
            else
            {
                harmony.Patch(
                    spawn,
                    transpiler: new HarmonyMethod(typeof(EffectPools), nameof(ReuseSpawnCopy)),
                    postfix: new HarmonyMethod(typeof(EffectPools), nameof(NoteSpawned)));
            }

            MethodInfo? disable = AccessTools.Method(typeof(VFXAttributes), "OnDisable");
            if (disable != null)
            {
                harmony.Patch(disable, postfix: new HarmonyMethod(typeof(EffectPools), nameof(OnEffectDisabled)));
            }
        }

        internal static AssetOrDirectReference<GameObject>? PrefabReference(EffectDef def)
        {
            return ReferenceField?.GetValue(def) as AssetOrDirectReference<GameObject>;
        }

        internal static Dictionary<GameObject, EffectPool>? PoolMap()
        {
            return MapField?.GetValue(null) as Dictionary<GameObject, EffectPool>;
        }

        internal static void CountPools(out int pools, out int idle, out int inUse)
        {
            pools = 0;
            idle = 0;
            inUse = 0;
            Dictionary<GameObject, EffectPool>? map = PoolMap();
            if (map == null)
            {
                return;
            }

            pools = map.Count;
            foreach (EffectPool pool in map.Values)
            {
                idle += pool.PoolCount();
                inUse += pool.InUseCount();
            }
        }

        internal static int CheckoutCount(GameObject prefab)
        {
            Dictionary<GameObject, EffectPool>? map = PoolMap();
            if (map == null || !map.TryGetValue(prefab, out EffectPool? pool))
            {
                return 0;
            }

            return pool.InUseCount();
        }

        internal static int PoolIdleCount(GameObject prefab)
        {
            Dictionary<GameObject, EffectPool>? map = PoolMap();
            if (map == null || !map.TryGetValue(prefab, out EffectPool? pool))
            {
                return 0;
            }

            return pool.PoolCount();
        }

        // Idle plus in-use is the number of copies the pool has actually instantiated.
        // GetObject only creates when that total grows; popping an idle copy does not.
        internal static int PoolTotalCount(GameObject prefab)
        {
            Dictionary<GameObject, EffectPool>? map = PoolMap();
            if (map == null || !map.TryGetValue(prefab, out EffectPool? pool))
            {
                return 0;
            }

            return pool.PoolCount() + pool.InUseCount();
        }

        // GetPooledEffect adds the copy to the in-use list and only then runs Initialize.
        // A throw there leaves that copy checked out. It is the tail of the list.
        internal static void DiscardCheckoutGrowth(GameObject prefab, int checkoutCountBefore)
        {
            Dictionary<GameObject, EffectPool>? map = PoolMap();
            if (map == null || !map.TryGetValue(prefab, out EffectPool? pool))
            {
                return;
            }

            int guard = pool.InUseCount();
            while (pool.InUseCount() > checkoutCountBefore && guard-- > 0)
            {
                List<EffectManagerHelper>? inUse = pool.InUse;
                if (inUse == null || inUse.Count == 0)
                {
                    return;
                }

                EffectManagerHelper leaked = inUse[inUse.Count - 1];
                pool.RemoveObject(leaked);
                if (leaked)
                {
                    UnityEngine.Object.Destroy(leaked.gameObject);
                }
            }
        }

        internal static void Discard(EffectManagerHelper helper)
        {
            if (!helper)
            {
                return;
            }

            EffectPool? pool = helper.OwningPool;
            if (pool != null)
            {
                pool.RemoveObject(helper);
            }

            UnityEngine.Object.Destroy(helper.gameObject);
        }

        // EffectDef's own preload is released on the next scene change. OnRunEnd stays until the run ends,
        // so a later spawn reads the prefab that is already loaded instead of waiting inside SpawnEffect.
        internal static AsyncOperationHandle<GameObject> PinAddress(AssetReferenceT<GameObject> address)
        {
            EnsureLoad();
            if (loadAsset == null || onRunEnd == null)
            {
                return default;
            }

            LoadArgs[0] = address;
            LoadArgs[1] = onRunEnd;
            object? boxed = loadAsset.Invoke(null, LoadArgs);
            LoadArgs[0] = null!;
            if (boxed is AsyncOperationHandle<GameObject> handle)
            {
                return handle;
            }

            return default;
        }

        private static void EnsureLoad()
        {
            if (loadResolved)
            {
                return;
            }

            loadResolved = true;
            Type? open = AccessTools.TypeByName("RoR2.ContentManagement.AssetAsyncReferenceManager`1");
            Type? unload = AccessTools.TypeByName("RoR2.ContentManagement.AsyncReferenceHandleUnloadType");
            if (open == null || unload == null)
            {
                RORO.GlobalLogger?.LogWarning("Effect prefab pinning is unavailable; the asset loader type was not found.");
                return;
            }

            Type closed = open.MakeGenericType(typeof(GameObject));
            loadAsset = AccessTools.Method(closed, "LoadAsset", new[] { typeof(AssetReferenceT<GameObject>), unload });
            if (loadAsset == null)
            {
                RORO.GlobalLogger?.LogWarning("Effect prefab pinning is unavailable; LoadAsset was not found.");
                return;
            }

            onRunEnd = Enum.Parse(unload, "OnRunEnd");
        }

        private static bool SkipIdlePoolUnload()
        {
            return !EffectRuntime.KeepPools;
        }

        private static void NoteSpawned(EffectIndex effectIndex)
        {
            EffectRuntime.NoteEffectSpawned(effectIndex);
        }

        private static void OnEffectDisabled(VFXAttributes __instance)
        {
            EffectRuntime.NoteEffectDisabled(__instance);
        }

        // KillAllPools runs on every scene unload and then calls GC.Collect. The copies are already destroyed.
        // The forced collection is the long pause on a stage change, so the sweep no longer asks for one.
        private static IEnumerable<CodeInstruction> DropForcedCollection(IEnumerable<CodeInstruction> instructions)
        {
            var output = new List<CodeInstruction>();
            int removed = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (CollectMethod != null && instruction.Calls(CollectMethod))
                {
                    removed++;
                    continue;
                }

                output.Add(instruction);
            }

            if (removed == 0)
            {
                RORO.GlobalLogger?.LogWarning("EffectManager.KillAllPools has no parameterless GC.Collect; scene unload still collects.");
            }
            else
            {
                RORO.GlobalLogger?.LogInfo("Effect pools no longer force a garbage collection when a scene unloads.");
            }

            return output;
        }

        // SpawnEffect clones the spawn data, reads it, then clones that same copy again onto the effect.
        // Nothing else keeps the first copy, so the second allocation is stored directly.
        private static IEnumerable<CodeInstruction> ReuseSpawnCopy(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            int removed = 0;
            for (int i = 1; i < list.Count - 1; i++)
            {
                if (list[i].opcode != OpCodes.Callvirt && list[i].opcode != OpCodes.Call)
                {
                    continue;
                }

                if (CloneMethod == null || !list[i].Calls(CloneMethod))
                {
                    continue;
                }

                if (list[i - 1].opcode != OpCodes.Ldloc_3)
                {
                    continue;
                }

                FieldInfo? field = list[i + 1].operand as FieldInfo;
                if (list[i + 1].opcode != OpCodes.Stfld || field == null || field.Name != "effectData")
                {
                    continue;
                }

                list.RemoveAt(i);
                removed++;
                i--;
            }

            if (removed != 2)
            {
                RORO.GlobalLogger?.LogWarning($"Effect spawn still clones its data (removed {removed}, expected 2).");
            }
            else
            {
                RORO.GlobalLogger?.LogInfo("Effect spawn stores its data copy directly.");
            }

            return list;
        }
    }
}
