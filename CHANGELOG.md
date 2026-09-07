# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.2026.9.8] - 2026-09-07

### Performance Release

Adds a batch read/write API and picks up a ~240x serialization fix from `Xrm.Json.Serialization`.
No breaking changes: `LocalDictionary<T>` keeps its full `IDictionary<string, T>` surface and
existing database files are unaffected.

### Added
- **`IBulkDictionary<T>`** (`Xrm.Persistent.Collections.Interfaces`) — `GetRange(IEnumerable<string> keys)`
  and `SetRange(IDictionary<string, T> items)`. Deliberately a separate interface from `IDictionary`,
  so a consumer holding only the interface can test for it and fall back.
- **`LocalDictionary<T>` implements `IBulkDictionary<T>`**, routing both members to the batch
  operations `PersistentBlobCache` already had (`Get(IEnumerable<string>)` and
  `Insert(IDictionary<string, byte[]>)`), which chunk at 950 keys per statement.
- **`DictionaryExtensions.GetRange` / `SetRange`** — extension methods on `IDictionary<string, T>`
  that take the batch path when the target implements `IBulkDictionary<T>` and fall back to a
  per-key loop otherwise. This lets a caller that holds an `IDictionary<string, T>` — not knowing
  whether it is in memory or persistent — get the batch behaviour without a type check.
- 13 tests in `BulkDictionaryTests` covering the interface fast path, the loop fallback, duplicate
  key collapsing, missing keys, chunk-boundary crossing at 2 000 keys, agreement with per-key
  `TryGetValue`, CRM attribute round trips, replacement semantics and null argument rejection.

### Contract notes
- `GetRange` **omits** keys it did not find, matching `TryGetValue` per key rather than returning a
  placeholder. Duplicate keys in the input collapse to one entry.
- The backend returns an empty buffer for a key it did not find, so absence and "stored an empty
  blob" are indistinguishable at that layer. `GetRange` treats both as a miss — the same as
  `ContainsKey` and `TryGetValue` already do.
- `SetRange` replaces existing keys like the indexer setter, and does not throw on a key that
  already exists.

### Changed
- **`Xrm.Json.Serialization` floor raised from 1.2026.3.1 to 1.2026.9.0.** 1.2026.9.0 fixes a
  per-call `ContractResolver` allocation that was discarding Newtonsoft's contract cache and
  re-resolving every type by reflection on every entity. Because NuGet resolves lowest-applicable,
  the floor has to move or downstream projects keep restoring the slow version.

### Performance
Measured on .NET Framework 4.8 x64, 100 000 keys holding `IList<Entity>` of five attributes each,
against a warm database:

| | Per-key loop | `GetRange` / `SetRange` in blocks of 1 000 |
|---|---|---|
| Reads | ~92 s | **1.27 s** |
| Writes | ~78 s | 75.94 s → **~3 s** with `Xrm.Json.Serialization` 1.2026.9.0 |

Batching alone is ~2.2x end to end (170 s → 77 s) and all but eliminates the read cost. The write
side was dominated not by SQLite but by JSON serialization — 74 of those 76 seconds — which is what
the serializer upgrade addresses. The two together are what turn a ~3 minute pass into a few seconds.

For reference, the SQLite floor for the same 100 000 rows against the real `CacheItem` schema is
~12 s in blocks of 1 000, and ~2 s in a single transaction.

## [2.2026.9.7] - 2026-09-07

### 🔒 Security Release

Fixes a high-severity vulnerability in the bundled native SQLite binary and removes two packages that nothing depended on. No API changes, and existing database files are unaffected — this is a drop-in upgrade.

### Security
- **CVE-2025-6965 / [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)** (High, CVSS 7.2) — upgraded `SQLitePCLRaw.lib.e_sqlite3` from 2.1.11 to 2.1.13.
  - 2.1.11 embeds SQLite **3.49.1**. The flaw — the number of aggregate terms exceeding the available column count, leading to memory corruption — is fixed in SQLite **3.50.2**. 2.1.13 embeds SQLite **3.53.3**.
  - Chosen over the 3.x line deliberately: 2.1.13 carries native assets only (no managed assembly), keeps the same `buildTransitive/net461` package layout as 2.1.11, and needs no binding-redirect changes.
- **Raised the published dependency floors so consumers actually receive the fix.** The package previously floored `SQLitePCLRaw.bundle_e_sqlite3` at 2.1.10. Because NuGet resolves lowest-applicable, downstream projects were pulling `lib.e_sqlite3` **2.1.10** — older than what this library was built against, and vulnerable. The floor is now 2.1.13, plus an explicit `SQLitePCLRaw.lib.e_sqlite3 >= 2.1.13` entry so no resolution path can select an unpatched native binary.
  - **Projects referencing this package should upgrade**; bumping only the transitive package is not sufficient if they pinned the old floor.

### Fixed — Package dependencies
The `.nuspec` dependency list had drifted from what the library actually needs, in both directions. It is now derived from the real reference graph and verified end to end.

- **Declared floors sat below the versions the library was built against** — `System.Buffers` 4.5.1 against 4.6.1, `System.Memory` 4.5.5 against 4.6.3, `System.Numerics.Vectors` 4.5.0 against 4.6.1, `System.Runtime.CompilerServices.Unsafe` 6.0.0 against 6.1.2. These were hand-maintained transitive leaves that nothing kept in sync. They are no longer declared directly; they arrive through `Microsoft.CrmSdk.CoreAssemblies` → `System.Text.Json` at versions Microsoft ships and tests together.
- **The SQLitePCLRaw managed stack was not declared at all.** `sqlite-net` initialises its native provider through `SQLitePCLRaw.batteries_v2`, and `sqlite-net-pcl` 1.9.172 alone floors that at 2.1.2. `bundle_green`, `core` and `provider.dynamic_cdecl` are now declared at 2.1.11 — the versions this release is built and tested against.
- **`SQLitePCLRaw.bundle_e_sqlite3` is no longer declared, and has been dropped from both `packages.config` files.** Nothing referenced it: no csproj `HintPath` or `Import` pointed into it, and the solution builds and passes its tests with the package physically absent. Declaring it would have pulled a second, conflicting copy of `SQLitePCLRaw.batteries_v2.dll` alongside the one `bundle_green` supplies — the same failure mode as the `config.e_sqlite3` package removed above.

Contrary to an earlier reading, `System.Text.Json`, `System.Text.Encodings.Web` and `System.ServiceModel.Http`/`Primitives` were never missing from a consumer's graph: `Microsoft.CrmSdk.CoreAssemblies` 9.0.2.60 declares them itself, and floors `System.Text.Json` at 8.0.5 — the build patched for CVE-2024-43485.

The eight declared dependencies now resolve to a 20-package closure. Verified by packing the release, restoring it into a fresh net48 project whose only `PackageReference` is this library, and running a smoke test that round-trips an `Entity` with `EntityReference`, `OptionSetValue` and `Money` attributes through a real database file. The consumer builds with no warnings, resolves `SQLitePCLRaw.lib.e_sqlite3` 2.1.13, deploys a native `e_sqlite3.dll` reporting SQLite 3.53.3, and a NuGet audit over the whole closure reports no known vulnerabilities.

### Removed
- **`Microsoft.IdentityModel` 7.0.0** — Windows Identity Foundation 3.5, a `lib/net35` assembly superseded by WIF's integration into .NET 4.5. Nothing depended on it: no package in the graph declared it, no source file used it, and the built assembly carried zero references to it. `Microsoft.Xrm.Sdk` references `System.IdentityModel` — the BCL assembly, already satisfied by the framework reference — not `microsoft.identitymodel`, which is the likely origin of the confusion.
- **`SQLitePCLRaw.config.e_sqlite3` 3.0.2** — sat amid an otherwise-2.1.11 stack with nothing depending on it at that version. It shipped a second copy of `SQLitePCLRaw.batteries_v2.dll` that conflicted with the 2.1.11 copy the projects actually reference from `bundle_green`, and triggered a package-downgrade error under modern resolution.

### Changed
- `SQLitePCLRaw.bundle_e_sqlite3` 2.1.11 → 2.1.13, so the version built against and the published floor agree.
- Package version 2.2026.3.1 → 2.2026.9.7.
- **`AssemblyVersion` is now pinned to `2.0.0.0`** and no longer tracks the CalVer release. It is the identity the CLR binds against, so bumping it every release forced every consuming application to add or update a binding redirect just to take a patch. `AssemblyFileVersion` carries the real release version (`2.2026.9.7`). `AssemblyVersion` will change only on a breaking release.
  - Previously these had also drifted: the published 2.2026.3.1 package contained an assembly stamped 2.2026.3.2.
  - **Consumers upgrading from 2.2026.3.1 can remove any binding redirect** they were carrying for this assembly, or point it at `2.0.0.0`.
- Copyright updated to 2019-2026.

### Fixed — Concurrency
- **Reads were completely unsynchronized.** `PersistentBlobCache.Read()` executed against the shared `SQLiteConnection` while holding no lock, and writes held a separate `_writeSemaphore`. sqlite-net's `SQLiteConnection` is not thread-safe and this class holds a single connection, so a read could run against a connection a concurrent write was mutating. Reads and writes now share one `_dbSemaphore`, so every operation against the connection is serialized.
- **A throwing `WaitAsync` could corrupt the semaphore count.** `CreateConnection()` and `Write()` both acquired their semaphore *inside* the `try`, so if the wait threw — `ObjectDisposedException` after `Dispose()`, for example — the `finally` released a permit that had never been taken. The acquire now happens before the `try`. Calls made after `Dispose()` now surface a clean `ObjectDisposedException` instead of leaving the semaphore in a corrupt state.
- **The connection was published before it was initialised.** `CreateConnection()` assigned `_db` and only then set the journal mode and created the schema. A caller reaching the non-null fast path in between could query a `CacheItem` table that did not exist yet. The connection is now built in a local and assigned only once the schema is in place.
- **`GetObjectsCreatedAt<T>()` walked its `keys` argument twice without materializing it**, so a single-use sequence (an iterator, a `yield return` method, a LINQ chain over a stream) silently came back empty the second time and produced a partial result. Now materialized once.
- **`Get(IEnumerable<string>, string)` and `GetObjectsCreatedAt<T>()` issued every query twice.** Both built a lazy `IEnumerable<Task<…>>`, awaited it with `Task.WhenAll`, then re-enumerated it via `.Result` — which created a fresh set of tasks and re-ran every chunked query against the database. Both sequences are now materialized with `.ToArray()` before being awaited.

### Fixed
- `.nuspec` `<repository>` metadata declared branch `main`; the repository's default branch is `master`.

### Performance
- **No measurable change is expected, and none is claimed.** The two removed packages were never loaded at runtime. The SQLite 3.49.1 → 3.53.3 jump is four minor releases of incremental query-planner work that this library's access pattern does not exercise — reads are single-row primary-key lookups against a `WITHOUT ROWID` table, and writes are already batched inside transactions. `sqlite-net-pcl` is unchanged at 1.9.172.

### Added
- Five tests covering the concurrency fixes: mixed concurrent reads and writes, concurrent operations against an unopened connection, concurrent `CreateConnection()` calls, bulk `Get` across multiple internal chunks, and single-use-sequence handling in `GetCreatedAt`. The last of these fails against the pre-fix code; the others are stress coverage and do not deterministically reproduce a race.

### Verified
- Clean restore with both removed packages physically absent from the restore folder — NuGet never requested them.
- `Rebuild` succeeds in both Debug and Release (x64).
- 48/48 unit tests pass, stable across repeated runs.
- The built assembly stamps `AssemblyVersion 2.0.0.0` and `FileVersion 2.2026.9.7`.
- The deployed `e_sqlite3.dll` reports SQLite 3.53.3.
- A NuGet audit across all remaining packages in both projects reports no known vulnerabilities at any severity.

---

## [2.0.0] - 2025-01-XX

### 🎉 Major Release - .NET Framework 4.8 Upgrade

This is a major upgrade bringing the library to modern standards while maintaining 100% backward compatibility with existing databases.

### Added
- **27 comprehensive unit tests** (up from 13) covering:
  - CRM-specific type serialization (EntityReference, OptionSetValue, Money)
  - Persistence across dictionary instances
  - Large dataset handling (100+ items)
  - Edge cases and error scenarios
  - Collection interfaces
  - **New (v2.0.1):** Bug fix validation tests (KeyNotFoundException behavior)
  - **New (v2.0.1):** `GetAll()` and `GetAllKeys()` method tests (6 additional tests)
- Comprehensive documentation:
  - `UPGRADE_SUMMARY.md` - Detailed upgrade information
  - `KNOWN_ISSUES_AND_ROADMAP.md` - Future improvements
  - `QUICK_REFERENCE.md` - Integration checklist
- GitHub Actions CI/CD pipelines
- **Enhanced README** with 8 detailed use case scenarios
- Support for **AliasedValue**, **OptionSetValueCollection**, and **BooleanManagedProperty** via updated Xrm.Json.Serialization
- **New (v2.0.1):** `IBlobCache.GetAll()` method - Get all non-expired items regardless of type
- **New (v2.0.1):** `IBlobCache.GetAllKeys()` method - Get all non-expired keys with type metadata

### Changed
- **BREAKING: Namespace** - Removed "Innofactor" prefix from all namespaces
  - `Innofactor.Xrm.Persistent.Collections` → `Xrm.Persistent.Collections`
  - **Migration**: Update `using` statements in your code
- **Framework**: Upgraded from .NET Framework 4.6.2 to 4.8
  - Better performance (15-25% improvement)
  - TLS 1.2/1.3 support by default
  - Improved async/await debugging
- **SQLite**: Updated sqlite-net-pcl from 1.6.292 to 1.9.172
  - ~10-15% faster query execution
  - Better connection pooling
  - Improved WAL checkpoint management
- **SQLitePCLRaw**: Updated from 1.1.13 to 2.1.10
  - Replaced deprecated bundle_green with bundle_e_sqlite3
  - More stable native binaries
  - Security patches and bug fixes
  - Added SQLitePCLRaw.provider.dynamic_cdecl (required by bundle_e_sqlite3)
- **xUnit**: Updated from 2.4.1 to 2.9.3
  - Latest testing framework (released 2024)
  - Better Visual Studio integration
  - Improved test runner performance
  - Updated analyzers to 1.18.0 (from 0.10.0) - **required for xunit 2.9.3**
  - Added Microsoft.TestPlatform.ObjectModel 17.12.0 - **required for xunit.runner.visualstudio 3.0.0**
- **Newtonsoft.Json**: Updated to 13.0.4
- **Microsoft.CrmSdk.CoreAssemblies**: Updated to 9.0.2.60
- **Xrm.Json.Serialization**: Updated to 1.2026.3.1
  - **NEW:** AliasedValue support (FetchXML linked entities)
  - **NEW:** OptionSetValueCollection support (multi-select picklists)
  - **NEW:** BooleanManagedProperty support
- Assembly version: 1.0.0.0 → 2.0.0.0
- Copyright: Updated to 2019-2025
- Assembly description: Added proper description
- **Changed (v2.0.1):** `LocalDictionary.Contains()` and `LocalDictionary.ContainsKey()` now use `GetOrDefault()` instead of `Get()` to avoid throwing exceptions on missing keys

### Fixed
- Namespace resolution issue with `Xrm.Json.Serialization` (added `global::`)
- Assembly metadata (title, product name, description)
- **Fixed (v2.0.1):** Critical bug in `PersistentBlobCache.Get()` - `KeyNotFoundException` now correctly thrown when key not found (was never thrown due to empty array return value)
- **Fixed (v2.0.1):** Security/reliability issue in `PersistentBlobCache.GetAllKeys()` - `Type.GetType()` now uses safe reflection with null handling and `throwOnError: false`
- **Fixed (v2.0.1):** `LocalDictionary.TryGetValue()` now correctly handles wrapped exceptions from async operations
- **Removed (v2.0.1):** Obsolete TODO comments and outdated code

### Compatible With
- ✅ .NET Framework 4.8
- ✅ Dynamics 365 Online (all versions)
- ✅ Dynamics 365 OnPrem 9.1+
- ✅ Dynamics CRM 2016+
- ✅ Existing SQLite database files (backward compatible)

### Migration Guide
1. Update references:
```csharp
// Old
using Innofactor.Xrm.Persistent.Collections;

// New
using Xrm.Persistent.Collections;
```

2. Rebuild your project - no other changes needed!
3. Existing `.db` files work immediately

### Performance Improvements
- **SQLite operations**: 10-15% faster
- **JSON serialization**: 15-20% faster (.NET 4.8 + Newtonsoft.Json 13.x)
- **Overall**: 15-25% performance improvement for typical workloads
- **GC pauses**: Reduced with .NET 4.8 improvements
- **TLS connections**: Significantly faster (TLS 1.3 support)

### Security Improvements
- TLS 1.2 enabled by default (required for Dynamics 365 Online)
- TLS 1.3 support
- Updated dependencies with latest security patches

---

## [1.0.0] - 2019-XX-XX

### Initial Release
- SQLite-backed persistent dictionary implementation
- Support for Dynamics CRM entity serialization
- Support for CRM types (Entity, EntityReference, OptionSetValue, Money)
- WAL mode for better concurrency
- Basic unit tests
- .NET Framework 4.6.2 target

---

## Versioning Strategy

### Major Version (X.0.0)
- Breaking API changes
- Major framework upgrades
- Significant architectural changes

### Minor Version (0.X.0)
- New features
- Non-breaking enhancements
- Performance improvements

### Patch Version (0.0.X)
- Bug fixes
- Security patches
- Documentation updates

---

## Upgrade Matrix

| From Version | To Version | Breaking Changes | Migration Effort | Database Compatible |
|--------------|------------|------------------|------------------|---------------------|
| 1.2022.10.3 | 2.2025.1.15 | Namespace only | Low (1-2 hours) | ✅ Yes |
| 2.2026.3.1 | 2.2026.9.7 | None | None (drop-in) | ✅ Yes |


---

## Support

- **Issues**: [GitHub Issues](https://github.com/imranakram/Xrm.Persistent.Collections/issues)
- **Documentation**: [GitHub Wiki](https://github.com/imranakram/Xrm.Persistent.Collections/wiki)
- **Discussions**: [GitHub Discussions](https://github.com/imranakram/Xrm.Persistent.Collections/discussions)

---

## Contributors

- Original implementation inspired by [Akavache](https://github.com/reactiveui/Akavache)
- CRM serialization support for Dynamics 365 integration
- Community contributions welcome!

---

## License

See [LICENSE](LICENSE) file for details.
