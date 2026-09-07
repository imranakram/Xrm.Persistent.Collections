namespace Xrm.Persistent.Collections
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Microsoft.Xrm.Sdk;
    using Xunit;

    /// <summary>
    /// Covers <see cref="Interfaces.IBulkDictionary{T}"/> on <see cref="LocalDictionary{T}"/> and the
    /// <see cref="DictionaryExtensions"/> dispatch around it. The batch path issues completely
    /// different SQL from the one-key-at-a-time path - a chunked <c>WHERE Key IN (...)</c> and a
    /// single transaction rather than a statement per key - so it needs its own coverage rather than
    /// leaning on the per-key tests next door.
    /// </summary>
    public class BulkDictionaryTests : IDisposable
    {
        #region Private Fields

        private readonly string dbPath;
        private readonly LocalDictionary<Entity> dictionary;

        #endregion Private Fields

        #region Public Constructors

        public BulkDictionaryTests()
        {
            var suffix = Guid.NewGuid();
            dbPath = Path.Combine(Directory.GetCurrentDirectory(), $"{nameof(BulkDictionaryTests)}-{suffix}.db");

            dictionary = new LocalDictionary<Entity>(dbPath);
        }

        #endregion Public Constructors

        #region Public Methods

        public void Dispose()
        {
            dictionary?.Dispose();

            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }

        [Fact]
        public void Extension_Takes_The_Bulk_Path_Through_The_Interface()
        {
            // Arrange - this is the shape a consumer actually holds: the concrete type is erased
            // behind IDictionary, and the extension has to find IBulkDictionary on it anyway.
            IDictionary<string, Entity> erased = dictionary;
            var entity = new Entity("test", Guid.NewGuid());

            // Act
            erased.SetRange(new Dictionary<string, Entity> { { "a", entity } });
            var actual = erased.GetRange(new[] { "a", "absent" });

            // Assert - reading it back through the per-key indexer proves the write went to the
            // database rather than to some in-memory shim the extension invented.
            Assert.Single(actual);
            Assert.Equal(entity.Id, actual["a"].Id);
            Assert.Equal(entity.Id, dictionary["a"].Id);
        }

        [Fact]
        public void GetRange_Collapses_Duplicate_Keys()
        {
            // Arrange
            dictionary["a"] = new Entity("test", Guid.NewGuid());

            // Act
            var actual = dictionary.GetRange(new[] { "a", "a", "a" });

            // Assert
            Assert.Single(actual);
        }

        [Fact]
        public void GetRange_Crosses_The_Chunk_Boundary()
        {
            // Arrange - the backend chunks at 950 keys per statement, so anything above that
            // exercises more than one round trip and the merge of their results. The chunking is
            // there to stay clear of SQLite's own bound-parameter ceiling, so a single-statement
            // implementation would fail this test rather than silently degrade.
            var expected = Enumerable.Range(0, 2000)
                .ToDictionary(i => i.ToString(), i => new Entity("test", Guid.NewGuid()));

            dictionary.SetRange(expected);

            // Act
            var actual = dictionary.GetRange(expected.Keys);

            // Assert
            Assert.Equal(expected.Count, actual.Count);

            foreach (var pair in expected)
            {
                Assert.Equal(pair.Value.Id, actual[pair.Key].Id);
            }
        }

        [Fact]
        public void GetRange_Matches_TryGetValue_Key_For_Key()
        {
            // Arrange - the batch path is only useful if it is indistinguishable from the loop it
            // replaces, so assert that directly rather than trusting the two to agree.
            var expected = Enumerable.Range(0, 50)
                .ToDictionary(i => i.ToString(), i => new Entity("test", Guid.NewGuid()));

            dictionary.SetRange(expected);

            var keys = expected.Keys.Concat(new[] { "absent" }).ToArray();

            // Act
            var batch = dictionary.GetRange(keys);

            var loop = new Dictionary<string, Entity>();
            foreach (var key in keys)
            {
                if (dictionary.TryGetValue(key, out var value))
                {
                    loop.Add(key, value);
                }
            }

            // Assert
            Assert.Equal(loop.Keys.OrderBy(o => o), batch.Keys.OrderBy(o => o));

            foreach (var pair in loop)
            {
                Assert.Equal(pair.Value.Id, batch[pair.Key].Id);
            }
        }

        [Fact]
        public void GetRange_Omits_Missing_Keys()
        {
            // Arrange
            var present = new Entity("test", Guid.NewGuid());
            dictionary["present"] = present;

            // Act
            var actual = dictionary.GetRange(new[] { "present", "absent" });

            // Assert - a miss is omitted rather than returned as a default value, so the caller can
            // tell "never stored" from "stored a null".
            Assert.Single(actual);
            Assert.True(actual.ContainsKey("present"));
            Assert.False(actual.ContainsKey("absent"));
            Assert.Equal(present.Id, actual["present"].Id);
        }

        [Fact]
        public void GetRange_Returns_Empty_For_An_Empty_Key_Set()
        {
            // Arrange
            dictionary["a"] = new Entity("test", Guid.NewGuid());

            // Act
            var actual = dictionary.GetRange(new string[0]);

            // Assert
            Assert.Empty(actual);
        }

        [Fact]
        public void GetRange_Round_Trips_Crm_Attribute_Types()
        {
            // Arrange - the batch read deserializes through the same converters as the indexer, but
            // it builds its result from a different query, so pin the CRM types down here too.
            var id = Guid.NewGuid();
            var entity = new Entity("test", id);
            entity.Attributes.Add("text", "value");
            entity.Attributes.Add("reference", new EntityReference("test", id));
            entity.Attributes.Add("option", new OptionSetValue(3));
            entity.Attributes.Add("money", new Money(12.5m));

            dictionary.SetRange(new Dictionary<string, Entity> { { "entity", entity } });

            // Act
            var actual = dictionary.GetRange(new[] { "entity" })["entity"];

            // Assert
            Assert.Equal(id, actual.Id);
            Assert.Equal("value", actual["text"]);
            Assert.Equal(id, (actual["reference"] as EntityReference).Id);
            Assert.Equal(3, (actual["option"] as OptionSetValue).Value);
            Assert.Equal(12.5m, (actual["money"] as Money).Value);
        }

        [Fact]
        public void Null_Arguments_Are_Rejected()
        {
            // Arrange - the extension is the reachable entry point, so cast to the interface type
            // rather than letting the compiler bind to the instance methods.
            IDictionary<string, Entity> missing = null;
            IDictionary<string, Entity> erased = dictionary;

            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => missing.GetRange(new[] { "a" }));
            Assert.Throws<ArgumentNullException>(() => missing.SetRange(new Dictionary<string, Entity>()));
            Assert.Throws<ArgumentNullException>(() => erased.GetRange(null));
            Assert.Throws<ArgumentNullException>(() => erased.SetRange(null));
        }

        [Fact]
        public void Plain_Dictionary_Falls_Back_To_A_Loop()
        {
            // Arrange - a ConcurrentDictionary or a plain Dictionary does not implement
            // IBulkDictionary, and the extension has to serve it anyway. This is the case a job
            // engine hits on every run that stays below the persistent-storage threshold.
            IDictionary<string, Entity> plain = new Dictionary<string, Entity>();
            var first = new Entity("test", Guid.NewGuid());
            var second = new Entity("test", Guid.NewGuid());

            // Act
            plain.SetRange(new Dictionary<string, Entity> { { "a", first }, { "b", second } });
            var actual = plain.GetRange(new[] { "a", "b", "a", "absent" });

            // Assert
            Assert.Equal(2, plain.Count);
            Assert.Equal(2, actual.Count);
            Assert.Equal(first.Id, actual["a"].Id);
            Assert.Equal(second.Id, actual["b"].Id);
        }

        [Fact]
        public void Plain_Dictionary_SetRange_Replaces_Existing_Keys()
        {
            // Arrange
            var replacement = new Entity("test", Guid.NewGuid());
            IDictionary<string, Entity> plain = new Dictionary<string, Entity>
            {
                { "a", new Entity("test", Guid.NewGuid()) }
            };

            // Act - Add would throw here; the contract is replace, matching the indexer setter.
            plain.SetRange(new Dictionary<string, Entity> { { "a", replacement } });

            // Assert
            Assert.Single(plain);
            Assert.Equal(replacement.Id, plain["a"].Id);
        }

        [Fact]
        public void SetRange_Accepts_An_Empty_Set()
        {
            // Arrange
            dictionary["a"] = new Entity("test", Guid.NewGuid());

            // Act
            dictionary.SetRange(new Dictionary<string, Entity>());

            // Assert - an empty write is a no-op, not an error and not a truncation.
            Assert.Single(dictionary.Keys);
        }

        [Fact]
        public void SetRange_Is_Visible_To_The_Per_Key_Readers()
        {
            // Arrange - a batch write has to land in the same rows the indexer and ContainsKey read,
            // otherwise mixing the two APIs in one job would silently lose data.
            var entity = new Entity("test", Guid.NewGuid());

            // Act
            dictionary.SetRange(new Dictionary<string, Entity> { { "a", entity } });

            // Assert
            Assert.True(dictionary.ContainsKey("a"));
            Assert.Equal(entity.Id, dictionary["a"].Id);
            Assert.True(dictionary.TryGetValue("a", out var found));
            Assert.Equal(entity.Id, found.Id);
        }

        [Fact]
        public void SetRange_Replaces_Existing_Keys()
        {
            // Arrange
            var original = new Entity("test", Guid.NewGuid());
            var replacement = new Entity("test", Guid.NewGuid());
            dictionary["a"] = original;

            // Act
            dictionary.SetRange(new Dictionary<string, Entity> { { "a", replacement } });

            // Assert
            Assert.Single(dictionary.Keys);
            Assert.Equal(replacement.Id, dictionary["a"].Id);
        }

        [Fact]
        public void SetRange_Survives_A_New_Dictionary_Instance()
        {
            // Arrange
            var expected = Enumerable.Range(0, 10)
                .ToDictionary(i => i.ToString(), i => new Entity("test", Guid.NewGuid()));

            dictionary.SetRange(expected);

            // Act - reopen the same file, so this asserts the transaction actually committed.
            using (var reopened = new LocalDictionary<Entity>(dbPath))
            {
                var actual = reopened.GetRange(expected.Keys);

                // Assert
                Assert.Equal(expected.Count, actual.Count);

                foreach (var pair in expected)
                {
                    Assert.Equal(pair.Value.Id, actual[pair.Key].Id);
                }
            }
        }

        #endregion Public Methods
    }
}
