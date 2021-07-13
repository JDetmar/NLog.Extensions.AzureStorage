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
        internal static Dictionary<TKey, IList<TValue>> BucketSort<TValue, TKey>(IList<TValue> inputs, KeySelector<TValue, TKey> keySelector)
        {
            var retVal = new Dictionary<TKey, IList<TValue>>();

            for (int i = 0; i < inputs.Count; ++i)
            {
                var input = inputs[i];
                var keyValue = keySelector(input);
                if (!retVal.TryGetValue(keyValue, out var eventsInBucket))
                {
                    eventsInBucket = new List<TValue>(inputs.Count - i);
                    retVal.Add(keyValue, eventsInBucket);
                }

                eventsInBucket.Add(input);
            }

            return retVal;
        }
    }
}