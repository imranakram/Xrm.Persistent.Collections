namespace Xrm.Persistent.Collections.Backend
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Interfaces;
    using SQLite;
    using Structure;

    public class PersistentBlobCache : IBlobCache
    {
        #region Private Fields

        private readonly Type _cacheItemType;
        private readonly SemaphoreSlim _createSemaphore;
        private readonly string _databasePath;
        private readonly SemaphoreSlim _writeSemaphore;
        private SQLiteConnection _db;

        #endregion Private Fields

        #region Public Constructors

        public PersistentBlobCache(string databasePath)
        {
            _databasePath = databasePath;
            _writeSemaphore = new SemaphoreSlim(1, 1);
            _createSemaphore = new SemaphoreSlim(1, 1);
            _cacheItemType = typeof(CacheItem);
        }

        public PersistentBlobCache(IStorageProvider storageProvider, StorageLocation location, string applicationName)
            : this(storageProvider.GetDatabasePath(applicationName, location))
        { }

        #endregion Public Constructors

        #region Public Methods

        public async Task CreateConnection()
        {
            if (_db != null)
            {
                return;
            }

            try
            {
                await _createSemaphore.WaitAsync();
                if (_db != null)
                {
                    return;
                }

                var directory = Path.GetDirectoryName(_databasePath);
                if (!Directory.Exists(directory) && !string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _db = new SQLiteConnection(_databasePath);
                _db.ExecuteScalar<string>("PRAGMA journal_mode = WAL");
                EnsureSchema();
            }
            finally
            {
                _createSemaphore.Release();
            }
        }

        public void Dispose()
        {
            _db?.Close();
            _db?.Dispose();
            _writeSemaphore?.Dispose();
            _createSemaphore?.Dispose();
        }

        public async Task<byte[]> Get(string key) =>
            await Get(key, string.Empty).ConfigureAwait(false);

        public async Task<IDictionary<string, byte[]>> Get(IEnumerable<string> keys) =>
            (await Get(keys, string.Empty).ConfigureAwait(false))
            .ToDictionary(o => o.Key, o => o.Value);

        public async Task<byte[]> Get(string key, string type)
        {
            var item = await GetOrDefault(key, type).ConfigureAwait(false);
            if (item == null)
            {
                throw new KeyNotFoundException(key);
            }

            return item;
        }

        public async Task<IDictionary<string, byte[]>> Get(IEnumerable<string> keys, string type)
        {
            var distinctKeys = keys.Distinct().ToArray();

            var tasks = distinctKeys
                .Chunk()
                .Select(chunk =>
                {
                    return Read(o =>
                    {
                        var chunkKeys = chunk.ToArray();
                        var keyParameters = string.Join(", ", Enumerable.Repeat("?", chunkKeys.Length));
                        var sql = $@"
                            SELECT * FROM CacheItem
                            WHERE
                                (Time IS null OR Time >= ?)
                                AND Type = ?
                                AND Key IN ({keyParameters})";

                        var args = new object[2 + chunkKeys.Length];
                        args[0] = DateTime.UtcNow.Ticks;
                        args[1] = type;
                        chunkKeys.CopyTo(args, 2);

                        return o.Query<CacheItem>(sql, args)
                            .Select(p => new GetObjectResult
                            {
                                Key = p.Key,
                                Data = p.Data
                            });
                    });
                });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var defaultT = new byte[] { };
            var result = new Dictionary<string, byte[]>(distinctKeys.ToDictionary(o => o, o => defaultT));

            var chunks = tasks.Select(o => o.Result).ToArray();
            foreach (var chunk in chunks)
            {
                foreach (var item in chunk)
                {
                    result[item.Key] = item.Data;
                }
            }
            return result;
        }

        public Task<IEnumerable<byte[]>> GetAll(string type) =>
            Read(o =>
            {
                var query = @"
                SELECT Data from CacheItem
                WHERE
                    TYPE = ?
                    AND (Time IS null OR Time >= ?)";

                return o.Query<CacheItem>(query, type, DateTime.UtcNow.Ticks)
                    .Select(p => p.Data);
            });

        // TODO: add to interface
        public Task<IEnumerable<byte[]>> GetAll() =>
            Read(o =>
            {
                var query = @"
                SELECT Data from CacheItem
                WHERE
                    (Time IS null OR Time >= ?)";

                return o.Query<CacheItem>(query, DateTime.UtcNow.Ticks)
                    .Select(p => p.Data);
            });

        // TODO: add to interface
        public Task<IEnumerable<KeyResult>> GetAllKeys()
        {
            var query = @"
                SELECT Key, Type FROM CacheItem
                WHERE
                    (Time IS null OR Time >= ?)";

            return Read(o =>
            {
                return o.Query<KeyQueryResult>(query, DateTime.UtcNow.Ticks)
                    .Select(p => new KeyResult
                    {
                        Key = p.Key,
                        Type = Type.GetType(p.Type)  // todo: test this
                    });
            });
        }

        public Task<DateTimeOffset?> GetCreatedAt(string key) =>
            GetObjectCreatedAt<BinaryItem>(key);

        public Task<IDictionary<string, DateTimeOffset?>> GetCreatedAt(IEnumerable<string> keys) =>
            GetObjectsCreatedAt<BinaryItem>(keys);

        public Task<DateTimeOffset?> GetObjectCreatedAt<T>(string key) =>
            Read(o =>
            {
                var query = @"
                    SELECT CreatedAt AS UtcTicks FROM CacheItem
                    WHERE
                        Key = ?
                        AND Type = ?
                        AND (Time IS null OR Time >= ?)
                    LIMIT 1";

                var result = o.Query<DateQueryResult>(query, key, typeof(T).FullName, DateTime.UtcNow.Ticks).FirstOrDefault();
                if (result == null)
                {
                    return default(DateTimeOffset?);
                }

                return new DateTimeOffset(result.UtcTicks, TimeSpan.Zero);
            });

        public async Task<IDictionary<string, DateTimeOffset?>> GetObjectsCreatedAt<T>(IEnumerable<string> keys)
        {
            keys = keys.Distinct();

            var utcTicks = DateTime.UtcNow.Ticks;
            var typeName = typeof(T).FullName;

            var tasks = keys
                .Chunk()
                .Select(chunk =>
                {
                    return Read(o =>
                    {
                        var chunkKeys = chunk.ToArray();
                        var keyParameters = string.Join(", ", Enumerable.Repeat("?", chunkKeys.Length));
                        var sql = $@"
                            SELECT Key, CreatedAt AS UtcTicks FROM CacheItem
                            WHERE
                                (Time IS null OR Time >= ?)
                                AND Type = ?
                                AND Key IN ({keyParameters})
                        ";

                        var args = new object[2 + chunkKeys.Length];
                        args[0] = utcTicks;
                        args[1] = typeName;
                        chunkKeys.CopyTo(args, 2);
                        return o.Query<DateQueryResult>(sql, args);
                    });
                });

            await Task.WhenAll(tasks).ConfigureAwait(false);
            var foundKeys = tasks
                .SelectMany(o => o.Result)
                .ToDictionary(o => o.Key, o => new DateTimeOffset(o.UtcTicks, TimeSpan.Zero));

            var result = new Dictionary<string, DateTimeOffset?>();
            foreach (var key in keys)
            {
                // This piece should not be modernized for sake of CI server
#pragma warning disable IDE0018 // Inline variable declaration
                var dateTimeOffset = default(DateTimeOffset);
#pragma warning restore IDE0018 // Inline variable declaration

                if (foundKeys.TryGetValue(key, out dateTimeOffset))
                {
                    result.Add(key, dateTimeOffset);
                }
                else
                {
                    result.Add(key, null);
                }
            }
            return result;
        }

        public Task<byte[]> GetOrDefault(string key, string type) =>
            Read(o =>
            {
                var query = @"
                    SELECT * FROM CacheItem
                    WHERE
                        Type = ?
                        AND Key = ?
                        AND (Time IS null OR Time >= ?)
                    LIMIT 1";

                var cacheItem = o.Query<CacheItem>(query, type, key, DateTime.UtcNow.Ticks).FirstOrDefault();
                if (cacheItem == null)
                {
                    return new byte[] { };
                }

                return cacheItem.Data;
            });

        public Task Insert(string key, byte[] data, DateTimeOffset? absoluteExpiration = null) =>
            Insert(key, data, string.Empty, absoluteExpiration);

        public Task Insert(IDictionary<string, byte[]> keyValuePairs, DateTimeOffset? absoluteExpiration = null) =>
            Insert(keyValuePairs, string.Empty, absoluteExpiration);

        public async Task Insert(string key, byte[] data, string type, DateTimeOffset? absoluteExpiration = null)
        {
            var item = await Task.Run(() =>
            {
                return new CacheItem
                {
                    Key = key,
                    Type = type,
                    CreatedAt = DateTime.UtcNow.Ticks,
                    Time = absoluteExpiration?.UtcTicks,
                    Data = data
                };
            }).ConfigureAwait(false);

            await Write(o =>
            {
                o.InsertOrReplace(item, _cacheItemType);
            });
        }

        public async Task Insert(IDictionary<string, byte[]> keyValuePairs, string type, DateTimeOffset? absoluteExpiration = null)
        {
            var items = await Task.Run(() =>
            {
                var createdTicks = DateTime.UtcNow.Ticks;
                var expiresTicks = absoluteExpiration?.UtcTicks;
                return keyValuePairs
                    .Select(p => new CacheItem
                    {
                        Key = p.Key,
                        Type = type,
                        CreatedAt = createdTicks,
                        Time = expiresTicks,
                        Data = p.Value
                    });
            }).ConfigureAwait(false);

            await Write(db =>
            {
                db.RunInTransaction(() =>
                {
                    var chunks = items.Chunk();
                    foreach (var chunk in chunks)
                    {
                        var keys = chunk.Select(o => o.Key).ToArray();
                        var keyParameters = string.Join(", ", Enumerable.Repeat("?", keys.Length));
                        var sql = $@"
                            DELETE FROM CacheItem
                            WHERE
                                Type = ?
                                AND Key IN ({keyParameters})";

                        var args = new object[1 + keys.Count()];
                        args[0] = type;
                        keys.CopyTo(args, 1);
                        db.Execute(sql, args);

                        db.InsertAll(chunk, _cacheItemType, runInTransaction: false);
                    }
                });
            });
        }

        public Task Invalidate(string key) =>
            InvalidateObject<BinaryItem>(key);

        public Task Invalidate(IEnumerable<string> keys) =>
            InvalidateObjects<BinaryItem>(keys);

        public Task InvalidateAll() =>
            Write(o => o.Execute("DELETE FROM CacheItem"));

        public Task InvalidateAllObjects<T>()
        {
            var sql = @"
                DELETE FROM CacheItem
                WHERE Type = ?";

            return Write(o => o.Execute(sql, typeof(T).FullName));
        }

        public Task InvalidateObject<T>(string key)
        {
            var sql = @"
                DELETE FROM CacheItem
                WHERE
                    Type = ?
                    AND Key = ?";

            return Write(o => o.Execute(sql, typeof(T).FullName, key));
        }

        public Task InvalidateObject(string key)
        {
            var sql = @"
                DELETE FROM CacheItem
                WHERE
                    Key = ?";

            return Write(o => o.Execute(sql, key));
        }

        public Task InvalidateObjects<T>(IEnumerable<string> keys) =>
            Write(db =>
            {
                db.RunInTransaction(() =>
                {
                    var chunks = keys.Chunk();
                    var tType = typeof(T).FullName;
                    foreach (var chunk in chunks)
                    {
                        var chunkKeys = chunk.ToArray();
                        var keyParameters = string.Join(", ", Enumerable.Repeat("?", chunkKeys.Length));
                        var sql = $@"
                            DELETE FROM CacheItem
                            WHERE
                                Type = ?
                                AND Key in ({keyParameters})";

                        var args = new object[1 + chunkKeys.Length];
                        args[0] = tType;
                        chunkKeys.CopyTo(args, 1);
                        db.Execute(sql, args);
                    }
                });
            });

        public Task Vacuum() =>
            Write(o =>
            {
                var sqlDeleteExpired = @"
                    DELETE FROM CacheItem
                    WHERE Time < ?";

                o.Execute(sqlDeleteExpired, DateTime.UtcNow.Ticks);
                o.Execute("VACUUM;");
            });

        #endregion Public Methods

        #region Private Methods

        private void EnsureSchema()
        {
            var tableSQL = @"
                CREATE TABLE IF NOT EXISTS CacheItem
                (
                    Key TEXT NOT NULL,
                    Type TEXT NOT NULL,
                    CreatedAt INTEGER NOT NULL,
                    Time INTEGER,
                    Data BLOB,
                    PRIMARY KEY (Key, Type)
                ) WITHOUT ROWID;";

            _db.Execute(tableSQL);
        }

        private async Task<T> Read<T>(Func<SQLiteConnection, T> readOperation)
        {
            if (_db == null)
            {
                await CreateConnection();
            }

            return readOperation(_db);
        }

        private async Task Write(Action<SQLiteConnection> writeOperation)
        {
            try
            {
                await _writeSemaphore.WaitAsync();  // todo: safe to configure await here?  ¯\_(ツ)_/¯
                if (_db == null)
                {
                    await CreateConnection();
                }

                writeOperation(_db);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        #endregion Private Methods
    }
}