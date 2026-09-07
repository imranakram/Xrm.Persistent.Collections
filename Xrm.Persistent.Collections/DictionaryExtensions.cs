namespace Xrm.Persistent.Collections
{
    using System;
    using System.Collections.Generic;
    using Interfaces;

    /// <summary>
    /// Batch access over any <see cref="IDictionary{TKey, TValue}"/>, taking the fast path when the
    /// instance can actually offer one.
    /// </summary>
    /// <remarks>
    /// The point of these is that a consumer usually does not know which implementation it holds. A
    /// job engine that swaps an in-memory <c>ConcurrentDictionary</c> for a
    /// <see cref="LocalDictionary{T}"/> once a run gets large hands both to the same code behind
    /// <see cref="IDictionary{TKey, TValue}"/>. Calling <see cref="GetRange{T}"/> there is one call
    /// site that costs a single round trip against the persistent dictionary and a plain loop
    /// against the in-memory one, where a loop was already free.
    /// </remarks>
    public static class DictionaryExtensions
    {
        #region Public Methods

        /// <summary>
        /// Read several keys, in one round trip where the dictionary supports it.
        /// </summary>
        /// <typeparam name="T">The value type stored in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to read from.</param>
        /// <param name="keys">
        /// Keys to read. Keys that are not present are omitted from the result, so it can be
        /// shorter than <paramref name="keys"/>.
        /// </param>
        /// <returns>The keys that were found, with their values.</returns>
        public static IDictionary<string, T> GetRange<T>(this IDictionary<string, T> dictionary, IEnumerable<string> keys)
        {
            if (dictionary == null)
            {
                throw new ArgumentNullException(nameof(dictionary));
            }

            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            if (dictionary is IBulkDictionary<T> bulk)
            {
                return bulk.GetRange(keys);
            }

            var result = new Dictionary<string, T>();
            foreach (var key in keys)
            {
                // Guard the add rather than the read: keys may repeat, and the fast path
                // collapses duplicates, so this has to as well.
                if (!result.ContainsKey(key) && dictionary.TryGetValue(key, out var value))
                {
                    result.Add(key, value);
                }
            }

            return result;
        }

        /// <summary>
        /// Write several key/value pairs, in one round trip where the dictionary supports it.
        /// </summary>
        /// <typeparam name="T">The value type stored in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to write to.</param>
        /// <param name="items">Pairs to write. Existing keys are replaced.</param>
        public static void SetRange<T>(this IDictionary<string, T> dictionary, IDictionary<string, T> items)
        {
            if (dictionary == null)
            {
                throw new ArgumentNullException(nameof(dictionary));
            }

            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (dictionary is IBulkDictionary<T> bulk)
            {
                bulk.SetRange(items);
                return;
            }

            foreach (var item in items)
            {
                dictionary[item.Key] = item.Value;
            }
        }

        #endregion Public Methods
    }
}
