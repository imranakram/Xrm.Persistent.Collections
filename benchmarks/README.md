# Benchmarks

Standalone measurement tools. **Deliberately not part of `Xrm.Persistent.Collections.sln`**, so CI is unaffected — build and run them directly.

---

## SyncModeBenchmark

Answers one question: **how much of a single-item write is the disk flush, on this particular machine's storage?**

`PersistentBlobCache` sets `journal_mode = WAL` but never sets `synchronous`, so it inherits the SQLite default of `FULL` — an `fsync` on every commit. Its single-item `Insert` is a bare `InsertOrReplace`, which SQLite auto-commits, so every `LocalDictionary` assignment pays one flush.

Whether that matters is entirely a property of the storage device, which is why this is a tool to run rather than a number quoted in a document. **Run it on the machine whose numbers you care about** — a developer laptop's NVMe and a VM on network-attached storage give very different answers.

### Running it

```bash
dotnet run -c Release --project benchmarks/SyncModeBenchmark
```

To include the control run against the real `PersistentBlobCache`, build the solution as `Release|x64` first; the project picks the DLL up automatically and skips that section if it is absent.

```
--singles N   single-item writes per round   (default 400)
--bulk N      items in the bulk write        (default 5000)
--payload N   payload size in bytes          (default 512)
--rounds N    rounds, median reported        (default 5)
--path DIR    where to put the databases     (default %TEMP%\xpc-syncbench)
```

Point `--path` at the volume you actually care about:

```bash
dotnet run -c Release --project benchmarks/SyncModeBenchmark -- --path D:\jobdata --rounds 9
```

### What it measures

For each of `OFF`, `NORMAL` and `FULL`, against a fresh WAL database with the library's exact schema:

- **single-item writes** — one auto-committed `InsertOrReplace` per item, mirroring `PersistentBlobCache.Insert(string, byte[], …)` and therefore every `LocalDictionary` assignment
- **bulk writes** — the whole batch in one transaction, chunked at 950 with delete-then-insert per chunk, mirroring `PersistentBlobCache.Insert(IDictionary, …)`

It also probes raw `fsync` latency directly, so the database numbers can be read against the cost of the underlying operation.

Design notes:

- Modes are **interleaved within each round** rather than run in blocks, so background load on the machine hits each mode roughly equally.
- The **median** across rounds is reported. Single rounds are noisy — expect outliers of 2× or more on a machine doing anything else.
- Each measurement gets a **fresh database file**, deleted afterwards.
- The `synchronous` and `journal_mode` pragmas are **read back and verified** after being set; a pragma that silently failed to apply would invalidate the run.
- The control run measures the **real library** and reports its per-call overhead separately, because a fixed per-call cost does not shrink when the flush goes away.

### Reading the output

Example from one developer machine — **these are not your numbers**, they are an illustration of the shape:

```
Raw storage: mean fsync 589.3 us - the cost FULL pays on every commit.

mode         single/sec      per write    vs FULL   bulk items/sec
------------------------------------------------------------------------------
OFF              18,952        52.8 us      13.7x           85,797
NORMAL           13,470        74.2 us       9.8x          104,563
FULL              1,380       724.7 us       1.0x           76,119
```

The internal consistency is the thing to check: `FULL` at 724.7 µs is roughly the 589 µs `fsync` plus `NORMAL`'s 74 µs of actual work. If those don't add up on your run, the machine was too busy — re-run with more `--rounds`.

Three conclusions fall out:

1. **`synchronous = NORMAL` is worth ~10× on single-item writes** on this storage. On an NVMe SSD with a fast flush it will be less; on network-attached storage, considerably more.
2. **Batching dwarfs it, and costs nothing in durability.** Even under `FULL`, the bulk path moved ~55× more items per second than repeated single writes, because the batch commits once instead of once per item. If a caller writes several entries in a row, switching to `Insert(IDictionary<string, byte[]>)` is the cheapest available win and requires no configuration change or durability trade-off.
3. **The library's own per-call overhead is real but currently secondary** — about 63 µs per write on this machine, roughly 8% of a `FULL` write. That's `Insert` wrapping a `Task.Run` around constructing one `CacheItem`, plus `LocalDictionary` blocking on the result: a thread-pool hop and a block per assignment. It is invisible next to a 589 µs flush, but once `synchronous = NORMAL` removes the flush it becomes a much larger share, which is why the tool projects the realistic library-path speedup (~5.8× here) alongside the raw one (~9.8×).

Bulk throughput is roughly mode-independent, which is exactly what you'd expect: one commit for the whole batch means the flush is amortised to nothing. Variation between bulk rows is machine noise, not signal.

### The durability trade-off

`NORMAL` in WAL mode **cannot corrupt the database**, and loses nothing when a process crashes — an unhandled exception, a killed service, a container restart — because the data is already in the OS page cache and the OS outlives the process.

What it can lose is commits since the last checkpoint, in a **power cut or OS-level crash**.

For a cache, that is normally fine: you re-derive the entry. If a write is itself the source of truth — an "already dispatched" marker whose loss would cause a duplicate — keep `FULL` for that data. That is a judgement about the data, not about SQLite.

### Applying the change

If the numbers justify it, it is one line in `PersistentBlobCache.CreateConnection()`, immediately after the journal mode is set:

```csharp
connection.ExecuteScalar<string>("PRAGMA journal_mode = WAL");
connection.Execute("PRAGMA synchronous = NORMAL");
```

This repository has not made that change. Measure first, then decide.
