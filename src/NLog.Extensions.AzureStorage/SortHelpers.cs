using System;
using System.Collections.Generic;

namespace NLog.Extensions.AzureStorage
{
    internal static class SortHelpers
    {
        /// <summary>
        /// Key Selector Delegate
        /// </summary>
        /// <typeparam name="TValue">The type of the value.</typeparam>
        /// <typeparam name="TKey">The type of the key.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        internal delegate TKey KeySelector<TValue, TKey>(TValue value);

        /// <summary>
        /// Buckets sorts returning a dictionary of lists
        /// </summary>
        /// <typeparam name="TValue">The type of the value.</typeparam>
        /// <typeparam name="TKey">The type of the key.</typeparam>
        /// <param name="inputs">The inputs.</param>
        /// <param name="keySelector">The key selector.</param>
        /// <returns></returns>
        internal static ICollection<KeyValuePair<TKey, IList<TValue>>> BucketSort<TValue, TKey>(IList<TValue> inputs, KeySelector<TValue, TKey> keySelector) where TKey : IEquatable<TKey>
        {
            if (inputs.Count == 0)
                return Array.Empty<KeyValuePair<TKey, IList<TValue>>>();

            Dictionary<TKey, IList<TValue>> buckets = null;
            TKey firstBucketKey = keySelector(inputs[0]);
            for (int i = 1; i < inputs.Count; ++i)
            {
                TKey keyValue = keySelector(inputs[i]);
                if (buckets is null)
                {
                    if (!firstBucketKey.Equals(keyValue))
                    {
                        // Multiple buckets needed, allocate full dictionary
                        buckets = CreateBucketDictionaryWithValue(inputs, i, firstBucketKey, keyValue);
                    }
                }
                else
                {
                    if (!buckets.TryGetValue(keyValue, out var eventsInBucket))
                    {
                        eventsInBucket = new List<TValue>();
                        buckets.Add(keyValue, eventsInBucket);
                    }
                    eventsInBucket.Add(inputs[i]);
                }
            }

            if (buckets is null)
            {
                // All inputs belong to the same bucket
                return new[] { new KeyValuePair<TKey, IList<TValue>>(firstBucketKey, inputs) };
            }
            else
            {
                return buckets;
            }
        }

        private static Dictionary<TKey, IList<TValue>> CreateBucketDictionaryWithValue<TValue, TKey>(IList<TValue> inputs, int currentIndex, TKey firstBucketKey, TKey nextBucketKey)
        {
            var buckets = new Dictionary<TKey, IList<TValue>>();
            var firstBucket = new List<TValue>(Math.Max(currentIndex + 2, 4));
            for (int i = 0; i < currentIndex; i++)
            {
                firstBucket.Add(inputs[i]);
            }
            buckets[firstBucketKey] = firstBucket;

            var nextBucket = new List<TValue> { inputs[currentIndex] };
            buckets[nextBucketKey] = nextBucket;
            return buckets;
        }
    }
}