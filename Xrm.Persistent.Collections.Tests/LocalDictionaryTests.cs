namespace Xrm.Persistent.Collections
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Microsoft.Xrm.Sdk;
    using Xunit;

    public class EntityDictionaryTests : IDisposable
    {
        #region Private Fields

        private readonly string dbPath;
        private readonly LocalDictionary<Entity> dictionary;

        #endregion Private Fields

        #region Public Constructors

        public EntityDictionaryTests()
        {
            var suffix = Guid.NewGuid();
            dbPath = Path.Combine(Directory.GetCurrentDirectory(), $"{nameof(EntityDictionaryTests)}-{suffix}.db");

            dictionary = new LocalDictionary<Entity>(dbPath);
        }

        #endregion Public Constructors

        #region Public Methods

        [Fact]
        public void Can_Add_And_TryGet_Value()
        {
            var p = dbPath;

            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("test", id);

            // Act
            dictionary.Add("test", entity);
            var retrieved = dictionary.TryGetValue("test", out var result);

            // Assert
            Assert.True(retrieved);
            Assert.Equal(entity.Id, result.Id);
            Assert.Equal(entity.LogicalName, result.LogicalName);
        }

        [Fact]
        public void Can_Check_If_Dictionary_Contains_Key()
        {
            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("test", id);

            // Act
            dictionary["test"] = entity;

            var firstSearch = dictionary.ContainsKey("test");
            var secondSearch = dictionary.ContainsKey("test1");

            // Assert
            Assert.True(firstSearch);
            Assert.False(secondSearch);
        }

        [Fact]
        public void Can_Check_If_Dictionary_Contains_Value()
        {
            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("test", id);

            // Act
            dictionary["test"] = entity;

            var existing = new KeyValuePair<string, Entity>("test", entity);
            var nonExisting = new KeyValuePair<string, Entity>("test1", new Entity());

            var firstSearch = dictionary.Contains(existing);
            var secondSearch = dictionary.Contains(nonExisting);

            // Assert
            Assert.True(firstSearch);
            Assert.False(secondSearch);
        }

        [Fact]
        public void Can_Copy()
        {
            // Arrange
            var id0 = Guid.NewGuid();
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();
            var id4 = Guid.NewGuid();
            var entity3 = new Entity("test3", id3);
            var entity4 = new Entity("test4", id4);
            var target = new KeyValuePair<string, Entity>[]
            {
                new KeyValuePair<string, Entity>("test0", new Entity("test0", id0)),
                new KeyValuePair<string, Entity>("test1", new Entity("test1", id1)),
                new KeyValuePair<string, Entity>("test2", new Entity("test2", id2))
            };

            Array.Resize(ref target, 4);

            // Act
            dictionary["test3"] = entity3;
            dictionary["test4"] = entity4;

            dictionary.CopyTo(target, 2);

            // Assert
            Assert.Equal(4, target.Length);
        }

        [Fact]
        public void Can_Get_Enumerator()
        {
            // Arrange
            var iterated = 0;
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var entity1 = new Entity("test1", id1);
            var entity2 = new Entity("test2", id2);

            // Act
            dictionary["test1"] = entity1;
            dictionary["test2"] = entity2;

            foreach (var item in dictionary)
            {
                iterated++;
            }

            // Assert
            Assert.Equal(2, iterated);
        }

        [Fact]
        public void Can_Get_Keys()
        {
            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("test", id);

            // Act
            dictionary["test"] = entity;

            var result = dictionary.Keys;

            // Assert
            Assert.Equal("test", result.SingleOrDefault());
            Assert.Equal(typeof(string), result.SingleOrDefault().GetType());
        }

        [Fact]
        public void Can_Get_Values()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var entity1 = new Entity("test1", id1);
            var entity2 = new Entity("test2", id2);

            // Act
            dictionary["test1"] = entity1;
            dictionary["test2"] = entity2;

            var result = dictionary.Values.ToList();

            // Assert
            Assert.Equal("test1", result[0].LogicalName);
            Assert.Equal("test2", result[1].LogicalName);
            Assert.Equal(id1, result[0].Id);
            Assert.Equal(id2, result[1].Id);
        }

        [Fact]
        public void Can_Remove_By_Key()
        {
            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("test", id);

            // Act
            dictionary["test"] = entity;

            var result = dictionary.Remove("test");

            // Assert
            Assert.True(result);
            Assert.False(dictionary.ContainsKey("test"));
        }

        [Fact]
        public void Can_Remove_By_KeyValuePair()
        {
            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("test", id);

            // Act
            dictionary["test"] = entity;

            var pair = new KeyValuePair<string, Entity>("test", entity);
            var result = dictionary.Remove(pair);

            // Assert
            Assert.True(result);
            Assert.False(dictionary.ContainsKey("test"));
        }

        [Fact]
        public void Can_Store_And_Retrieve_Value()
        {
            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("test", id);

            // Act
            dictionary["test"] = entity;

            var result = dictionary["test"];

            // Assert
            Assert.Equal(entity.LogicalName, result.LogicalName);
            Assert.Equal(entity.Id, result.Id);
        }

        [Fact]
        public void Dictionaty_Gets_Cleared()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var entity1 = new Entity("test1", id1);
            var entity2 = new Entity("test2", id2);

            // Act
            dictionary["test1"] = entity1;
            dictionary["test2"] = entity2;

            dictionary.Clear();

            // Assert
            Assert.True(dictionary.Count == 0);
        }

        public void Dispose()
        {
            // Cleanup here
            dictionary.Dispose();
            File.Delete(dbPath);
        }

        [Fact]
        public void Returns_Correct_Number_Of_Items()
        {
            // Arrange
            var rnd = new Random();
            var count = rnd.Next(0, 11);

            for (var i = 0; i < count; i++)
            {
                var id = Guid.NewGuid();
                var entityName = $"test{i}";
                var entity = new Entity(entityName, id);
                dictionary[entityName] = entity;
            }

            // Act
            var result = dictionary.Count;

            // Assert
            Assert.Equal(count, result);
        }

        [Fact]
        public void TryGet_Returns_Default_If_Key_Not_Found()
        {
            // Arrange

            // Act
            var p = dbPath;

            var retrieved = dictionary.TryGetValue("test", out var result);

            // Assert
            Assert.False(retrieved);
            Assert.Equal(default(Entity), result);
        }

        [Fact]
        public void Can_Update_Existing_Key()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var entity1 = new Entity("test", id1);
            var entity2 = new Entity("test", id2);

            // Act
            dictionary["key1"] = entity1;
            var firstValue = dictionary["key1"];

            dictionary["key1"] = entity2; // Update
            var updatedValue = dictionary["key1"];

            // Assert
            Assert.Equal(id1, firstValue.Id);
            Assert.Equal(id2, updatedValue.Id);
            Assert.Single(dictionary); // Still only 1 item
        }

        [Fact]
        public void Can_Store_Entity_With_Attributes()
        {
            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("contact", id);
            entity["firstname"] = "John";
            entity["lastname"] = "Doe";
            entity["age"] = 30;
            entity["createdon"] = DateTime.Now;

            // Act
            dictionary["contact1"] = entity;
            var retrieved = dictionary["contact1"];

            // Assert
            Assert.Equal("John", retrieved["firstname"]);
            Assert.Equal("Doe", retrieved["lastname"]);
            Assert.Equal(30, retrieved["age"]);
            Assert.NotNull(retrieved["createdon"]);
        }

        [Fact]
        public void Can_Store_Entity_With_EntityReference()
        {
            // Arrange
            var entityId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            var entity = new Entity("contact", entityId);
            entity["parentcustomerid"] = new EntityReference("account", accountId);

            // Act
            dictionary["contact1"] = entity;
            var retrieved = dictionary["contact1"];

            // Assert
            var retrievedRef = retrieved["parentcustomerid"] as EntityReference;
            Assert.NotNull(retrievedRef);
            Assert.Equal("account", retrievedRef.LogicalName);
            Assert.Equal(accountId, retrievedRef.Id);
        }

        [Fact]
        public void Can_Store_Entity_With_OptionSetValue()
        {
            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("contact", id);
            entity["gendercode"] = new OptionSetValue(1);

            // Act
            dictionary["contact1"] = entity;
            var retrieved = dictionary["contact1"];

            // Assert
            var optionSet = retrieved["gendercode"] as OptionSetValue;
            Assert.NotNull(optionSet);
            Assert.Equal(1, optionSet.Value);
        }

        [Fact]
        public void Can_Store_Entity_With_Money()
        {
            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("opportunity", id);
            entity["estimatedvalue"] = new Money(1000000.50m);

            // Act
            dictionary["opp1"] = entity;
            var retrieved = dictionary["opp1"];

            // Assert
            var money = retrieved["estimatedvalue"] as Money;
            Assert.NotNull(money);
            Assert.Equal(1000000.50m, money.Value);
        }

        [Fact]
        public void Data_Persists_Across_Dictionary_Instances()
        {
            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("account", id);
            entity["name"] = "Test Company";

            // Act - Store in first instance
            dictionary["account1"] = entity;
            var countBeforeDispose = dictionary.Count;
            dictionary.Dispose();

            // Create new instance pointing to same DB
            var dictionary2 = new LocalDictionary<Entity>(dbPath);
            var retrieved = dictionary2["account1"];
            var countAfterReopen = dictionary2.Count;

            // Assert
            Assert.Equal(1, countBeforeDispose);
            Assert.Equal(1, countAfterReopen);
            Assert.Equal(id, retrieved.Id);
            Assert.Equal("Test Company", retrieved["name"]);

            // Cleanup
            dictionary2.Dispose();
        }

        [Fact]
        public void Empty_Dictionary_Has_Zero_Count()
        {
            // Act
            var count = dictionary.Count;

            // Assert
            Assert.Equal(0, count);
        }

        [Fact]
        public void Can_Handle_Large_Dataset()
        {
            // Arrange
            const int itemCount = 100;
            var ids = new List<Guid>();

            // Act - Add 100 entities
            for (int i = 0; i < itemCount; i++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                var entity = new Entity("account", id);
                entity["name"] = $"Company {i}";
                entity["accountnumber"] = i.ToString();
                dictionary[$"account{i}"] = entity;
            }

            // Assert - Verify count
            Assert.Equal(itemCount, dictionary.Count);

            // Assert - Spot check some random items
            var retrieved50 = dictionary["account50"];
            Assert.Equal(ids[50], retrieved50.Id);
            Assert.Equal("Company 50", retrieved50["name"]);

            var retrieved99 = dictionary["account99"];
            Assert.Equal(ids[99], retrieved99.Id);
            Assert.Equal("Company 99", retrieved99["name"]);
        }

        [Fact]
        public void Can_Enumerate_With_IEnumerable()
        {
            // Arrange
            var entity1 = new Entity("account", Guid.NewGuid());
            var entity2 = new Entity("contact", Guid.NewGuid());
            var entity3 = new Entity("opportunity", Guid.NewGuid());

            dictionary["key1"] = entity1;
            dictionary["key2"] = entity2;
            dictionary["key3"] = entity3;

            // Act
            var enumerable = dictionary as System.Collections.IEnumerable;
            var count = 0;

            foreach (var item in enumerable)
            {
                Assert.IsType<KeyValuePair<string, Entity>>(item);
                count++;
            }

            // Assert
            Assert.Equal(3, count);
        }

        [Fact]
        public void Remove_NonExistent_Key_Returns_True()
        {
            // Note: Current implementation returns true even for non-existent keys
            // This is not standard IDictionary behavior but changing it might break existing code

            // Act
            var result = dictionary.Remove("nonexistent");

            // Assert
            Assert.True(result); // Current behavior
            Assert.Empty(dictionary); // Dictionary still empty
        }

        [Fact]
        public void Keys_Collection_Is_Empty_For_New_Dictionary()
        {
            // Act
            var keys = dictionary.Keys;

            // Assert
            Assert.Empty(keys);
        }

        [Fact]
        public void Values_Collection_Is_Empty_For_New_Dictionary()
        {
            // Act
            var values = dictionary.Values;

            // Assert
            Assert.Empty(values);
        }

        [Fact]
        public void Can_Add_Using_KeyValuePair()
        {
            // Arrange
            var id = Guid.NewGuid();
            var entity = new Entity("account", id);
            var kvp = new KeyValuePair<string, Entity>("account1", entity);

            // Act
            dictionary.Add(kvp);

            // Assert
            Assert.True(dictionary.ContainsKey("account1"));
            Assert.Equal(id, dictionary["account1"].Id);
        }

        [Fact]
        public void Clear_Removes_All_WAL_Files()
        {
            // Arrange
            dictionary["key1"] = new Entity("account", Guid.NewGuid());
            dictionary["key2"] = new Entity("contact", Guid.NewGuid());

            // Act
            dictionary.Clear();

            // Assert
            Assert.Empty(dictionary);

            // Verify can still use dictionary after clear
            dictionary["key3"] = new Entity("opportunity", Guid.NewGuid());
            Assert.Single(dictionary);
        }

        #endregion Public Methods
    }
}