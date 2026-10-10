using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ROR_O.patches
{
    /// <summary>
    /// Weak-key map that drops entries after the key is collected.
    /// Adapted from RoR2BepInExPack.Utilities.FixedConditionalWeakTable by Risk of Thunder.
    /// Unity Mono's <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/> keeps dead keys alive.
    /// Source: https://github.com/risk-of-thunder/RoR2BepInExPack/blob/dlc3/src/Utilities/FixedConditionalWeakTable.cs
    /// </summary>
    internal sealed class FixedConditionalWeakTable<TKey, TValue> : IFixedConditionalWeakTable
        where TKey : class
        where TValue : class, new()
    {
        private readonly ConcurrentDictionary<WeakReferenceWrapper, TValue> valueByKey =
            new ConcurrentDictionary<WeakReferenceWrapper, TValue>(new WeakReferenceWrapperComparer());

        public FixedConditionalWeakTable()
        {
            FixedConditionalWeakTableManager.Register(this);
        }

        void IFixedConditionalWeakTable.Shrink()
        {
            Shrink();
        }

        public TValue GetOrCreateValue(TKey key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (valueByKey.TryGetValue(new WeakReferenceWrapper(key, true), out TValue? existing))
            {
                return existing;
            }

            TValue created = new TValue();
            if (valueByKey.TryAdd(new WeakReferenceWrapper(key, false), created))
            {
                return created;
            }

            return valueByKey[new WeakReferenceWrapper(key, true)];
        }

        internal void Shrink()
        {
            List<int>? deadHashes = null;
            foreach (WeakReferenceWrapper keyReference in valueByKey.Keys)
            {
                if (keyReference.weakReference != null && !keyReference.weakReference.TryGetTarget(out _))
                {
                    deadHashes ??= new List<int>();
                    deadHashes.Add(keyReference.targetHashCode);
                }
            }

            if (deadHashes == null)
            {
                return;
            }

            for (int i = 0; i < deadHashes.Count; i++)
            {
                valueByKey.TryRemove(new WeakReferenceWrapper(deadHashes[i]), out _);
            }
        }

        private readonly struct WeakReferenceWrapper
        {
            public readonly int targetHashCode;
            public readonly WeakReference<TKey>? weakReference;
            private readonly TKey? target;

            public WeakReferenceWrapper(TKey target, bool strongReference)
            {
                targetHashCode = target.GetHashCode();
                if (strongReference)
                {
                    this.target = target;
                    weakReference = null;
                }
                else
                {
                    this.target = null;
                    weakReference = new WeakReference<TKey>(target);
                }
            }

            public WeakReferenceWrapper(int targetHashCode)
            {
                this.targetHashCode = targetHashCode;
                target = null;
                weakReference = null;
            }

            public bool IsTombstone => target == null && weakReference == null;

            public bool TryGetTarget(out TKey? resolved)
            {
                if (target != null)
                {
                    resolved = target;
                    return true;
                }

                if (weakReference != null && weakReference.TryGetTarget(out TKey? alive))
                {
                    resolved = alive;
                    return true;
                }

                resolved = null;
                return false;
            }
        }

        private sealed class WeakReferenceWrapperComparer : IEqualityComparer<WeakReferenceWrapper>
        {
            public bool Equals(WeakReferenceWrapper first, WeakReferenceWrapper second)
            {
                if (first.IsTombstone)
                {
                    return !second.TryGetTarget(out _);
                }

                if (second.IsTombstone)
                {
                    return !first.TryGetTarget(out _);
                }

                return first.TryGetTarget(out TKey? firstTarget)
                    && second.TryGetTarget(out TKey? secondTarget)
                    && ReferenceEquals(firstTarget, secondTarget);
            }

            public int GetHashCode(WeakReferenceWrapper obj)
            {
                return obj.targetHashCode;
            }
        }
    }

    internal interface IFixedConditionalWeakTable
    {
        void Shrink();
    }

    internal static class FixedConditionalWeakTableManager
    {
        private const int ShrinkDelayMilliseconds = 2000;
        private static readonly object Gate = new object();
        private static readonly List<WeakReference<IFixedConditionalWeakTable>> Tables =
            new List<WeakReference<IFixedConditionalWeakTable>>();
        private static int lastCollectionCount;

        public static void Register(IFixedConditionalWeakTable table)
        {
            lock (Gate)
            {
                if (Tables.Count == 0)
                {
                    var thread = new Thread(ShrinkLoop)
                    {
                        IsBackground = true,
                        Name = "ROR-O weak table cleanup"
                    };
                    thread.Start();
                }

                Tables.Add(new WeakReference<IFixedConditionalWeakTable>(table));
            }
        }

        private static void ShrinkLoop()
        {
            while (true)
            {
                Thread.Sleep(ShrinkDelayMilliseconds);
                int collectionCount = GC.CollectionCount(2);
                if (collectionCount == lastCollectionCount)
                {
                    continue;
                }

                lastCollectionCount = collectionCount;
                lock (Gate)
                {
                    for (int i = Tables.Count - 1; i >= 0; i--)
                    {
                        if (!Tables[i].TryGetTarget(out IFixedConditionalWeakTable? table))
                        {
                            Tables.RemoveAt(i);
                            continue;
                        }

                        table.Shrink();
                    }

                    if (Tables.Count == 0)
                    {
                        return;
                    }
                }
            }
        }
    }
}
