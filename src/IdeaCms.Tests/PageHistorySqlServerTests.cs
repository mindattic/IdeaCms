using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests;

/// <summary>
/// The LIVE temporal proof for M.IDEA-US-B5: <see cref="PageHistoryService.GetHistoryAsync"/> against a
/// real SQL Server temporal table. [Explicit] because it needs the dev LocalDB with the
/// IdeaCms database (run: dotnet test --filter FullyQualifiedName~PageHistorySqlServerTests).
/// The frontpage row has a rich edit history, so the assertion demands MULTIPLE temporal versions —
/// the thing InMemory can never prove.
/// </summary>
[TestFixture]
public class PageHistorySqlServerTests
{
    private const string Conn =
        @"Server=(localdb)\MSSQLLocalDB;Database=IdeaCms;Trusted_Connection=True;TrustServerCertificate=True";

    [Test]
    [Explicit("Requires SQL Server LocalDB with the IdeaCms database (the dev DB).")]
    public async Task GetHistoryAsync_OnSqlServer_ReturnsOrderedTemporalVersions()
    {
        var factory = new SqlFactory(Conn);
        await using var db = factory.CreateDbContext();
        var front = await db.Pages.SingleAsync(p => p.Slug == "frontpage");

        var history = await new PageHistoryService(factory).GetHistoryAsync(front.Id);

        Assert.Multiple(() =>
        {
            Assert.That(history, Has.Count.GreaterThan(1),
                "the frontpage has been edited repeatedly — temporal history must hold multiple versions");
            Assert.That(history[0].ValidFrom, Is.GreaterThanOrEqualTo(history[^1].ValidFrom),
                "most recent first");
            Assert.That(history.Select(h => h.PageId), Is.All.EqualTo(front.Id));
            Assert.That(history[0].Slug, Is.EqualTo("frontpage"));
        });
        TestContext.Out.WriteLine($"temporal versions: {history.Count}; newest ValidFrom={history[0].ValidFrom:O}");
    }

    private sealed class SqlFactory(string conn) : IDbContextFactory<CmsDbContext>
    {
        public CmsDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<CmsDbContext>().UseSqlServer(conn).Options);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }
}
