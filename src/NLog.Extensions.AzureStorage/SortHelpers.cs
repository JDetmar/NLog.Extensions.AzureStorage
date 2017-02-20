using System.Collections.Generic;

namespace NLog.Extensions.AzureStorage
{
    internal sealed class SortHelpers
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
        internal static Dictionary<TKey, List<TValue>> BucketSort<TValue, TKey>(IEnumerable<TValue> inputs, KeySelector<TValue, TKey> keySelector)
        {
            var retVal = new Dictionary<TKey, List<TValue>>();

            foreach (var input in inputs)
            {
                var keyValue = keySelector(input);
                if (!retVal.TryGetValue(keyValue, out List<TValue> eventsInBucket))
                {
                    eventsInBucket = new List<TValue>();
                    retVal.Add(keyValue, eventsInBucket);
                }

                eventsInBucket.Add(input);
            }

            return retVal;
        }
    }
}