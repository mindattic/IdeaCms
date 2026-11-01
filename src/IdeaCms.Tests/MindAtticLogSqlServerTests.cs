using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MindAttic.Log.Extensions;
using MindAttic.Log.Migration;
using MindAttic.Log.Schema;
using MindAttic.Vault.Paths;

namespace IdeaCms.Tests;

/// <summary>
/// The LIVE proof that AddIdeaCms's MindAttic.Log wiring (DependencyInjection/ServiceCollectionExtensions.cs)
/// writes through a real standalone database, and that Program.cs's Phase-2 startup step
/// (MindAttic.Log 3.0.0, see its docs/BIBLE.md §1.1) actually carries Phase 1's historical
/// dbo.MindAttic_Log rows out of a real SQL Server table and drops it — not just that both compile
/// against their respective APIs. [Explicit] for the same reason PageHistorySqlServerTests is: needs
/// a live SQL Server/LocalDB instance, which isn't assumed to exist in every environment this suite
/// runs in. This provisions and drops its own throwaway database rather than depending on the
/// pre-seeded dev DB — the MindAttic_Log table has no dependency on CMS content.
/// </summary>
[TestFixture]
public class MindAtticLogSqlServerTests
{
    private const string Master = @"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";
    private string databaseName = null!;
    private string connectionString = null!;
    private string standaloneDbPath = null!;

    [SetUp]
    public async Task SetUp()
    {
        databaseName = "MindAtticLogTest_" + Guid.NewGuid().ToString("N");
        connectionString = $@"Server=(localdb)\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True";
        standaloneDbPath = Path.Combine(Path.GetTempPath(), $"ideas-standalone-log-{Guid.NewGuid()}.db");

        await using var master = new SqlConnection(Master);
        await master.OpenAsync();
        await using var create = master.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{databaseName}];";
        await create.ExecuteNonQueryAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(standaloneDbPath)) File.Delete(standaloneDbPath);
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(standaloneDbPath + suffix)) File.Delete(standaloneDbPath + suffix);

        await using var master = new SqlConnection(Master);
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];";
        await drop.ExecuteNonQueryAsync();
    }

    [Test]
    [Explicit("Requires SQL Server LocalDB — see PageHistorySqlServerTests for the same convention.")]
    public async Task AddMindAtticLog_Writes_Into_The_Standalone_Database()
    {
        var services = new ServiceCollection();
        services.AddMindAtticLog(o =>
        {
            o.Application = "IdeaCms";
            o.DbPath = standaloneDbPath;
        });

        using (var provider = services.BuildServiceProvider())
        {
            var logger = provider.GetRequiredService<ILogger<MindAtticLogSqlServerTests>>();
            logger.LogWarning("IdeaCms standalone-database integration test at {Utc}", DateTime.UtcNow);
        } // Dispose flushes the sink (AddSerilog(..., dispose: true))

        SqliteConnection.ClearAllPools();
        using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = standaloneDbPath }.ToString());
        verify.Open();
        using var select = verify.CreateCommand();
        select.CommandText = $"SELECT Application, Message, Category FROM {LogSchema.TableName};";
        using var reader = select.ExecuteReader();

        Assert.That(reader.Read(), Is.True, "Expected the logged warning to have reached the standalone database.");
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("IdeaCms"));
            Assert.That(reader.GetString(1), Does.Contain("IdeaCms standalone-database integration test"));
            Assert.That(reader.GetString(2), Does.Contain(nameof(MindAtticLogSqlServerTests)));
        });
    }

    [Test]
    [Explicit("Requires SQL Server LocalDB — see PageHistorySqlServerTests for the same convention.")]
    public async Task Program_Phase2_Step_Migrates_Historical_Rows_Out_Of_SqlServer_And_Drops_The_Table()
    {
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = LogSchema.CreateTableSqlServer;
            await create.ExecuteNonQueryAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText = $"""
                INSERT INTO dbo.{LogSchema.TableName} (TimestampUtc, Level, Application, Message)
                VALUES (@ts, 2, 'IdeaCms', 'Phase 1 row');
                """;
            insert.Parameters.AddWithValue("@ts", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            await insert.ExecuteNonQueryAsync();
        }

        // Same call Program.cs's InitializeAsync makes.
        var copied = await MindAtticLogMigrator.MigrateFromSqlServerAsync(connectionString, standaloneDbPath, dropSource: true);
        Assert.That(copied, Is.EqualTo(1));

        await using var verifySource = new SqlConnection(connectionString);
        await verifySource.OpenAsync();
        await using var checkTable = verifySource.CreateCommand();
        checkTable.CommandText = $"SELECT COUNT(*) FROM sys.tables WHERE name = '{LogSchema.TableName}';";
        Assert.That(Convert.ToInt32(await checkTable.ExecuteScalarAsync()), Is.EqualTo(0), "dropSource: true should have dropped the Phase 1 table.");

        SqliteConnection.ClearAllPools();
        using var verifyDest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = standaloneDbPath }.ToString());
        verifyDest.Open();
        using var select = verifyDest.CreateCommand();
        select.CommandText = $"SELECT Message FROM {LogSchema.TableName};";
        Assert.That(select.ExecuteScalar(), Is.EqualTo("Phase 1 row"));
    }
}
