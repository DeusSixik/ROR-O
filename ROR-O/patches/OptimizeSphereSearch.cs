using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace ROR_O.patches
{
    // SphereSearch walks the same collider buffer once per filter, and OrderByDistance bubble-sorts it.
    // One event search was about a thousand GetCandidate calls. The teleporter pull does that almost every
    // fixed frame even though its origin does not move. AI targeting uses BullseyeSearch, not this path.
    internal static class OptimizeSphereSearch
    {
        private const float RefreshSeconds = 0.25f;
        private const float MoveMetersSqr = 0.25f;

        private static float[] distances = new float[128];
        private static HealthComponent?[] healths = new HealthComponent?[128];
        private static HealthComponent?[] kept = new HealthComponent?[128];

        private static Func<Array, int, float>? readDistance;
        private static Func<Array, int, HealthComponent?>? readHealth;

        internal static void Install(Harmony harmony)
        {
            try
            {
                Type searchData = AccessTools.Inner(typeof(SphereSearch), "SearchData");
                Type candidate = AccessTools.Inner(typeof(SphereSearch), "Candidate");
                if (searchData == null || candidate == null)
                {
                    RORO.GlobalLogger?.LogWarning("SphereSearch layout was not found; candidate walk stays vanilla.");
                    return;
                }

                FieldInfo mappingField = AccessTools.Field(searchData, "candidatesMapping");
                FieldInfo countField = AccessTools.Field(searchData, "candidatesCount");
                FieldInfo bufferField = AccessTools.Field(searchData, "candidatesBuffer");
                FieldInfo distanceField = AccessTools.Field(candidate, "distanceSqr");
                FieldInfo hurtBoxField = AccessTools.Field(candidate, "hurtBox");
                FieldInfo healthField = AccessTools.Field(typeof(HurtBox), "healthComponent");
                MethodInfo healthFilter = AccessTools.Method(searchData, "FilterByHurtBoxHealthComponents");
                if (mappingField == null || countField == null || bufferField == null || distanceField == null
                    || hurtBoxField == null || healthField == null || healthFilter == null)
                {
                    RORO.GlobalLogger?.LogWarning("SphereSearch fields were not found; candidate walk stays vanilla.");
                    return;
                }

                readDistance = CompileDistance(candidate, distanceField);
                readHealth = CompileHealth(candidate, hurtBoxField, healthField);
                Module module = typeof(SphereSearch).Module;
                harmony.Patch(
                    AccessTools.Method(searchData, "OrderByDistance"),
                    new HarmonyMethod(CompileSkip(module, searchData, mappingField, countField, bufferField, nameof(Sort), false, null)));
                harmony.Patch(
                    AccessTools.Method(searchData, "FilterByHurtBoxEntitiesDistinct"),
                    new HarmonyMethod(CompileSkip(module, searchData, mappingField, countField, bufferField, nameof(Unique), true, healthFilter)));
                RORO.GlobalLogger?.LogInfo("SphereSearch candidate walk is one pass. Stationary radial pulls reuse their target list.");
            }
            catch (Exception exception)
            {
                RORO.GlobalLogger?.LogWarning($"SphereSearch candidate walk stays vanilla: {exception.Message}");
            }
        }

        public static void Sort(int[]? mapping, int count, Array? buffer)
        {
            if (mapping == null || buffer == null || count <= 1 || readDistance == null)
            {
                return;
            }

            Ensure(count);
            for (int i = 0; i < count; i++)
            {
                distances[i] = readDistance(buffer, mapping[i]);
            }

            for (int i = 1; i < count; i++)
            {
                int map = mapping[i];
                float distance = distances[i];
                int j = i;
                while (j > 0 && distances[j - 1] > distance)
                {
                    distances[j] = distances[j - 1];
                    mapping[j] = mapping[j - 1];
                    j--;
                }

                distances[j] = distance;
                mapping[j] = map;
            }
        }

        public static void Unique(int[]? mapping, ref int count, Array? buffer)
        {
            if (mapping == null || buffer == null || count <= 1 || readHealth == null)
            {
                return;
            }

            int length = count;
            Ensure(length);
            for (int i = 0; i < length; i++)
            {
                healths[i] = readHealth(buffer, mapping[i]);
            }

            int write = 0;
            for (int i = 0; i < length; i++)
            {
                HealthComponent? health = healths[i];
                if (!health)
                {
                    continue;
                }

                bool seen = false;
                for (int j = 0; j < write; j++)
                {
                    if (ReferenceEquals(kept[j], health))
                    {
                        seen = true;
                        break;
                    }
                }

                if (seen)
                {
                    continue;
                }

                kept[write] = health;
                mapping[write] = mapping[i];
                write++;
            }

            count = write;
        }

        private static void Ensure(int count)
        {
            if (count <= distances.Length)
            {
                return;
            }

            int size = distances.Length;
            while (size < count)
            {
                size *= 2;
            }

            distances = new float[size];
            healths = new HealthComponent?[size];
            kept = new HealthComponent?[size];
        }

        private static Func<Array, int, float> CompileDistance(Type candidate, FieldInfo distanceField)
        {
            var method = new DynamicMethod(
                "RoroSphereDistance",
                typeof(float),
                new[] { typeof(Array), typeof(int) },
                typeof(SphereSearch).Module,
                skipVisibility: true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, candidate.MakeArrayType());
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldelem, candidate);
            il.Emit(OpCodes.Ldfld, distanceField);
            il.Emit(OpCodes.Ret);
            return (Func<Array, int, float>)method.CreateDelegate(typeof(Func<Array, int, float>));
        }

        private static Func<Array, int, HealthComponent?> CompileHealth(Type candidate, FieldInfo hurtBoxField, FieldInfo healthField)
        {
            var method = new DynamicMethod(
                "RoroSphereHealth",
                typeof(HealthComponent),
                new[] { typeof(Array), typeof(int) },
                typeof(SphereSearch).Module,
                skipVisibility: true);
            ILGenerator il = method.GetILGenerator();
            Label missing = il.DefineLabel();
            Label done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, candidate.MakeArrayType());
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldelema, candidate);
            il.Emit(OpCodes.Ldfld, hurtBoxField);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, missing);
            il.Emit(OpCodes.Ldfld, healthField);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(missing);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull);
            il.MarkLabel(done);
            il.Emit(OpCodes.Ret);
            return (Func<Array, int, HealthComponent?>)method.CreateDelegate(typeof(Func<Array, int, HealthComponent?>));
        }

        private static DynamicMethod CompileSkip(
            Module module,
            Type searchData,
            FieldInfo mappingField,
            FieldInfo countField,
            FieldInfo bufferField,
            string helper,
            bool healthFilterFirst,
            MethodInfo? healthFilter)
        {
            var method = new DynamicMethod(
                "Roro" + helper,
                typeof(bool),
                new[] { searchData.MakeByRefType() },
                module,
                skipVisibility: true);
            ILGenerator il = method.GetILGenerator();
            if (healthFilterFirst && healthFilter != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, healthFilter);
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, mappingField);
            il.Emit(OpCodes.Ldarg_0);
            if (helper == nameof(Unique))
            {
                il.Emit(OpCodes.Ldflda, countField);
            }
            else
            {
                il.Emit(OpCodes.Ldfld, countField);
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, bufferField);
            il.Emit(OpCodes.Castclass, typeof(Array));
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(OptimizeSphereSearch), helper));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            return method;
        }

        [HarmonyPatch(typeof(RadialForce), "SearchForTargets")]
        private static class CacheRadialForceTargets
        {
            private static bool Prefix(RadialForce __instance, List<HurtBox> dest, ref bool __state)
            {
                __state = false;
                if (HurtBoxQueryCache.TryFill(__instance, dest))
                {
                    return false;
                }

                __state = true;
                return true;
            }

            private static void Postfix(RadialForce __instance, List<HurtBox> dest, bool __state)
            {
                if (__state)
                {
                    HurtBoxQueryCache.Store(__instance, dest);
                }
            }
        }

        private sealed class Slot
        {
            public Vector3 Origin;
            public float Radius;
            public float Until;
            public HurtBox[] Hits = new HurtBox[8];
            public int Count;
        }

        private static class HurtBoxQueryCache
        {
            private static readonly ConditionalWeakTable<RadialForce, Slot> Slots = new ConditionalWeakTable<RadialForce, Slot>();

            public static bool TryFill(RadialForce force, List<HurtBox> dest)
            {
                if (force.transform == null || !Slots.TryGetValue(force, out Slot? slot))
                {
                    return false;
                }

                Vector3 origin = force.transform.position;
                if (Time.fixedTime >= slot.Until
                    || (origin - slot.Origin).sqrMagnitude > MoveMetersSqr
                    || !Mathf.Approximately(force.radius, slot.Radius))
                {
                    return false;
                }

                int live = 0;
                for (int i = 0; i < slot.Count; i++)
                {
                    HurtBox hit = slot.Hits[i];
                    if (!hit)
                    {
                        continue;
                    }

                    dest.Add(hit);
                    live++;
                }

                return live > 0 || slot.Count == 0;
            }

            public static void Store(RadialForce force, List<HurtBox> dest)
            {
                if (force.transform == null)
                {
                    return;
                }

                Slot slot = Slots.GetValue(force, _ => new Slot());
                if (slot.Hits.Length < dest.Count)
                {
                    int size = slot.Hits.Length;
                    while (size < dest.Count)
                    {
                        size *= 2;
                    }

                    slot.Hits = new HurtBox[size];
                }

                for (int i = 0; i < dest.Count; i++)
                {
                    slot.Hits[i] = dest[i];
                }

                slot.Count = dest.Count;
                slot.Origin = force.transform.position;
                slot.Radius = force.radius;
                slot.Until = Time.fixedTime + RefreshSeconds;
            }
        }
    }
}
