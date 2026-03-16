using SQLite;
using Xunit;

namespace Xrm.Persistent.Collections.Tests
{
    public class SQLiteVersionTests
    {
        [Fact]
        public void SQLite_Engine_Version_Is_Available()
        {
            using (var db = new SQLiteConnection(":memory:"))
            {
                var version = db.ExecuteScalar<string>("select sqlite_version();");
                System.Diagnostics.Trace.WriteLine($"SQLite engine version: {version}");
                Assert.False(string.IsNullOrWhiteSpace(version));
            }
        }
    }
}
