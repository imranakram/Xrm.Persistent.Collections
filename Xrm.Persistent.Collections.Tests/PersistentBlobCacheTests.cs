namespace Xrm.Persistent.Collections.Backend
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Structure;
    using Xunit;

    public class PersistentBlobCacheTests : IDisposable
    {
        #region Private Fields

        private readonly string dbPath;
        private readonly PersistentBlobCache cache;

        #endregion Private Fields

        #region Public Constructors

        public PersistentBlobCacheTests()
        {
            var suffix = Guid.NewGuid();
            dbPath = Path.Combine(Directory.GetCurrentDirectory(), $"{nameof(PersistentBlobCacheTests)}-{suffix}.db");
            cache = new PersistentBlobCache(dbPath);
        }

        #endregion Public Constructors

        #region Public Methods

        public void Dispose()
        {
            cache?.Dispose();

            // Clean up test database files
            try
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
                if (File.Exists(dbPath + "-wal"))
                    File.Delete(dbPath + "-wal");
                if (File.Exists(dbPath + "-shm"))
                    File.Delete(dbPath + "-shm");
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

        [Fact]
        public async Task Get_Throws_KeyNotFoundException_When_Key_Does_Not_Exist()
        {
            // Arrange
            await cache.CreateConnection();

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(
                async () => await cache.Get("nonexistent-key", "TestType"));
        }

        [Fact]
        public async Task Get_Throws_KeyNotFoundException_When_Key_Does_Not_Exist_Using_Simple_Overload()
        {
            // Arrange
            await cache.CreateConnection();

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(
                async () => await cache.Get("nonexistent-key"));
        }

        [Fact]
        public async Task Get_Returns_Data_When_Key_Exists()
        {
            // Arrange
            await cache.CreateConnection();
            var testData = Encoding.UTF8.GetBytes("Hello, World!");
            await cache.Insert("existing-key", testData, "TestType");

            // Act
            var result = await cache.Get("existing-key", "TestType");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            Assert.Equal("Hello, World!", Encoding.UTF8.GetString(result));
        }

        [Fact]
        public async Task GetOrDefault_Returns_Empty_Array_When_Key_Does_Not_Exist()
        {
            // Arrange
            await cache.CreateConnection();

            // Act
            var result = await cache.GetOrDefault("nonexistent-key", "TestType");

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetOrDefault_Returns_Data_When_Key_Exists()
        {
            // Arrange
            await cache.CreateConnection();
            var testData = Encoding.UTF8.GetBytes("Test data");
            await cache.Insert("test-key", testData, "TestType");

            // Act
            var result = await cache.GetOrDefault("test-key", "TestType");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Test data", Encoding.UTF8.GetString(result));
        }

        [Fact]
        public async Task Get_Throws_KeyNotFoundException_When_Key_Expired()
        {
            // Arrange
            await cache.CreateConnection();
            var testData = Encoding.UTF8.GetBytes("Expiring data");
            var expiration = DateTimeOffset.UtcNow.AddMilliseconds(-100); // Already expired
            await cache.Insert("expired-key", testData, "TestType", expiration);

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(
                async () => await cache.Get("expired-key", "TestType"));
        }

        [Fact]
        public async Task Get_Returns_Data_When_Key_Not_Yet_Expired()
        {
            // Arrange
            await cache.CreateConnection();
            var testData = Encoding.UTF8.GetBytes("Not expired data");
            var expiration = DateTimeOffset.UtcNow.AddHours(1); // Expires in 1 hour
            await cache.Insert("valid-key", testData, "TestType", expiration);

            // Act
            var result = await cache.Get("valid-key", "TestType");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Not expired data", Encoding.UTF8.GetString(result));
        }

        [Fact]
        public async Task Insert_And_Get_Round_Trip_Works()
        {
            // Arrange
            await cache.CreateConnection();
            var originalData = Encoding.UTF8.GetBytes("Round trip test data");

            // Act
            await cache.Insert("round-trip-key", originalData);
            var retrievedData = await cache.Get("round-trip-key");

            // Assert
            Assert.Equal(originalData.Length, retrievedData.Length);
            Assert.Equal("Round trip test data", Encoding.UTF8.GetString(retrievedData));
        }

        [Fact]
        public async Task Invalidate_Causes_Get_To_Throw_KeyNotFoundException()
        {
            // Arrange
            await cache.CreateConnection();
            var testData = Encoding.UTF8.GetBytes("Data to invalidate");
            await cache.Insert("invalidate-key", testData);

            // Verify key exists first
            var existingData = await cache.Get("invalidate-key");
            Assert.NotEmpty(existingData);

            // Act
            await cache.InvalidateObject("invalidate-key");

            // Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(
                async () => await cache.Get("invalidate-key"));
        }

        [Fact]
        public async Task GetAll_Returns_All_Non_Expired_Items()
        {
            // Arrange
            await cache.CreateConnection();
            await cache.Insert("key1", Encoding.UTF8.GetBytes("value1"));
            await cache.Insert("key2", Encoding.UTF8.GetBytes("value2"));
            await cache.Insert("key3", Encoding.UTF8.GetBytes("value3"));

            // Act
            var results = await cache.GetAll();

            // Assert
            Assert.Equal(3, results.Count());
        }

        [Fact]
        public async Task GetAll_Returns_Empty_When_No_Items()
        {
            // Arrange
            await cache.CreateConnection();

            // Act
            var results = await cache.GetAll();

            // Assert
            Assert.Empty(results);
        }

        [Fact]
        public async Task GetAllKeys_Returns_All_Non_Expired_Keys()
        {
            // Arrange
            await cache.CreateConnection();
            await cache.Insert("key1", Encoding.UTF8.GetBytes("value1"));
            await cache.Insert("key2", Encoding.UTF8.GetBytes("value2"));
            await cache.Insert("key3", Encoding.UTF8.GetBytes("value3"));

            // Act
            var results = await cache.GetAllKeys();
            var keys = results.Select(r => r.Key).ToList();

            // Assert
            Assert.Equal(3, keys.Count);
            Assert.Contains("key1", keys);
            Assert.Contains("key2", keys);
            Assert.Contains("key3", keys);
        }

        [Fact]
        public async Task GetAllKeys_Returns_Empty_When_No_Items()
        {
            // Arrange
            await cache.CreateConnection();

            // Act
            var results = await cache.GetAllKeys();

            // Assert
            Assert.Empty(results);
        }

        [Fact]
        public async Task GetAll_Excludes_Expired_Items()
        {
            // Arrange
            await cache.CreateConnection();
            await cache.Insert("valid-key", Encoding.UTF8.GetBytes("valid"));
            await cache.Insert("expired-key", Encoding.UTF8.GetBytes("expired"),
                DateTimeOffset.UtcNow.AddMilliseconds(-100)); // Already expired

            // Act
            var results = await cache.GetAll();

            // Assert
            Assert.Single(results);
            Assert.Equal("valid", Encoding.UTF8.GetString(results.First()));
        }

        [Fact]
        public async Task GetAllKeys_Excludes_Expired_Keys()
        {
            // Arrange
            await cache.CreateConnection();
            await cache.Insert("valid-key", Encoding.UTF8.GetBytes("valid"));
            await cache.Insert("expired-key", Encoding.UTF8.GetBytes("expired"),
                DateTimeOffset.UtcNow.AddMilliseconds(-100)); // Already expired

            // Act
            var results = await cache.GetAllKeys();
            var keys = results.Select(r => r.Key).ToList();

            // Assert
            Assert.Single(keys);
            Assert.Contains("valid-key", keys);
            Assert.DoesNotContain("expired-key", keys);
        }

        [Fact]
        public async Task Concurrent_Reads_And_Writes_Do_Not_Throw()
        {
            // Arrange
            await cache.CreateConnection();
            const int operations = 150;

            // Act - reads and writes issued against the same connection at the same time.
            // Before reads were serialized against writes, this raced on a shared
            // SQLiteConnection, which sqlite-net does not guard.
            var work = new List<Task>();
            for (var i = 0; i < operations; i++)
            {
                var key = "concurrent-" + i;
                var payload = Encoding.UTF8.GetBytes("value-" + i);

                work.Add(Task.Run(() => cache.Insert(key, payload)));
                work.Add(Task.Run(() => cache.GetOrDefault(key, string.Empty)));
                work.Add(Task.Run(() => cache.GetAllKeys()));
            }

            await Task.WhenAll(work);

            // Assert - every write landed and is readable
            for (var i = 0; i < operations; i++)
            {
                var stored = await cache.Get("concurrent-" + i);
                Assert.Equal("value-" + i, Encoding.UTF8.GetString(stored));
            }
        }

        [Fact]
        public async Task Concurrent_Operations_On_Unopened_Connection_Do_Not_Throw()
        {
            // Arrange - deliberately no CreateConnection() first, so many callers race
            // to open the connection at once.

            // Act
            var work = Enumerable.Range(0, 50)
                .Select(i => Task.Run(async () =>
                {
                    await cache.Insert("cold-" + i, Encoding.UTF8.GetBytes("v" + i));
                    return await cache.GetOrDefault("cold-" + i, string.Empty);
                }))
                .ToArray();

            var results = await Task.WhenAll(work);

            // Assert
            Assert.All(results, r => Assert.NotEmpty(r));
        }

        [Fact]
        public async Task Get_With_Keys_Spanning_Multiple_Chunks_Returns_All_Values()
        {
            // Arrange - the internal chunk size is 950, so this spans three chunks
            await cache.CreateConnection();
            const int count = 2000;

            var items = new Dictionary<string, byte[]>();
            for (var i = 0; i < count; i++)
            {
                items["bulk-" + i] = Encoding.UTF8.GetBytes("payload-" + i);
            }

            await cache.Insert(items);

            // Act
            var fetched = await cache.Get(items.Keys);

            // Assert
            Assert.Equal(count, fetched.Count);
            for (var i = 0; i < count; i++)
            {
                Assert.Equal("payload-" + i, Encoding.UTF8.GetString(fetched["bulk-" + i]));
            }
        }

        [Fact]
        public async Task GetCreatedAt_Enumerates_The_Supplied_Keys_Only_Once()
        {
            // Arrange - GetObjectsCreatedAt walks its keys argument twice. Passing a
            // sequence that refuses a second enumeration pins that behaviour down.
            await cache.CreateConnection();

            var items = new Dictionary<string, byte[]>();
            for (var i = 0; i < 20; i++)
            {
                items["stamped-" + i] = Encoding.UTF8.GetBytes("v" + i);
            }

            await cache.Insert(items);

            // Act
            var stamps = await cache.GetCreatedAt(new SingleUseSequence(items.Keys));

            // Assert - an entry comes back for every key that was supplied
            Assert.Equal(items.Count, stamps.Count);
            foreach (var key in items.Keys)
            {
                Assert.True(stamps.ContainsKey(key));
            }
        }

        [Fact]
        public async Task Concurrent_CreateConnection_Publishes_A_Fully_Initialised_Connection()
        {
            // Arrange - many callers open the connection at once. The connection field is
            // only assigned after the schema exists, so no caller can observe a connection
            // whose CacheItem table has not been created yet.
            var opens = Enumerable.Range(0, 64)
                .Select(_ => Task.Run(() => cache.CreateConnection()))
                .ToArray();

            // Act
            await Task.WhenAll(opens);

            // Assert - the schema is usable immediately afterwards
            await cache.Insert("after-open", Encoding.UTF8.GetBytes("ok"));
            var stored = await cache.Get("after-open");
            Assert.Equal("ok", Encoding.UTF8.GetString(stored));
        }

        #endregion Public Methods

        #region Private Types

        /// <summary>
        /// A sequence that throws if anything walks it more than once, so that code
        /// relying on repeated enumeration of a caller-supplied <see cref="IEnumerable{T}"/>
        /// fails loudly instead of silently returning nothing the second time.
        /// </summary>
        private sealed class SingleUseSequence : IEnumerable<string>
        {
            private readonly IEnumerable<string> source;
            private bool enumerated;

            public SingleUseSequence(IEnumerable<string> source)
            {
                this.source = source;
            }

            public IEnumerator<string> GetEnumerator()
            {
                if (enumerated)
                {
                    throw new InvalidOperationException("Sequence was enumerated more than once.");
                }

                enumerated = true;
                return source.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        #endregion Private Types
    }
}