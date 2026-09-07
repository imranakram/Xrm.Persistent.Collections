// Measures what PRAGMA synchronous actually costs on THIS machine's storage.
//
// PersistentBlobCache sets journal_mode = WAL but never sets synchronous, so it inherits
// the SQLite default of FULL: an fsync on every commit. Its single-item Insert path is a
// bare InsertOrReplace, which SQLite auto-commits, so LocalDictionary pays one disk flush
// per assignment. This benchmark measures how much of that write time is the flush.
//
// The magnitude is entirely a property of the storage device, which is why this exists as
// a tool to run rather than a number quoted in a document. Run it on the machine whose
// numbers you care about.

namespace SyncModeBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using SQLite;

    internal static class Program
    {
        /// <summary>Matches the chunk size in the library's InternalExtensions.</summary>
        private const int ChunkSize = 950;

        /// <summary>Schema copied verbatim from PersistentBlobCache.EnsureSchema.</summary>
        private const string TableSql = @"
                CREATE TABLE IF NOT EXISTS CacheItem
                (
                    Key TEXT NOT NULL,
                    Type TEXT NOT NULL,
                    CreatedAt INTEGER NOT NULL,
                    Time INTEGER,
                    Data BLOB,
                    PRIMARY KEY (Key, Type)
                ) WITHOUT ROWID;";

        private static readonly string[] Modes = { "OFF", "NORMAL", "FULL" };

        private static int Main(string[] args)
        {
            Options options;
            if (!Options.TryParse(args, out options))
            {
                return 1;
            }

            Directory.CreateDirectory(options.WorkingDirectory);

            Console.WriteLine("SQLite `PRAGMA synchronous` benchmark");
            Console.WriteLine(new string('=', 78));
            Console.WriteLine("  machine        : {0} ({1} logical cores)", Environment.MachineName, Environment.ProcessorCount);
            Console.WriteLine("  working dir    : {0}", options.WorkingDirectory);
            Console.WriteLine("  volume         : {0}", DescribeVolume(options.WorkingDirectory));
            Console.WriteLine("  single writes  : {0:N0} per round", options.SingleWrites);
            Console.WriteLine("  bulk items     : {0:N0} per round", options.BulkItems);
            Console.WriteLine("  payload        : {0:N0} bytes", options.PayloadBytes);
            Console.WriteLine("  rounds         : {0} (median reported)", options.Rounds);
            Console.WriteLine();

            try
            {
                var fsync = MeasureFsyncLatency(options.WorkingDirectory);
                Console.WriteLine("Raw storage: mean fsync {0} - the cost FULL pays on every commit.", FormatDuration(fsync));
                Console.WriteLine();

                var results = RunAllModes(options);
                Report(results, options);
                RunLibraryControl(options, results);

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Benchmark failed: " + ex);
                return 1;
            }
        }

        #region Measurement

        private static List<ModeResult> RunAllModes(Options options)
        {
            var singles = Modes.ToDictionary(m => m, m => new List<double>());
            var bulks = Modes.ToDictionary(m => m, m => new List<double>());

            // Warm up once, so JIT, the page cache and the native provider are not charged
            // to whichever mode happens to run first.
            MeasureSingleWrites("FULL", Math.Min(50, options.SingleWrites), options);
            MeasureBulkWrites("FULL", Math.Min(500, options.BulkItems), options);

            for (var round = 1; round <= options.Rounds; round++)
            {
                // Modes are interleaved within each round rather than run in blocks, so
                // background drift on the machine hits every mode roughly equally.
                foreach (var mode in Modes)
                {
                    Console.Write("  round {0}/{1}  {2,-6} ... ", round, options.Rounds, mode);

                    var single = MeasureSingleWrites(mode, options.SingleWrites, options);
                    var bulk = MeasureBulkWrites(mode, options.BulkItems, options);

                    singles[mode].Add(single);
                    bulks[mode].Add(bulk);

                    Console.WriteLine("single {0,10:N0} ops/s   bulk {1,10:N0} items/s",
                        options.SingleWrites / single, options.BulkItems / bulk);
                }
            }

            Console.WriteLine();

            return Modes
                .Select(m => new ModeResult
                {
                    Mode = m,
                    SingleOpsPerSecond = options.SingleWrites / Median(singles[m]),
                    SingleSecondsPerOp = Median(singles[m]) / options.SingleWrites,
                    BulkItemsPerSecond = options.BulkItems / Median(bulks[m])
                })
                .ToList();
        }

        /// <summary>
        /// One auto-committed InsertOrReplace per item, mirroring
        /// PersistentBlobCache.Insert(string, byte[], ...) and therefore every
        /// LocalDictionary assignment.
        /// </summary>
        private static double MeasureSingleWrites(string mode, int count, Options options)
        {
            using (var db = OpenFreshDatabase(mode, options))
            {
                var items = BuildItems(count, MakePayload(options.PayloadBytes));

                var sw = Stopwatch.StartNew();
                foreach (var item in items)
                {
                    db.Connection.InsertOrReplace(item);
                }
                sw.Stop();

                return sw.Elapsed.TotalSeconds;
            }
        }

        /// <summary>
        /// The whole batch inside one transaction, chunked, delete-then-insert per chunk,
        /// mirroring PersistentBlobCache.Insert(IDictionary, ...).
        /// </summary>
        private static double MeasureBulkWrites(string mode, int count, Options options)
        {
            using (var db = OpenFreshDatabase(mode, options))
            {
                var items = BuildItems(count, MakePayload(options.PayloadBytes));
                var connection = db.Connection;

                var sw = Stopwatch.StartNew();
                connection.RunInTransaction(() =>
                {
                    for (var offset = 0; offset < items.Count; offset += ChunkSize)
                    {
                        var chunk = items.GetRange(offset, Math.Min(ChunkSize, items.Count - offset));

                        var parameters = string.Join(", ", Enumerable.Repeat("?", chunk.Count));
                        var sql = "DELETE FROM CacheItem WHERE Type = ? AND Key IN (" + parameters + ")";

                        var args = new object[1 + chunk.Count];
                        args[0] = string.Empty;
                        for (var i = 0; i < chunk.Count; i++)
                        {
                            args[i + 1] = chunk[i].Key;
                        }

                        connection.Execute(sql, args);
                        connection.InsertAll(chunk, runInTransaction: false);
                    }
                });
                sw.Stop();

                return sw.Elapsed.TotalSeconds;
            }
        }

        /// <summary>
        /// Measures a bare fsync, so the database numbers can be read against the cost of
        /// the underlying operation rather than in isolation.
        /// </summary>
        private static double MeasureFsyncLatency(string workingDirectory)
        {
            var path = Path.Combine(workingDirectory, "fsync-probe-" + Guid.NewGuid().ToString("N") + ".tmp");
            var block = MakePayload(4096);
            const int iterations = 40;

            try
            {
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    // First write allocates the file; keep it out of the measurement.
                    stream.Write(block, 0, block.Length);
                    stream.Flush(true);

                    var sw = Stopwatch.StartNew();
                    for (var i = 0; i < iterations; i++)
                    {
                        stream.Write(block, 0, block.Length);
                        stream.Flush(true);
                    }
                    sw.Stop();

                    return sw.Elapsed.TotalSeconds / iterations;
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        #endregion Measurement

        #region Control run

        /// <summary>
        /// Runs the real PersistentBlobCache, which is always FULL, so the replicated FULL
        /// path can be checked against it. If the two disagree materially then the
        /// replication is not faithful and the mode comparison should not be trusted.
        /// </summary>
        private static void RunLibraryControl(Options options, List<ModeResult> results)
        {
#if HAVE_LIBRARY
            var payload = MakePayload(options.PayloadBytes);
            var timings = new List<double>();

            try
            {
                for (var round = 0; round < options.Rounds; round++)
                {
                    var path = Path.Combine(options.WorkingDirectory, "control-" + Guid.NewGuid().ToString("N") + ".db");

                    try
                    {
                        using (var cache = new Xrm.Persistent.Collections.Backend.PersistentBlobCache(path))
                        {
                            cache.CreateConnection().Wait();
                            cache.Insert("warmup", payload).Wait();

                            var sw = Stopwatch.StartNew();
                            for (var i = 0; i < options.SingleWrites; i++)
                            {
                                cache.Insert("control-" + i.ToString(CultureInfo.InvariantCulture), payload).Wait();
                            }
                            sw.Stop();

                            timings.Add(sw.Elapsed.TotalSeconds);
                        }
                    }
                    finally
                    {
                        DeleteDatabase(path);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Control run failed; the mode table above is unaffected: " + ex.Message);
                return;
            }

            var full = results.Single(r => r.Mode == "FULL");
            var normal = results.Single(r => r.Mode == "NORMAL");

            var librarySecondsPerOp = Median(timings) / options.SingleWrites;
            var overheadSecondsPerOp = librarySecondsPerOp - full.SingleSecondsPerOp;

            Console.WriteLine();
            Console.WriteLine("Control: the real PersistentBlobCache, which is always FULL");
            Console.WriteLine(new string('-', 78));
            Console.WriteLine("  real library         {0,12:N0} writes/sec   {1,10} per write",
                1.0 / librarySecondsPerOp, FormatDuration(librarySecondsPerOp));
            Console.WriteLine("  raw SQLite at FULL   {0,12:N0} writes/sec   {1,10} per write",
                full.SingleOpsPerSecond, FormatDuration(full.SingleSecondsPerOp));

            if (overheadSecondsPerOp <= 0)
            {
                Console.WriteLine();
                Console.WriteLine("  The library measured no slower than raw SQLite, which means this run was");
                Console.WriteLine("  too noisy to separate them. Re-run with more --rounds on an idle machine.");
                return;
            }

            // A fixed per-call cost does not shrink when the fsync goes away, so the
            // speedup the library can actually realise is smaller than the raw table's.
            var projectedNormalSecondsPerOp = overheadSecondsPerOp + normal.SingleSecondsPerOp;
            var projectedSpeedup = librarySecondsPerOp / projectedNormalSecondsPerOp;
            var rawSpeedup = full.SingleSecondsPerOp / normal.SingleSecondsPerOp;

            Console.WriteLine("  library overhead     {0,12}                {1,9:N0}% of a write",
                FormatDuration(overheadSecondsPerOp), overheadSecondsPerOp / librarySecondsPerOp * 100.0);
            Console.WriteLine();
            Console.WriteLine("  This gap is the library's own per-insert cost, not storage: Insert wraps a");
            Console.WriteLine("  Task.Run around constructing one CacheItem, then LocalDictionary blocks on");
            Console.WriteLine("  the result. That is a thread-pool hop and a block per assignment.");
            Console.WriteLine();
            Console.WriteLine("  What NORMAL is actually worth here");
            Console.WriteLine("  {0}", new string('-', 74));
            Console.WriteLine("    raw SQLite         {0,6:N1}x   ({1} -> {2} per write)",
                rawSpeedup, FormatDuration(full.SingleSecondsPerOp), FormatDuration(normal.SingleSecondsPerOp));
            Console.WriteLine("    via this library   {0,6:N1}x   ({1} -> {2} per write, projected)",
                projectedSpeedup, FormatDuration(librarySecondsPerOp), FormatDuration(projectedNormalSecondsPerOp));
            Console.WriteLine();

            if (overheadSecondsPerOp > full.SingleSecondsPerOp - normal.SingleSecondsPerOp)
            {
                Console.WriteLine("    The per-call overhead now exceeds the fsync it would save, so removing");
                Console.WriteLine("    that Task.Run is the larger win on this path - do it before, or as well");
                Console.WriteLine("    as, changing synchronous.");
            }
            else
            {
                Console.WriteLine("    The fsync still dominates the per-call overhead, so synchronous = NORMAL");
                Console.WriteLine("    remains the bigger lever on this path.");
            }
#else
            Console.WriteLine();
            Console.WriteLine("Control run skipped: Xrm.Persistent.Collections.dll was not found.");
            Console.WriteLine("Build the solution as Release|x64, then rebuild this project to include it.");
#endif
        }

        #endregion Control run

        #region Reporting

        private static void Report(List<ModeResult> results, Options options)
        {
            var full = results.Single(r => r.Mode == "FULL");
            var normal = results.Single(r => r.Mode == "NORMAL");

            Console.WriteLine("Results (median of {0} rounds)", options.Rounds);
            Console.WriteLine(new string('-', 78));
            Console.WriteLine("{0,-8} {1,14} {2,14} {3,10} {4,16}", "mode", "single/sec", "per write", "vs FULL", "bulk items/sec");
            Console.WriteLine(new string('-', 78));

            foreach (var r in results)
            {
                Console.WriteLine("{0,-8} {1,14:N0} {2,14} {3,9:N1}x {4,16:N0}",
                    r.Mode,
                    r.SingleOpsPerSecond,
                    FormatDuration(r.SingleSecondsPerOp),
                    r.SingleOpsPerSecond / full.SingleOpsPerSecond,
                    r.BulkItemsPerSecond);
            }

            Console.WriteLine(new string('-', 78));
            Console.WriteLine();

            var speedup = normal.SingleOpsPerSecond / full.SingleOpsPerSecond;
            var bulkVsSingle = full.BulkItemsPerSecond / full.SingleOpsPerSecond;

            Console.WriteLine("Reading the numbers");
            Console.WriteLine(new string('-', 78));
            Console.WriteLine("  synchronous = NORMAL is {0:N1}x faster than FULL on single-item writes,", speedup);
            Console.WriteLine("  which is the path LocalDictionary uses for every assignment.");
            Console.WriteLine();
            Console.WriteLine("  Batching is the larger lever and costs nothing in durability: even under");
            Console.WriteLine("  FULL, the bulk path moves {0:N0}x more items per second than repeated", bulkVsSingle);
            Console.WriteLine("  single writes, because the batch commits once instead of once per item.");
            Console.WriteLine();
            Console.WriteLine("  NORMAL in WAL cannot corrupt the database and loses nothing when a process");
            Console.WriteLine("  crashes. A power cut or OS crash can lose commits since the last checkpoint.");

            if (speedup < 1.25)
            {
                Console.WriteLine();
                Console.WriteLine("  On this storage the gain is small, so writes here are not fsync-bound and");
                Console.WriteLine("  changing synchronous would buy little. Prefer batching.");
            }
        }

        #endregion Reporting

        #region Helpers

        private static BenchDatabase OpenFreshDatabase(string mode, Options options)
        {
            var path = Path.Combine(options.WorkingDirectory, "bench-" + Guid.NewGuid().ToString("N") + ".db");
            var connection = new SQLiteConnection(path);

            try
            {
                var journal = connection.ExecuteScalar<string>("PRAGMA journal_mode = WAL");
                connection.Execute("PRAGMA synchronous = " + mode);
                connection.Execute(TableSql);

                // A pragma that silently failed to apply would invalidate the whole run.
                var applied = connection.ExecuteScalar<int>("PRAGMA synchronous");
                var expected = mode == "OFF" ? 0 : mode == "NORMAL" ? 1 : 2;
                if (applied != expected)
                {
                    throw new InvalidOperationException(string.Format(
                        "PRAGMA synchronous = {0} did not apply; the connection reports {1}.", mode, applied));
                }

                if (!string.Equals(journal, "wal", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("journal_mode is '" + journal + "', expected 'wal'.");
                }

                return new BenchDatabase(connection, path);
            }
            catch
            {
                connection.Dispose();
                DeleteDatabase(path);
                throw;
            }
        }

        private static List<CacheItem> BuildItems(int count, byte[] payload)
        {
            var createdAt = DateTime.UtcNow.Ticks;
            var items = new List<CacheItem>(count);

            for (var i = 0; i < count; i++)
            {
                items.Add(new CacheItem
                {
                    Key = "bench-key-" + i.ToString(CultureInfo.InvariantCulture),
                    Type = string.Empty,
                    CreatedAt = createdAt,
                    Time = null,
                    Data = payload
                });
            }

            return items;
        }

        private static byte[] MakePayload(int bytes)
        {
            var payload = new byte[bytes];
            for (var i = 0; i < bytes; i++)
            {
                payload[i] = (byte)(i % 251);
            }

            return payload;
        }

        private static double Median(List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 1
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        /// <summary>
        /// Takes seconds rather than a TimeSpan on purpose: TimeSpan.FromSeconds rounds to
        /// the nearest millisecond, which flattens every sub-millisecond result to zero.
        /// </summary>
        private static string FormatDuration(double seconds)
        {
            var ms = seconds * 1000.0;
            return ms >= 1
                ? ms.ToString("N2", CultureInfo.InvariantCulture) + " ms"
                : (ms * 1000.0).ToString("N1", CultureInfo.InvariantCulture) + " us";
        }

        private static string DescribeVolume(string path)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                var drive = new DriveInfo(root);
                return string.Format("{0} ({1}, {2:N1} GB free)",
                    root, drive.DriveFormat, drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0);
            }
            catch
            {
                return "unknown";
            }
        }

        private static void DeleteDatabase(string path)
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort; these live under a temp directory.
            }
        }

        #endregion Helpers

        #region Types

        /// <summary>Mirrors the library's internal CacheItem and its table.</summary>
        [Table("CacheItem")]
        public class CacheItem
        {
            public string Key { get; set; }

            public string Type { get; set; }

            public long CreatedAt { get; set; }

            public long? Time { get; set; }

            public byte[] Data { get; set; }
        }

        /// <summary>A connection whose database files are removed when it is disposed.</summary>
        private sealed class BenchDatabase : IDisposable
        {
            private readonly string path;

            public BenchDatabase(SQLiteConnection connection, string path)
            {
                Connection = connection;
                this.path = path;
            }

            public SQLiteConnection Connection { get; private set; }

            public void Dispose()
            {
                Connection.Close();
                Connection.Dispose();
                DeleteDatabase(path);
            }
        }

        private sealed class ModeResult
        {
            public string Mode { get; set; }

            public double SingleOpsPerSecond { get; set; }

            public double SingleSecondsPerOp { get; set; }

            public double BulkItemsPerSecond { get; set; }
        }

        private sealed class Options
        {
            public int SingleWrites { get; private set; }

            public int BulkItems { get; private set; }

            public int PayloadBytes { get; private set; }

            public int Rounds { get; private set; }

            public string WorkingDirectory { get; private set; }

            public static bool TryParse(string[] args, out Options options)
            {
                options = new Options
                {
                    SingleWrites = 400,
                    BulkItems = 5000,
                    PayloadBytes = 512,
                    Rounds = 5,
                    WorkingDirectory = Path.Combine(Path.GetTempPath(), "xpc-syncbench")
                };

                for (var i = 0; i < args.Length; i++)
                {
                    var arg = args[i];

                    if (arg == "--help" || arg == "-h" || arg == "-?")
                    {
                        PrintUsage();
                        return false;
                    }

                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for " + arg);
                        PrintUsage();
                        return false;
                    }

                    var value = args[++i];

                    switch (arg)
                    {
                        case "--singles":
                            options.SingleWrites = int.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "--bulk":
                            options.BulkItems = int.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "--payload":
                            options.PayloadBytes = int.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "--rounds":
                            options.Rounds = int.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "--path":
                            options.WorkingDirectory = value;
                            break;
                        default:
                            Console.Error.WriteLine("Unknown argument " + arg);
                            PrintUsage();
                            return false;
                    }
                }

                return true;
            }

            private static void PrintUsage()
            {
                Console.WriteLine("Usage: SyncModeBenchmark [options]");
                Console.WriteLine();
                Console.WriteLine("  --singles N   single-item writes per round   (default 400)");
                Console.WriteLine("  --bulk N      items in the bulk write        (default 5000)");
                Console.WriteLine("  --payload N   payload size in bytes          (default 512)");
                Console.WriteLine("  --rounds N    rounds, median reported        (default 5)");
                Console.WriteLine("  --path DIR    where to put the databases     (default %TEMP%\\xpc-syncbench)");
                Console.WriteLine();
                Console.WriteLine("Point --path at the volume you actually care about; the result is a");
                Console.WriteLine("property of the storage, not of the code.");
            }
        }

        #endregion Types
    }
}
