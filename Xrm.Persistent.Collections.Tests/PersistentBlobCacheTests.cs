namespace Xrm.Persistent.Collections.Backend
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
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

        #endregion Public Methods
    }
}
