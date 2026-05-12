using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace ROR_O.Utilities
{
    /// <summary>
    /// ConditionalWeakTable replacement that behaves correctly on Unity Mono.
    /// Adapted from RoR2BepInExPack utilities.
    /// https://github.com/risk-of-thunder/RoR2BepInExPack/blob/dlc3/src/Utilities/FixedConditionalWeakTable.cs
    /// </summary>
    internal sealed class FixedConditionalWeakTable<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, FixedConditionalWeakTableManager.IShrinkable
        where TKey : class
        where TValue : class
    {
        private ConstructorInfo? cachedConstructor;
        private readonly ConcurrentDictionary<WeakReferenceWrapper<TKey>, TValue> valueByKey =
            new ConcurrentDictionary<WeakReferenceWrapper<TKey>, TValue>(new WeakReferenceWrapperComparer<TKey>());

        public TValue this[TKey key]
        {
            get => valueByKey[new WeakReferenceWrapper<TKey>(key, true)];
            set => valueByKey[new WeakReferenceWrapper<TKey>(key, false)] = value;
        }

        public ICollection<TKey> Keys
        {
            get
            {
                List<TKey> keys = new List<TKey>(valueByKey.Count);
                foreach (WeakReferenceWrapper<TKey> keyReference in valueByKey.Keys)
                {
                    if (keyReference.WeakReference != null && keyReference.WeakReference.TryGetTarget(out TKey key))
                    {
                        keys.Add(key);
                    }
                }

                return keys.AsReadOnly();
            }
        }

        public ICollection<TValue> Values
        {
            get
            {
                List<TValue> values = new List<TValue>(valueByKey.Count);
                foreach (KeyValuePair<WeakReferenceWrapper<TKey>, TValue> pair in valueByKey)
                {
                    if (pair.Key.WeakReference != null && pair.Key.WeakReference.TryGetTarget(out _))
                    {
                        values.Add(pair.Value);
                    }
                }

                return values.AsReadOnly();
            }
        }

        public int Count
        {
            get
            {
                int count = 0;

                foreach (WeakReferenceWrapper<TKey> keyReference in valueByKey.Keys)
                {
                    if (keyReference.WeakReference != null && keyReference.WeakReference.TryGetTarget(out _))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int SpeculativeCount => valueByKey.Count;

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        public FixedConditionalWeakTable()
        {
            FixedConditionalWeakTableManager.Add(this);
        }

        public void Add(TKey key, TValue value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (!valueByKey.TryAdd(new WeakReferenceWrapper<TKey>(key, false), value))
            {
                throw new ArgumentException("The key already exists", nameof(key));
            }
        }

        public bool Remove(TKey key)
        {
            return valueByKey.TryRemove(new WeakReferenceWrapper<TKey>(key, true), out _);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return valueByKey.TryGetValue(new WeakReferenceWrapper<TKey>(key, true), out value!);
        }

        public TValue GetValue(TKey key, Func<TKey, TValue> defaultFunc)
        {
            if (TryGetValue(key, out TValue value))
            {
                return value;
            }

            value = defaultFunc(key);
            Add(key, value);
            return value;
        }

        public TValue GetOrCreateValue(TKey key)
        {
            if (TryGetValue(key, out TValue value))
            {
                return value;
            }

            if (cachedConstructor == null)
            {
                Type type = typeof(TValue);
                cachedConstructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (cachedConstructor == null)
                {
                    throw new MissingMethodException(type.FullName + " doesn't have a parameterless constructor");
                }
            }

            value = (TValue)cachedConstructor.Invoke(Array.Empty<object>());
            Add(key, value);
            return value;
        }

        void FixedConditionalWeakTableManager.IShrinkable.Shrink()
        {
            foreach (KeyValuePair<WeakReferenceWrapper<TKey>, TValue> item in valueByKey)
            {
                if (item.Key.WeakReference == null || item.Key.WeakReference.TryGetTarget(out _))
                {
                    continue;
                }

                valueByKey.TryRemove(new WeakReferenceWrapper<TKey>(item.Key.TargetHashCode), out _);
            }
        }

        public bool ContainsKey(TKey key)
        {
            return valueByKey.ContainsKey(new WeakReferenceWrapper<TKey>(key, true));
        }

        public void Clear()
        {
            valueByKey.Clear();
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            return TryGetValue(item.Key, out TValue value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (arrayIndex < 0 || arrayIndex >= array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), nameof(arrayIndex) + " is not a valid index in " + nameof(array));
            }

            int count = Count;
            if (arrayIndex + count > array.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(array), "Destination array is not long enough to copy all the items in the collection.");
            }

            foreach (KeyValuePair<WeakReferenceWrapper<TKey>, TValue> pair in valueByKey)
            {
                if (pair.Key.WeakReference != null && pair.Key.WeakReference.TryGetTarget(out TKey key))
                {
                    array[arrayIndex++] = new KeyValuePair<TKey, TValue>(key, pair.Value);
                }
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            return TryGetValue(item.Key, out TValue value)
                   && EqualityComparer<TValue>.Default.Equals(value, item.Value)
                   && Remove(item.Key);
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            foreach (KeyValuePair<WeakReferenceWrapper<TKey>, TValue> pair in valueByKey)
            {
                if (pair.Key.WeakReference != null && pair.Key.WeakReference.TryGetTarget(out TKey key))
                {
                    yield return new KeyValuePair<TKey, TValue>(key, pair.Value);
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private readonly struct WeakReferenceWrapper<T>
            where T : class
        {
            public readonly int TargetHashCode;
            public readonly WeakReference<T>? WeakReference;
            public readonly T? Target;

            public WeakReferenceWrapper(T target, bool strongReference)
            {
                TargetHashCode = target.GetHashCode();
                if (strongReference)
                {
                    Target = target;
                    WeakReference = null;
                }
                else
                {
                    Target = null;
                    WeakReference = new WeakReference<T>(target);
                }
            }

            public WeakReferenceWrapper(int targetHashCode)
            {
                TargetHashCode = targetHashCode;
                Target = null;
                WeakReference = null;
            }
        }

        private readonly struct WeakReferenceWrapperComparer<T> : IEqualityComparer<WeakReferenceWrapper<T>>
            where T : class
        {
            public bool Equals(WeakReferenceWrapper<T> first, WeakReferenceWrapper<T> second)
            {
                T? firstTarget = first.Target;
                T? secondTarget = second.Target;

                if (firstTarget == null && first.WeakReference == null)
                {
                    return second.WeakReference != null && !second.WeakReference.TryGetTarget(out _);
                }

                if (secondTarget == null && second.WeakReference == null)
                {
                    return first.WeakReference != null && !first.WeakReference.TryGetTarget(out _);
                }

                if (firstTarget == null && (first.WeakReference == null || !first.WeakReference.TryGetTarget(out firstTarget)))
                {
                    return false;
                }

                if (secondTarget == null && (second.WeakReference == null || !second.WeakReference.TryGetTarget(out secondTarget)))
                {
                    return false;
                }

                return firstTarget == secondTarget;
            }

            public int GetHashCode(WeakReferenceWrapper<T> obj)
            {
                return obj.TargetHashCode;
            }
        }
    }

    internal static class FixedConditionalWeakTableManager
    {
        private const int ShrinkAttemptDelayMilliseconds = 2000;

        private static readonly object LockObject = new object();
        private static readonly List<WeakReference<IShrinkable>> Instances = new List<WeakReference<IShrinkable>>();
        private static int lastCollectionCount;

        public static void Add(IShrinkable weakTable)
        {
            lock (LockObject)
            {
                if (Instances.Count == 0)
                {
                    Thread shrinkThread = new Thread(ShrinkThreadLoop)
                    {
                        IsBackground = true,
                        Name = "ROR-O FixedConditionalWeakTable Shrinker"
                    };
                    shrinkThread.Start();
                }

                Instances.Add(new WeakReference<IShrinkable>(weakTable));
            }
        }

        private static void ShrinkThreadLoop()
        {
            while (true)
            {
                Thread.Sleep(ShrinkAttemptDelayMilliseconds);
                int newCollectionCount = GC.CollectionCount(2);
                if (lastCollectionCount == newCollectionCount)
                {
                    continue;
                }

                lastCollectionCount = newCollectionCount;

                lock (LockObject)
                {
                    for (int i = Instances.Count - 1; i >= 0; i--)
                    {
                        if (!Instances[i].TryGetTarget(out IShrinkable weakTable))
                        {
                            Instances.RemoveAt(i);
                            continue;
                        }

                        weakTable.Shrink();
                    }

                    if (Instances.Count == 0)
                    {
                        return;
                    }
                }
            }
        }

        internal interface IShrinkable
        {
            void Shrink();
        }
    }
}
