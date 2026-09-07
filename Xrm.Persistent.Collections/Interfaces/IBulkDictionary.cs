namespace Xrm.Persistent.Collections.Interfaces
{
    using System.Collections.Generic;

    /// <summary>
    /// Batch read and write for dictionaries where per-key access costs a round trip.
    /// </summary>
    /// <remarks>
    /// <see cref="LocalDictionary{T}"/> satisfies <see cref="IDictionary{TKey, TValue}"/> one key at
    /// a time, and every one of those keys is a SQLite query. A caller that walks n keys therefore
    /// pays n round trips even though the backing <see cref="Backend.PersistentBlobCache"/> has
    /// always been able to serve them in one - it chunks a set of keys into a single
    /// <c>WHERE Key IN (...)</c> per 950 keys, and writes a set inside a single transaction.
    /// <para>
    /// This interface is what exposes that. It is deliberately separate from
    /// <see cref="IDictionary{TKey, TValue}"/> so that a consumer holding only the interface type
    /// can test for it and fall back, which is what the <c>GetRange</c>/<c>SetRange</c> extension
    /// methods in <see cref="DictionaryExtensions"/> do.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The value type stored in the dictionary.</typeparam>
    public interface IBulkDictionary<T>
    {
        #region Public Methods

        /// <summary>
        /// Read several keys in one round trip.
        /// </summary>
        /// <param name="keys">
        /// Keys to read. Duplicates are collapsed. Keys that are not present are omitted from the
        /// result rather than yielding a default value, so the result can be shorter than
        /// <paramref name="keys"/> - the same contract as calling
        /// <see cref="IDictionary{TKey, TValue}.TryGetValue"/> on each key and keeping the hits.
        /// </param>
        /// <returns>The keys that were found, with their values.</returns>
        IDictionary<string, T> GetRange(IEnumerable<string> keys);

        /// <summary>
        /// Write several key/value pairs in one round trip.
        /// </summary>
        /// <param name="items">
        /// Pairs to write. Existing keys are replaced, exactly as the
        /// <see cref="IDictionary{TKey, TValue}"/> indexer setter does - this is not an add, and it
        /// does not throw on a key that already exists.
        /// </param>
        void SetRange(IDictionary<string, T> items);

        #endregion Public Methods
    }
}
