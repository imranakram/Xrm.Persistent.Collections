# Xrm.Persistent.Collections

[![NuGet](https://img.shields.io/nuget/v/Xrm.Persistent.Collections.svg)](https://www.nuget.org/packages/Xrm.Persistent.Collections)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**SQLite-backed persistent dictionary storage for Microsoft Dynamics CRM/XRM applications** that survives process restarts and provides disk-based caching for long-running operations.

Built on top of SQLite with automatic JSON serialization of Dynamics 365 entities using [Xrm.Json.Serialization](https://github.com/imranakram/Xrm.Json.Serialization), which now supports **AliasedValue** (FetchXML linked entities), **OptionSetValueCollection** (multi-select picklists), and **BooleanManagedProperty** in addition to all standard CRM data types.

---

## 🚀 Features

- ✅ **Persistent Storage**: Data survives application restarts
- ✅ **Type-Safe**: Generic dictionary implementation `LocalDictionary<T>`
- ✅ **CRM Native**: Full support for all Dynamics 365 data types via [Xrm.Json.Serialization v1.2026.3.1](https://github.com/imranakram/Xrm.Json.Serialization)
  - Entity, EntityReference, EntityCollection
  - OptionSetValue, Money, DateTime, Guid
  - **NEW:** AliasedValue (FetchXML linked entities)
  - **NEW:** OptionSetValueCollection (multi-select picklists)
  - **NEW:** BooleanManagedProperty
- ✅ **High Performance**: SQLite with WAL mode for concurrent access (10-15% faster than v1.x)
- ✅ **Simple API**: Standard `IDictionary<string, T>` interface
- ✅ **Cache Introspection**: `GetAll()` and `GetAllKeys()` methods for querying all cached items
- ✅ **Thread-Safe**: Built-in synchronization for multi-threaded scenarios
- ✅ **Expiration Support**: Automatic cleanup of expired items with configurable TTL
- ✅ **.NET Framework 4.8**: Latest framework with TLS 1.2/1.3 support

---

## 📦 Installation

```powershell
Install-Package Xrm.Persistent.Collections
```

### Requirements
- .NET Framework 4.8
- Microsoft.CrmSdk.CoreAssemblies 9.0.2.60+
- Dynamics 365 Online or OnPrem 9.1+

---

## 📖 Quick Start

```csharp
using Xrm.Persistent.Collections;
using Microsoft.Xrm.Sdk;

// Create a persistent dictionary backed by SQLite
using (var dict = new LocalDictionary<Entity>("data.db"))
{
    // Store an entity
    var account = new Entity("account", Guid.NewGuid());
    account["name"] = "Contoso";
    account["revenue"] = new Money(1000000);

    dict["account1"] = account;

    // Retrieve it later (even after application restart!)
    var retrieved = dict["account1"];
    Console.WriteLine(retrieved["name"]); // Output: Contoso
}
```

---

## 💡 Use Cases & Scenarios

### 1️⃣ **Long-Running Job Engines**
Store job state, checkpoints, and progress to survive crashes or restarts:

```csharp
using (var jobState = new LocalDictionary<Entity>("jobs.db"))
{
    foreach (var entity in entities)
    {
        // Process entity
        ProcessEntity(entity);

        // Save checkpoint - resume from here if job crashes
        jobState["lastProcessed"] = entity;
    }
}
```

**Why this is useful:**
- Job crashes don't mean starting from scratch
- Resume processing from exact checkpoint
- Track progress across multiple runs
- Perfect for bulk data migration, ETL processes

### 2️⃣ **Offline-First Applications**
Cache Dynamics 365 data locally for offline access:

```csharp
using (var cache = new LocalDictionary<Entity>("offline-cache.db"))
{
    // Online: Fetch and cache data
    var accounts = service.RetrieveMultiple(query);
    foreach (var account in accounts.Entities)
    {
        cache[account.Id.ToString()] = account;
    }

    // Offline: Read from cache
    var cachedAccount = cache[accountId.ToString()];
    DisplayAccountDetails(cachedAccount);
}
```

**Why this is useful:**
- Work without internet connectivity
- Reduce API calls to Dynamics 365 (avoid throttling)
- Faster data access (local disk vs. network)
- Ideal for field service scenarios

### 3️⃣ **Incremental Sync & Change Tracking**
Track what's been synchronized to avoid re-processing:

```csharp
using (var syncState = new LocalDictionary<DateTime>("sync-state.db"))
{
    var lastSync = syncState.ContainsKey("lastSyncDate") 
        ? syncState["lastSyncDate"] 
        : DateTime.MinValue;

    // Fetch only changed records since last sync
    var query = $@"<fetch>
        <entity name='account'>
            <filter>
                <condition attribute='modifiedon' operator='gt' value='{lastSync:yyyy-MM-dd}' />
            </filter>
        </entity>
    </fetch>";

    var changes = service.RetrieveMultiple(new FetchExpression(query));
    ProcessChanges(changes);

    syncState["lastSyncDate"] = DateTime.UtcNow;
}
```

**Why this is useful:**
- Efficient delta syncs
- Avoid processing unchanged data
- Reduce API load and improve performance
- Perfect for integration scenarios

### 4️⃣ **Complex Entity Caching with Linked Entities (FetchXML)**
Cache FetchXML query results with related entities using AliasedValue support:

```csharp
using (var cache = new LocalDictionary<Entity>("fetchxml-cache.db"))
{
    // FetchXML query with linked entities
    var fetchXml = @"<fetch>
        <entity name='account'>
            <attribute name='name' />
            <link-entity name='contact' from='parentcustomerid' to='accountid' alias='primarycontact'>
                <attribute name='fullname' />
                <attribute name='emailaddress1' />
            </link-entity>
        </entity>
    </fetch>";

    var results = service.RetrieveMultiple(new FetchExpression(fetchXml));

    // Cache entities with linked data (AliasedValue preserved!)
    foreach (var entity in results.Entities)
    {
        cache[entity.Id.ToString()] = entity;
        // Entity includes "primarycontact.fullname" as AliasedValue
    }

    // Later: Retrieve with all linked data intact
    var cachedEntity = cache[accountId.ToString()];
    var contactName = cachedEntity.GetAliasedValue<string>("primarycontact.fullname");
}
```

**Why this is useful:**
- Preserve complex FetchXML query results
- Avoid expensive re-queries with joins
- Cache reports and dashboards data
- Xrm.Json.Serialization v1.2026.3+ handles AliasedValue automatically!

### 5️⃣ **Multi-Select Picklist (OptionSetValueCollection) Support**
Store entities with multi-select picklists:

```csharp
using (var dict = new LocalDictionary<Entity>("multiselect.db"))
{
    var account = new Entity("account", Guid.NewGuid());
    account["name"] = "Contoso";

    // Multi-select picklist (new in Dynamics 365)
    account["industry_categories"] = new OptionSetValueCollection(new[] { 
        new OptionSetValue(1), // Manufacturing
        new OptionSetValue(3), // Technology
        new OptionSetValue(5)  // Services
    });

    dict["account1"] = account;

    // Retrieve and read multi-select values
    var retrieved = dict["account1"];
    var categories = (OptionSetValueCollection)retrieved["industry_categories"];
    Console.WriteLine($"Categories: {string.Join(", ", categories.Select(o => o.Value))}");
}
```

**Why this is useful:**
- Full support for modern Dynamics 365 multi-select fields
- Previously required custom serialization logic
- Xrm.Json.Serialization v1.2026.3+ handles this automatically!

### 6️⃣ **Session State Persistence**
Store user session data that persists across application restarts:

```csharp
using (var session = new LocalDictionary<Dictionary<string, object>>("session.db"))
{
    // Store session state
    session["user123"] = new Dictionary<string, object>
    {
        { "lastActivity", DateTime.UtcNow },
        { "viewedRecords", new List<Guid> { id1, id2, id3 } },
        { "preferences", new { theme = "dark", pageSize = 50 } }
    };

    // Later (even after restart): Restore session
    var userData = session["user123"];
}
```

**Why this is useful:**
- Preserve user context across sessions
- Better user experience
- Useful for desktop applications or Windows Services

### 7️⃣ **Error Recovery & Replay**
Store failed operations for retry logic:

```csharp
using (var errorQueue = new LocalDictionary<Entity>("failed-ops.db"))
{
    try
    {
        service.Update(entity);
    }
    catch (Exception ex)
    {
        // Store for later retry
        errorQueue[entity.Id.ToString()] = entity;
        LogError(ex);
    }

    // Retry logic (scheduled job or manual trigger)
    foreach (var key in errorQueue.Keys.ToList())
    {
        try
        {
            var entity = errorQueue[key];
            service.Update(entity);
            errorQueue.Remove(key); // Success - remove from queue
        }
        catch { /* Will retry next time */ }
    }
}
```

**Why this is useful:**
- Guaranteed operation retry
- Durable queue for failed operations
- No data loss during transient errors

### 8️⃣ **Batch Processing with State Management**
Process large datasets in batches with persistent state:

```csharp
using (var batchState = new LocalDictionary<int>("batch-progress.db"))
{
    const int batchSize = 500;
    int currentBatch = batchState.ContainsKey("currentBatch") ? batchState["currentBatch"] : 0;

    while (true)
    {
        var entities = FetchBatch(currentBatch, batchSize);
        if (!entities.Any()) break;

        ProcessBatch(entities);

        // Save progress after each batch
        batchState["currentBatch"] = ++currentBatch;
    }
}
```

**Why this is useful:**
- Process millions of records safely
- Survive crashes without losing progress
- Throttle-aware processing (Dynamics 365 API limits)

### 9️⃣ **Cache Introspection & Monitoring**
Query all cached items without knowing keys in advance:

```csharp
using (var cache = new LocalDictionary<Entity>("monitoring.db"))
{
    // Get all cached items
    var allItems = await cache.GetAll();
    Console.WriteLine($"Total cached items: {allItems.Count()}");

    // Get all keys with type information
    var allKeys = await cache.GetAllKeys();
    foreach (var keyInfo in allKeys)
    {
        Console.WriteLine($"Key: {keyInfo.Key}, Type: {keyInfo.Type?.Name}");
    }

    // Use in reporting or diagnostics
    var reportData = new Dictionary<string, object>
    {
        { "totalCached", allItems.Count() },
        { "cacheSize", allItems.Sum(item => item.Length) / 1024.0, " KB" },
        { "keyCount", allKeys.Count() },
        { "lastUpdated", DateTime.UtcNow }
    };
}
```

**Why this is useful:**
- Monitor cache health and size
- Audit what's been cached
- Generate cache statistics and reports
- Implement cache warming strategies
- Debug what's actually in the cache

---

## 🔧 Advanced Features

### Thread-Safe Operations
Built-in synchronization allows safe multi-threaded access:

```csharp
using (var dict = new LocalDictionary<Entity>("shared.db"))
{
    Parallel.ForEach(entities, entity =>
    {
        dict[entity.Id.ToString()] = entity; // Thread-safe
    });
}
```

### Enumeration Support
Standard dictionary operations work as expected:

```csharp
using (var dict = new LocalDictionary<Entity>("data.db"))
{
    // Count
    Console.WriteLine($"Total items: {dict.Count}");

    // Keys
    foreach (var key in dict.Keys)
    {
        Console.WriteLine(key);
    }

    // Values
    foreach (var entity in dict.Values)
    {
        Console.WriteLine(entity.LogicalName);
    }

    // Key-Value pairs
    foreach (var kvp in dict)
    {
        Console.WriteLine($"{kvp.Key}: {kvp.Value["name"]}");
    }
}
```

---

## 🎯 When to Use This Library

| Scenario | Use Xrm.Persistent.Collections | Use In-Memory Collections |
|----------|--------------------------------|---------------------------|
| Long-running processes (hours/days) | ✅ Yes | ❌ No |
| Must survive crashes/restarts | ✅ Yes | ❌ No |
| Large datasets (MB/GB) | ✅ Yes | ⚠️ Limited |
| Cross-process data sharing | ✅ Yes | ❌ No |
| High-frequency writes (ms) | ⚠️ Limited | ✅ Yes |
| Temporary data (minutes) | ❌ No | ✅ Yes |

---

## 📚 Dependencies & Compatibility

### Xrm.Json.Serialization v1.2026.9.0
This library uses the latest version of Xrm.Json.Serialization with major enhancements:

#### Serialization Performance
1.2026.9.0 fixes a per-call `ContractResolver` allocation that was discarding Newtonsoft's
contract cache and re-resolving every type by reflection on every entity. Serializing 100 000
single-entity lists went from 82.31 s to 0.34 s, with byte-identical output. Nothing in the
JSON format or the public API changed.

#### New Data Type Support
- **AliasedValue**: FetchXML queries with linked entities are now fully supported
- **OptionSetValueCollection**: Multi-select picklists work seamlessly
- **BooleanManagedProperty**: Managed properties serialize correctly

#### Compact JSON Format
Entities are serialized in a compact, readable format:

```json
{
  "_reference": "account:12345678-1234-1234-1234-123456789012",
  "name": "Contoso Ltd",
  "revenue": { "_money": 1000000 },
  "industrycode": { "_option": 1 },
  "parentaccountid": { "_reference": "account:87654321-4321-4321-4321-210987654321" },
  "createdon": "2024-01-15T10:30:00Z",
  "contact.fullname": { "_aliased": "contact|fullname|John Doe" },
  "categories": { "_options": [1, 2, 3] }
}
```

### Runtime Requirements
- **.NET Framework 4.8**
- **Dynamics 365 Online** (all versions)
- **Dynamics 365 OnPrem 9.1+**
- **Dynamics CRM 2016+**

### Key Dependencies
| Package | Version | Purpose |
|---------|---------|---------|
| Xrm.Json.Serialization | 1.2026.9 | CRM entity serialization |
| sqlite-net-pcl | 1.9.172 | SQLite ORM |
| SQLitePCLRaw.bundle_green | 2.1.11 | Provider initialisation (`batteries_v2`) |
| SQLitePCLRaw.core | 2.1.11 | Managed SQLite core |
| SQLitePCLRaw.provider.dynamic_cdecl | 2.1.11 | Native binding shim |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.13 | Native SQLite binary (SQLite 3.53.3, CVE-2025-6965 floor) |
| Newtonsoft.Json | 13.0.4 | JSON serialization |
| Microsoft.CrmSdk.CoreAssemblies | 9.0.2.60 | Dynamics 365 SDK |

---

## 🎓 API Reference

### Constructor
```csharp
var dict = new LocalDictionary<T>(string databasePath)
```

### IDictionary<string, T> Implementation
```csharp
// Add/Update
dict["key"] = value;
dict.Add("key", value);

// Retrieve
var value = dict["key"];
bool found = dict.TryGetValue("key", out var value);

// Remove
dict.Remove("key");

// Check existence
bool exists = dict.ContainsKey("key");

// Enumerate
int count = dict.Count;
ICollection<string> keys = dict.Keys;
ICollection<T> values = dict.Values;

// Iterate
foreach (var kvp in dict)
{
    Console.WriteLine($"{kvp.Key}: {kvp.Value}");
}

// Cleanup
dict.Clear();
dict.Dispose();
```

### Bulk Operations

`LocalDictionary<T>` implements `IBulkDictionary<T>`, which reads and writes many keys per
round trip instead of one:

```csharp
using Xrm.Persistent.Collections.Interfaces;

// Read many keys in one go. Keys that are not present are omitted from the result,
// the same way TryGetValue reports a miss. Duplicate keys collapse to one entry.
IDictionary<string, Entity> found = dict.GetRange(new[] { "a", "b", "c" });

// Write many keys in one go. Existing keys are replaced, like the indexer setter.
dict.SetRange(new Dictionary<string, Entity>
{
    ["a"] = first,
    ["b"] = second
});
```

If you hold the value as an `IDictionary<string, T>` — so you cannot tell whether it is an
in-memory dictionary or a persistent one — use the extension methods instead. They take the
batch path when the target supports it and fall back to a per-key loop when it does not:

```csharp
using Xrm.Persistent.Collections;

IDictionary<string, Entity> maybePersistent = GetCache();

var found = maybePersistent.GetRange(keys);   // batched if persistent, looped if not
maybePersistent.SetRange(items);              // same
```

Batch in blocks rather than passing 100 000 keys at once. The backend chunks at 950 keys per
SQL statement, but the whole result set is materialised in memory, so a block of about 1 000
keeps both bounded.

### Cache Introspection Methods
```csharp
// Get all non-expired items (raw byte arrays)
var allItems = await cache.GetAll();
var count = allItems.Count();

// Get all non-expired keys with type metadata
var allKeys = await cache.GetAllKeys();
foreach (var keyInfo in allKeys)
{
    Console.WriteLine($"Key: {keyInfo.Key}, Type: {keyInfo.Type?.Name}");
}
```

---

## 🛠️ Best Practices

### 1. Always Dispose
```csharp
// Use 'using' statement to ensure proper cleanup
using (var dict = new LocalDictionary<Entity>("data.db"))
{
    // Your code here
} // Automatically disposed
```

### 2. Choose Meaningful Database Names
```csharp
// Good - descriptive names
var jobQueue = new LocalDictionary<Entity>("job-queue.db");
var syncState = new LocalDictionary<DateTime>("sync-checkpoints.db");

// Avoid - generic names
var dict = new LocalDictionary<Entity>("data.db");
```

### 3. Handle Large Datasets Efficiently
```csharp
// Process in batches instead of loading all values at once
using (var dict = new LocalDictionary<Entity>("large-dataset.db"))
{
    foreach (var key in dict.Keys.Take(100))
    {
        var entity = dict[key];
        ProcessEntity(entity);
    }
}
```

### 4. Use Separate Databases for Different Concerns
```csharp
// Separate concerns = easier maintenance
var userCache = new LocalDictionary<Entity>("user-cache.db");
var jobQueue = new LocalDictionary<Entity>("job-queue.db");
var errorLog = new LocalDictionary<Entity>("errors.db");
```

---

## 📊 Performance Characteristics

- **Read operations**: ~0.5-2ms per item (depends on entity size)
- **Write operations**: ~1-5ms per item (WAL mode optimized)
- **Enumeration**: ~100-500ms for 1,000 items
- **Storage overhead**: ~15-25% JSON + SQLite indexes
- **Concurrent reads**: Excellent (WAL mode)
- **Concurrent writes**: Serialized (SQLite limitation)

### Per-key access does not scale

Every indexer or `ContainsKey` call is its own SQL round trip, so a per-key loop is linear in
round trips rather than in rows. Measured on .NET Framework 4.8 x64, 100 000 keys holding an
`IList<Entity>` of five attributes each, warm database:

| | Per-key loop | `GetRange` / `SetRange` in blocks of 1 000 |
|---|---|---|
| Reads | ~92 s | **1.27 s** |
| Writes | ~78 s | **~3 s** |

For scale, raw SQLite for the same 100 000 rows is ~12 s in blocks of 1 000 and ~2 s in one
transaction — so on the write side the cost was never the database. It was JSON serialization,
which is why the `Xrm.Json.Serialization` 1.2026.9 floor matters as much as the batching does.

### Performance Tips
- Use `GetRange()` / `SetRange()` instead of a per-key loop — this is the single biggest win
- Batch in blocks of about 1 000 keys rather than one call for everything
- Avoid enumerating `Values` for large datasets
- Use `ContainsKey()` instead of `TryGetValue()` when you only need existence check
- Keep entity sizes reasonable (<1 MB per entity)

---

## 🔄 Migration from v1.x

If upgrading from the old `Innofactor.Xrm.Persistent.Collections`:

```csharp
// OLD (v1.x)
using Innofactor.Xrm.Persistent.Collections;

// NEW (v2.x)
using Xrm.Persistent.Collections;
```

**That's it!** Your existing `.db` files work without any changes. See [CHANGELOG.md](CHANGELOG.md) for full migration guide.

---

## 📘 Documentation

- **[CHANGELOG.md](CHANGELOG.md)** - Version history and migration guide
- **[UPGRADE_SUMMARY.md](UPGRADE_SUMMARY.md)** - Detailed upgrade information
- **[KNOWN_ISSUES_AND_ROADMAP.md](KNOWN_ISSUES_AND_ROADMAP.md)** - Future improvements
- **[QUICK_REFERENCE.md](QUICK_REFERENCE.md)** - Integration checklist

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## 👥 Authors

- **Alexey Shytikov** - Original Akavache inspiration
- **Jonas Rapp** - Original Innofactor implementation
- **Imran Akram** - Current maintainer (v2.x)

---

## 🔗 Related Projects

- **[Xrm.Json.Serialization](https://github.com/imranakram/Xrm.Json.Serialization)** - Compact JSON serialization for Dynamics 365 entities (dependency)
- **[Akavache](https://github.com/reactiveui/Akavache)** - Original inspiration for persistent caching

---

## 🐛 Support

- **Issues**: [GitHub Issues](https://github.com/imranakram/Xrm.Persistent.Collections/issues)
- **Discussions**: [GitHub Discussions](https://github.com/imranakram/Xrm.Persistent.Collections/discussions)
- **NuGet**: [NuGet Package](https://www.nuget.org/packages/Xrm.Persistent.Collections)

---

*Version: 2.2026.9.8 | Assembly: 2.0.0.0 | Framework: .NET Framework 4.8 | License: MIT | Tests: 62 passing*
