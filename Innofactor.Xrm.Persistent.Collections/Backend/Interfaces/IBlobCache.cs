namespace Xrm.Persistent.Collections.Backend.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface IBlobCache : IDisposable
    {
        #region Public Methods

        /// <summary>
        /// Get a single item
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<byte[]> Get(string key);

        /// <summary>
        /// Get a list of items
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        Task<IDictionary<string, byte[]>> Get(IEnumerable<string> keys);

        /// <summary>
        /// Get an object serialized via InsertObject
        /// </summary>
        /// <param name="key"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        Task<byte[]> Get(string key, string type);

        /// <summary>
        /// Get a list of objects given a list of keys
        /// </summary>
        /// <param name="keys"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        Task<IDictionary<string, byte[]>> Get(IEnumerable<string> keys, string type);

        /// <summary>
        /// Get all objects of type <paramref name="type"/>
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        Task<IEnumerable<byte[]>> GetAll(string type);

        /// <summary>
        /// Return the time which an item was created
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<DateTimeOffset?> GetCreatedAt(string key);

        /// <summary>
        /// Return the time which a list of keys were created
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        Task<IDictionary<string, DateTimeOffset?>> GetCreatedAt(IEnumerable<string> keys);

        //// Return a list of all keys. Use for debugging purposes only.
        //Task<IEnumerable<string>> GetAllKeys();
        // Return the time which an object of type T was created
        Task<DateTimeOffset?> GetObjectCreatedAt<T>(string key);

        /// <summary>
        /// Get an object serialized via InsertObject
        /// </summary>
        /// <param name="key"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        Task<byte[]> GetOrDefault(string key, string type);

        /*
         * Save items to the store
         */

        /// <summary>
        /// Insert a single item
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        /// <param name="absoluteExpiration"></param>
        /// <returns></returns>
        Task Insert(string key, byte[] data, DateTimeOffset? absoluteExpiration = null);

        /// <summary>
        /// Insert a set of items
        /// </summary>
        /// <param name="keyValuePairs"></param>
        /// <param name="absoluteExpiration"></param>
        /// <returns></returns>
        Task Insert(IDictionary<string, byte[]> keyValuePairs, DateTimeOffset? absoluteExpiration = null);

        /// <summary>
        /// Insert a single object
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        /// <param name="type"></param>
        /// <param name="absoluteExpiration"></param>
        /// <returns></returns>
        Task Insert(string key, byte[] data, string type, DateTimeOffset? absoluteExpiration = null);

        /// <summary>
        /// Insert a group of objects
        /// </summary>
        /// <param name="keyValuePairs"></param>
        /// <param name="type"></param>
        /// <param name="absoluteExpiration"></param>
        /// <returns></returns>
        Task Insert(IDictionary<string, byte[]> keyValuePairs, string type, DateTimeOffset? absoluteExpiration = null);

        /*
         * Remove items from the store
         */

        /// <summary>
        /// Delete a single item
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task Invalidate(string key);

        /// <summary>
        /// Delete a list of items
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        Task Invalidate(IEnumerable<string> keys);

        /// <summary>
        /// Deletes all items (regardless if they are objects or not)
        /// </summary>
        /// <returns></returns>
        Task InvalidateAll();

        /// <summary>
        /// Deletes all objects of type <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task InvalidateAllObjects<T>();

        /// <summary>
        /// Delete a single object (do *not* use Invalidate for items inserted with InsertObject!)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        Task InvalidateObject<T>(string key);

        /// <summary>
        /// Deletes a list of objects
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="keys"></param>
        /// <returns></returns>
        Task InvalidateObjects<T>(IEnumerable<string> keys);

        /*
         * Get Metadata about items
         */
        /*
         * Utility methods
         */

        /// <summary>
        /// Preemptively drop all expired keys and run SQLite's VACUUM method on the underlying database
        /// </summary>
        /// <returns></returns>
        Task Vacuum();

        #endregion Public Methods
    }
}