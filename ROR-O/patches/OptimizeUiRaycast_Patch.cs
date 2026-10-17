// Off for now. A profiled run put GraphicRaycaster.Raycast around 0.07 ms/frame
// including the work it calls, so reusing the hit list is not worth a separate path.
#if false
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ROR_O.patches
{
    // GraphicRaycaster tests every raycast target on the canvas. While the pointer stays put the
    // result does not change, so the same hit list is reused for a few frames and then refreshed.
    [HarmonyPatch(typeof(GraphicRaycaster), nameof(GraphicRaycaster.Raycast), typeof(PointerEventData), typeof(List<RaycastResult>))]
    public static class OptimizeUiRaycastPatch
    {
        private const int MaxIdleFrames = 4;
        private const int SlotCount = 4;
        private const float PointerEpsilonSqr = 1f;

        private struct Slot
        {
            public bool Occupied;
            public int PointerId;
            public Vector2 Pointer;
            public int Frame;
            public RaycastResult[]? Results;
            public int Count;
        }

        private sealed class RaycastCache
        {
            public readonly Slot[] Slots = new Slot[SlotCount];
            public int StartCount;
            public int WriteSlot = -1;
            public bool Reused;
        }

        private static readonly FixedConditionalWeakTable<GraphicRaycaster, RaycastCache> CacheByRaycaster =
            new FixedConditionalWeakTable<GraphicRaycaster, RaycastCache>();

        static OptimizeUiRaycastPatch()
        {
            RORO.GlobalLogger?.LogInfo("UI raycasts are reused for a few frames while the pointer stays still.");
        }

        private static bool Prefix(GraphicRaycaster __instance, PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            if (!__instance || eventData == null || resultAppendList == null)
            {
                return true;
            }

            RaycastCache cache = CacheByRaycaster.GetOrCreateValue(__instance);
            cache.Reused = false;
            cache.WriteSlot = -1;
            int slotIndex = FindSlot(cache, eventData.pointerId);
            cache.WriteSlot = slotIndex;
            bool scrolling = eventData.scrollDelta.sqrMagnitude > 0.0001f;
            if (!scrolling && CanReuse(cache.Slots[slotIndex], eventData))
            {
                Append(resultAppendList, cache.Slots[slotIndex]);
                cache.Reused = true;
                return false;
            }

            cache.StartCount = resultAppendList.Count;
            return true;
        }

        private static void Postfix(GraphicRaycaster __instance, PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            if (!__instance || eventData == null || resultAppendList == null)
            {
                return;
            }

            RaycastCache cache = CacheByRaycaster.GetOrCreateValue(__instance);
            if (cache.Reused || cache.WriteSlot < 0)
            {
                return;
            }

            int added = resultAppendList.Count - cache.StartCount;
            if (added < 0)
            {
                return;
            }

            Slot slot = cache.Slots[cache.WriteSlot];
            if (slot.Results == null || slot.Results.Length < added)
            {
                slot.Results = new RaycastResult[Mathf.Max(added, 8)];
            }

            for (int i = 0; i < added; i++)
            {
                slot.Results[i] = resultAppendList[cache.StartCount + i];
            }

            slot.Count = added;
            slot.Occupied = true;
            slot.Pointer = eventData.position;
            slot.PointerId = eventData.pointerId;
            slot.Frame = Time.frameCount;
            cache.Slots[cache.WriteSlot] = slot;
            cache.WriteSlot = -1;
        }

        private static int FindSlot(RaycastCache cache, int pointerId)
        {
            int match = -1;
            int free = -1;
            int oldest = 0;
            int oldestFrame = int.MaxValue;
            for (int i = 0; i < SlotCount; i++)
            {
                Slot slot = cache.Slots[i];
                if (!slot.Occupied)
                {
                    if (free < 0)
                    {
                        free = i;
                    }

                    continue;
                }

                if (slot.Frame < oldestFrame)
                {
                    oldestFrame = slot.Frame;
                    oldest = i;
                }

                if (slot.PointerId == pointerId)
                {
                    match = i;
                }
            }

            if (match >= 0)
            {
                return match;
            }

            return free >= 0 ? free : oldest;
        }

        private static bool CanReuse(in Slot slot, PointerEventData eventData)
        {
            if (!slot.Occupied)
            {
                return false;
            }

            int age = Time.frameCount - slot.Frame;
            if (age < 0 || age >= MaxIdleFrames)
            {
                return false;
            }

            float dx = eventData.position.x - slot.Pointer.x;
            float dy = eventData.position.y - slot.Pointer.y;
            if (dx * dx + dy * dy > PointerEpsilonSqr)
            {
                return false;
            }

            return ResultsAlive(slot);
        }

        private static bool ResultsAlive(in Slot slot)
        {
            RaycastResult[]? results = slot.Results;
            if (results == null)
            {
                return slot.Count == 0;
            }

            int count = slot.Count;
            if (count > results.Length)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (!results[i].gameObject)
                {
                    return false;
                }
            }

            return true;
        }

        private static void Append(List<RaycastResult> resultAppendList, in Slot slot)
        {
            RaycastResult[]? results = slot.Results;
            if (results == null)
            {
                return;
            }

            for (int i = 0; i < slot.Count; i++)
            {
                resultAppendList.Add(results[i]);
            }
        }
    }
}
#endif
