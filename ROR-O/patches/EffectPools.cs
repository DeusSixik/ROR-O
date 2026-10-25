using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RoR2;
using RoR2.Audio;
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
        private static readonly Dictionary<string, uint> SoundIds = new Dictionary<string, uint>(128);
        private static readonly ConditionalWeakTable<EffectManagerHelper, ParkedCopy> Parked = new ConditionalWeakTable<EffectManagerHelper, ParkedCopy>();
        private static readonly Dictionary<GameObject, int> SpawnCounts = new Dictionary<GameObject, int>();
        private static int activeParked;

        // A copy parked active keeps every script on it ticking, so only effects that spawn
        // often earn it, and only up to a global cap. The rest deactivate as the game does.
        private const int HotSpawnThreshold = 12;

        internal static void NotePrefabSpawn(GameObject? prefab)
        {
            if (prefab == null)
            {
                return;
            }

            SpawnCounts.TryGetValue(prefab, out int count);
            SpawnCounts[prefab] = count + 1;
        }

        internal static void ResetParkedActive()
        {
            activeParked = 0;
            SpawnCounts.Clear();
        }
        private static readonly FieldInfo? TrailField = AccessTools.Field(typeof(EffectManagerHelper), "_TrailRenderers");
        private static readonly FieldInfo? VfxField = AccessTools.Field(typeof(EffectManagerHelper), "_vfxAttributes");
        private static readonly FieldInfo? PoolCapacityField = AccessTools.Field(typeof(EffectPool), "poolCapacity");
        private static readonly FieldInfo? OrphanField = AccessTools.Field(typeof(EffectPool), "_OrphanedEffects");
        private static readonly FieldInfo? InUseField = FindPoolField("_InUse");
        private static readonly FieldInfo? IdleField = FindPoolField("_Pool");
        private static readonly MethodInfo? TimestampSetter = FindTimestampSetter();
        private static readonly Vector3 Park = new Vector3(0f, -5000f, 0f);
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

            MethodInfo? reset = AccessTools.Method(typeof(EffectManagerHelper), nameof(EffectManagerHelper.Reset), new[] { typeof(bool) });
            if (reset == null)
            {
                RORO.GlobalLogger?.LogWarning("EffectManagerHelper.Reset was not found; particle restart still walks every child system.");
            }
            else
            {
                harmony.Patch(reset, transpiler: new HarmonyMethod(typeof(EffectPools), nameof(PlayParticlesOnce)));
            }

            MethodInfo? soundId = AccessTools.Method(typeof(AkEventIdArg), "op_Explicit", new[] { typeof(string) });
            if (soundId == null)
            {
                RORO.GlobalLogger?.LogWarning("AkEventIdArg string cast was not found; effect sounds still ask Wwise for the id on every spawn.");
            }
            else
            {
                harmony.Patch(soundId, prefix: new HarmonyMethod(typeof(EffectPools), nameof(RememberSoundId)));
            }

            MethodInfo? effectReturn = AccessTools.Method(typeof(EffectPool), nameof(EffectPool.ReturnObject), new[] { typeof(EffectManagerHelper) });
            if (effectReturn == null)
            {
                RORO.GlobalLogger?.LogWarning("EffectPool.ReturnObject was not found; pooled effects still deactivate.");
            }
            else
            {
                try
                {
                    harmony.Patch(effectReturn, prefix: new HarmonyMethod(typeof(EffectPools), nameof(ParkInsteadOfDeactivate)));
                    RORO.GlobalLogger?.LogInfo("Pooled effects can stay active while parked.");
                }
                catch (Exception exception)
                {
                    RORO.GlobalLogger?.LogWarning($"Pooled effects still deactivate: {exception.Message}");
                }
            }

            foreach (MethodInfo method in typeof(EffectManager).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != nameof(EffectManager.GetPooledEffect) || method.ReturnType != typeof(EffectManagerHelper))
                {
                    continue;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(typeof(EffectPools), nameof(WakeParkedCopy)));
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

        internal static int CheckoutCount(GameObject? prefab)
        {
            if (prefab == null)
            {
                return 0;
            }

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

            return UseCachedEffectComponent(list);
        }

        // GetPooledEffect already ran Initialize, which stored this copy's EffectComponent.
        // The spawn then looks it up on the GameObject again.
        private static List<CodeInstruction> UseCachedEffectComponent(List<CodeInstruction> list)
        {
            MethodInfo? componentGetter = AccessTools.PropertyGetter(typeof(EffectManagerHelper), nameof(EffectManagerHelper.effectComponent));
            MethodInfo? gameObjectGetter = AccessTools.PropertyGetter(typeof(Component), nameof(Component.gameObject));
            MethodInfo? getComponent = AccessTools.Method(typeof(GameObject), nameof(GameObject.GetComponent), Type.EmptyTypes, new[] { typeof(EffectComponent) });
            if (componentGetter == null || gameObjectGetter == null || getComponent == null)
            {
                RORO.GlobalLogger?.LogWarning("Effect spawn still looks up EffectComponent on every copy.");
                return list;
            }

            int replaced = 0;
            for (int i = 0; i < list.Count - 1; i++)
            {
                if (!list[i].Calls(gameObjectGetter) || !list[i + 1].Calls(getComponent))
                {
                    continue;
                }

                list[i].operand = componentGetter;
                CodeInstruction removed = list[i + 1];
                list.RemoveAt(i + 1);
                if (i + 1 < list.Count)
                {
                    list[i + 1].MoveLabelsFrom(removed);
                    list[i + 1].MoveBlocksFrom(removed);
                }

                replaced++;
                break;
            }

            if (replaced != 1)
            {
                RORO.GlobalLogger?.LogWarning($"Effect spawn still looks up EffectComponent on every copy (replaced {replaced}, expected 1).");
            }
            else
            {
                RORO.GlobalLogger?.LogInfo("Effect spawn uses the pool copy's cached EffectComponent.");
            }

            return list;
        }

        // Reset already visits every particle system on the copy. Stop(), Clear(), and Play()
        // with no arguments also walk that system's children, so each child is restarted once
        // for itself and again for every ancestor. The explicit calls cover the same set.
        private static IEnumerable<CodeInstruction> PlayParticlesOnce(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo? stopBare = AccessTools.Method(typeof(ParticleSystem), nameof(ParticleSystem.Stop), Type.EmptyTypes);
            MethodInfo? clearBare = AccessTools.Method(typeof(ParticleSystem), nameof(ParticleSystem.Clear), Type.EmptyTypes);
            MethodInfo? playBare = AccessTools.Method(typeof(ParticleSystem), nameof(ParticleSystem.Play), Type.EmptyTypes);
            MethodInfo? stop = AccessTools.Method(typeof(ParticleSystem), nameof(ParticleSystem.Stop), new[] { typeof(bool), typeof(ParticleSystemStopBehavior) });
            MethodInfo? clear = AccessTools.Method(typeof(ParticleSystem), nameof(ParticleSystem.Clear), new[] { typeof(bool) });
            MethodInfo? play = AccessTools.Method(typeof(ParticleSystem), nameof(ParticleSystem.Play), new[] { typeof(bool) });
            if (stopBare == null || clearBare == null || playBare == null || stop == null || clear == null || play == null)
            {
                RORO.GlobalLogger?.LogWarning("Effect restart still walks child particle systems for every system.");
                return instructions;
            }

            int stops = 0;
            int clears = 0;
            int plays = 0;
            var output = new List<CodeInstruction>();
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(stopBare))
                {
                    output.Add(ShiftArguments(instruction));
                    output.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
                    instruction.operand = stop;
                    stops++;
                }
                else if (instruction.Calls(clearBare))
                {
                    output.Add(ShiftArguments(instruction));
                    instruction.operand = clear;
                    clears++;
                }
                else if (instruction.Calls(playBare))
                {
                    output.Add(ShiftArguments(instruction));
                    instruction.operand = play;
                    plays++;
                }

                output.Add(instruction);
            }

            if (stops != 1 || clears != 1 || plays != 1)
            {
                RORO.GlobalLogger?.LogWarning($"Effect restart still walks child particle systems (stop {stops}, clear {clears}, play {plays}; expected 1 each).");
            }
            else
            {
                RORO.GlobalLogger?.LogInfo("Effect restart plays each particle system once.");
            }

            return output;
        }

        // Wwise resolves a name to a stable id. SpawnEffect casts the effect sound and the surface
        // sound on every copy, and each cast marshals the string into the native sound engine.
        private static bool RememberSoundId(string eventName, ref AkEventIdArg __result)
        {
            if (eventName == null)
            {
                __result = 0u;
                return false;
            }

            if (!SoundIds.TryGetValue(eventName, out uint id))
            {
                id = AkSoundEngine.GetIDFromString(eventName);
                SoundIds.Add(eventName, id);
            }

            __result = id;
            return false;
        }

        // The pool deactivates a returned copy, and the next spawn activates it again. That pair
        // walks the whole hierarchy. Patching PrefabComponentPool<T>.ReturnObject fails: Harmony
        // cannot rebuild a method that still has an open generic argument. EffectPool.ReturnObject
        // is a normal method, so the active-copy path lives here and pushes onto the idle stack
        // itself instead of calling the deactivate.
        private static bool ParkInsteadOfDeactivate(EffectPool __instance, EffectManagerHelper inReturnObject)
        {
            if (!EffectRuntime.KeepCopiesActive || !inReturnObject)
            {
                return true;
            }

            // Keeping every idle copy active taxed every frame for a saving that only
            // frequent spawns repay. Cold effects and a full cap fall back to deactivation.
            GameObject? prefab = __instance.PrefabObject;
            if (!prefab || activeParked >= EffectRuntime.ActiveParkLimit
                || !SpawnCounts.TryGetValue(prefab, out int spawns) || spawns < HotSpawnThreshold)
            {
                return true;
            }

            if (!inReturnObject.EffectComplete())
            {
                if (OrphanField?.GetValue(__instance) is List<EffectManagerHelper> orphans)
                {
                    orphans.Add(inReturnObject);
                }

                return false;
            }

            inReturnObject.OnReturnToPool();
            int capacity = PoolCapacityField != null ? (int)PoolCapacityField.GetValue(__instance) : 20;
            EffectPool? owning = inReturnObject.OwningPool;
            if (owning != null && owning.PoolCount() >= capacity)
            {
                UnityEngine.Object.Destroy(inReturnObject.gameObject);
                return false;
            }

            ParkActiveCopy(inReturnObject);
            if (!PushIdle(__instance, inReturnObject))
            {
                RORO.GlobalLogger?.LogWarning($"Active effect pool could not park {inReturnObject.name}; it was deactivated.");
                inReturnObject.gameObject.SetActive(false);
                PushIdle(__instance, inReturnObject);
                return false;
            }

            if (inReturnObject.gameObject.activeSelf
                && Parked.TryGetValue(inReturnObject, out ParkedCopy? parked) && parked != null && parked.Hidden)
            {
                activeParked++;
                parked.Counted = true;
            }

            return false;
        }

        private static bool PushIdle(EffectPool pool, EffectManagerHelper helper)
        {
            object? inUseValue = InUseField?.GetValue(pool);
            object? idleValue = IdleField?.GetValue(pool);
            if (!(inUseValue is List<EffectManagerHelper> inUse) || !(idleValue is Stack<EffectManagerHelper> idle))
            {
                return false;
            }

            if (!inUse.Contains(helper))
            {
                if (!idle.Contains(helper))
                {
                    UnityEngine.Object.Destroy(helper.gameObject);
                }

                return true;
            }

            idle.Push(helper);
            inUse.Remove(helper);
            TimestampSetter?.Invoke(pool, new object[] { Time.realtimeSinceStartup });
            return true;
        }

        private static FieldInfo? FindPoolField(string name)
        {
            Type? open = AccessTools.TypeByName("Grumpy.GenericPool`1");
            if (open == null)
            {
                return null;
            }

            return AccessTools.Field(open.MakeGenericType(typeof(EffectManagerHelper)), name);
        }

        private static MethodInfo? FindTimestampSetter()
        {
            Type? open = AccessTools.TypeByName("Grumpy.GenericPool`1");
            if (open == null)
            {
                return null;
            }

            return AccessTools.PropertySetter(open.MakeGenericType(typeof(EffectManagerHelper)), "timestamp");
        }

        private static void ParkActiveCopy(EffectManagerHelper helper)
        {
            GameObject copy = helper.gameObject;
            // A copy created inactive has not run Start. Turning it on here runs Start
            // before SpawnEffect has assigned effect data. Leave it inactive; the real
            // spawn activates it after the data is in place.
            if (!copy.activeSelf)
            {
                return;
            }

            helper.StopAllParticleSystems(true);
            HideRenderers(helper);
            HideLights(helper);
            ClearTrails(helper);
            if (copy.transform.parent != null)
            {
                copy.transform.SetParent(null, true);
            }

            copy.transform.position = Park;
            if (VfxField?.GetValue(helper) is VFXAttributes attributes && attributes.enabled)
            {
                attributes.enabled = false;
            }
        }

        internal static void WakeParkedCopy(EffectManagerHelper __result)
        {
            // Runs even when the toggle was switched off after this copy parked: a copy
            // checked out hidden must get its renderers and lights back either way.
            if (!__result || !__result.gameObject.activeSelf)
            {
                return;
            }

            if (!Parked.TryGetValue(__result, out ParkedCopy? parked) || parked == null || !parked.Hidden)
            {
                return;
            }

            Renderer[] renderers = parked.Renderers;
            bool[] wasEnabled = parked.WasEnabled;
            int count = renderers.Length < wasEnabled.Length ? renderers.Length : wasEnabled.Length;
            for (int i = 0; i < count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer)
                {
                    renderer.enabled = wasEnabled[i];
                }
            }

            Light[] lights = parked.Lights;
            bool[] lightWasEnabled = parked.LightWasEnabled;
            int lightCount = lights.Length < lightWasEnabled.Length ? lights.Length : lightWasEnabled.Length;
            for (int i = 0; i < lightCount; i++)
            {
                Light light = lights[i];
                if (light)
                {
                    light.enabled = lightWasEnabled[i];
                }
            }

            parked.Hidden = false;
            if (parked.Counted)
            {
                parked.Counted = false;
                if (activeParked > 0)
                {
                    activeParked--;
                }
            }

            if (VfxField?.GetValue(__result) is VFXAttributes attributes && !attributes.enabled)
            {
                attributes.enabled = true;
            }
        }

        private static ParkedCopy ParkedFor(EffectManagerHelper helper)
        {
            if (Parked.TryGetValue(helper, out ParkedCopy? parked) && parked != null)
            {
                return parked;
            }

            parked = new ParkedCopy(
                helper.GetComponentsInChildren<Renderer>(true),
                helper.GetComponentsInChildren<Light>(true));
            Parked.Add(helper, parked);
            return parked;
        }

        private static void HideLights(EffectManagerHelper helper)
        {
            ParkedCopy parked = ParkedFor(helper);
            Light[] lights = parked.Lights;
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (!light)
                {
                    parked.LightWasEnabled[i] = false;
                    continue;
                }

                parked.LightWasEnabled[i] = light.enabled;
                light.enabled = false;
            }
        }

        private static void HideRenderers(EffectManagerHelper helper)
        {
            ParkedCopy parked = ParkedFor(helper);

            if (parked.Hidden)
            {
                return;
            }

            Renderer[] renderers = parked.Renderers;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!renderer)
                {
                    parked.WasEnabled[i] = false;
                    continue;
                }

                parked.WasEnabled[i] = renderer.enabled;
                renderer.enabled = false;
            }

            parked.Hidden = true;
        }

        private static void ClearTrails(EffectManagerHelper helper)
        {
            if (!(TrailField?.GetValue(helper) is List<TrailRenderer> trails))
            {
                return;
            }

            for (int i = 0; i < trails.Count; i++)
            {
                TrailRenderer trail = trails[i];
                if (trail)
                {
                    trail.Clear();
                }
            }
        }

        private sealed class ParkedCopy
        {
            internal readonly Renderer[] Renderers;
            internal readonly bool[] WasEnabled;
            internal readonly Light[] Lights;
            internal readonly bool[] LightWasEnabled;
            internal bool Hidden;
            internal bool Counted;

            internal ParkedCopy(Renderer[] renderers, Light[] lights)
            {
                Renderers = renderers;
                WasEnabled = new bool[renderers.Length];
                Lights = lights;
                LightWasEnabled = new bool[lights.Length];
            }
        }

        private static CodeInstruction ShiftArguments(CodeInstruction call)
        {
            CodeInstruction argument = new CodeInstruction(OpCodes.Ldc_I4_0);
            argument.MoveLabelsFrom(call);
            argument.MoveBlocksFrom(call);
            return argument;
        }
    }
}
