using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ROR_O
{
    // Addressables reads the asset on its own loader thread. The call that starts a load has to be made
    // from the main thread, and WaitForCompletion is what would pin this thread until the read finishes.
    // The game's loader refuses to start anything while a scene is loading, and it drops Preload handles
    // when the new scene activates, so a preload made through that loader during the fade never sticks.
    // These handles are held here, and nothing in this type waits for them.
    internal static class AssetLoadAhead
    {
        private sealed class Slot
        {
            public AssetReference? Reference;
            public AsyncOperationHandle<UnityEngine.Object> Handle;
            public bool Started;
        }

        private static readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>();
        private static readonly Queue<string> order = new Queue<string>();
        private static bool startFailed;

        internal static void Enqueue(AssetReference? reference)
        {
            if (reference == null || !reference.RuntimeKeyIsValid())
            {
                return;
            }

            string key = reference.RuntimeKey.ToString();
            if (slots.ContainsKey(key))
            {
                return;
            }

            slots[key] = new Slot { Reference = reference };
            order.Enqueue(key);
        }

        internal static void Release(AssetReference? reference)
        {
            if (reference == null || !reference.RuntimeKeyIsValid())
            {
                return;
            }

            string key = reference.RuntimeKey.ToString();
            if (!slots.TryGetValue(key, out Slot? slot) || slot == null)
            {
                return;
            }

            slots.Remove(key);
            if (slot.Started && slot.Handle.IsValid())
            {
                Addressables.Release(slot.Handle);
            }
        }

        // False while the read is still queued or in flight. A finished load, including a failed
        // one, is settled so the caller can stop waiting. A key we never started is not settled.
        internal static bool IsSettled(AssetReference? reference)
        {
            if (reference == null || !reference.RuntimeKeyIsValid())
            {
                return true;
            }

            if (!slots.TryGetValue(reference.RuntimeKey.ToString(), out Slot? slot) || slot == null || !slot.Started)
            {
                return false;
            }

            if (!slot.Handle.IsValid())
            {
                return true;
            }

            return slot.Handle.IsDone;
        }

        internal static void ReleaseAll()
        {
            foreach (KeyValuePair<string, Slot> pair in slots)
            {
                Slot? slot = pair.Value;
                if (slot != null && slot.Started && slot.Handle.IsValid())
                {
                    Addressables.Release(slot.Handle);
                }
            }

            slots.Clear();
            order.Clear();
        }

        // alreadyProcessed counts work already done in this frame against the loading budget,
        // so a fade frame does not pay for masters and asset starts separately.
        internal static void Pump(long frameStart, int alreadyProcessed)
        {
            int started = alreadyProcessed;
            while (order.Count > 0)
            {
                if (LoadingPhase.BudgetExpired(frameStart, started))
                {
                    break;
                }

                string key = order.Dequeue();
                if (!slots.TryGetValue(key, out Slot? slot) || slot == null || slot.Started || slot.Reference == null)
                {
                    continue;
                }

                try
                {
                    slot.Handle = Addressables.LoadAssetAsync<UnityEngine.Object>(slot.Reference);
                    slot.Started = true;
                    slot.Reference = null;
                }
                catch (System.Exception exception)
                {
                    // Leave the slot settled and invalid so a master is not queued forever on one bad key.
                    slot.Started = true;
                    slot.Handle = default;
                    slot.Reference = null;
                    if (!startFailed)
                    {
                        startFailed = true;
                        RORO.GlobalLogger?.LogWarning($"Asset load did not start: {exception.Message}");
                    }

                    continue;
                }

                started++;
                // The start itself can run the integration before it returns. One such call
                // spends the frame; do not stack another on top of it.
                if (LoadingPhase.BudgetExpired(frameStart, started))
                {
                    break;
                }
            }
        }
    }
}
