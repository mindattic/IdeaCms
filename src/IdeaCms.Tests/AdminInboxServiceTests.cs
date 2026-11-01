using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests;

/// <summary>
/// AdminInboxService upsert-by-DedupKey behavior over an in-memory CmsDbContext: repeats collapse to one
/// row, Resolve flips status, a recurrence after Resolve reopens, and UnreadCount counts only New.
/// (The unique DedupKey DB index is the runtime backstop; InMemory ignores it, so collapse is verified as
/// the service's own lookup-before-insert behavior.)
/// </summary>
[TestFixture]
public class AdminInboxServiceTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
    }

    private static AdminInboxService NewService() =>
        new(new InMemoryFactory("inbox_" + Guid.NewGuid().ToString("N")));

    [Test]
    public async Task RaiseAsync_InsertsNewRow()
    {
        var svc = NewService();
        await svc.RaiseAsync("Warning", "Render", "Disabled theme", "cyberspace disabled", "render:disabled:theme:cyberspace");

        var all = await svc.ListAsync();
        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[0].Status, Is.EqualTo("New"));
        Assert.That(all[0].Severity, Is.EqualTo("Warning"));
    }

    [Test]
    public async Task RaiseAsync_SameDedupKey_CollapsesToOneRow()
    {
        var svc = NewService();
        await svc.RaiseAsync("Error", "Render", "Missing", "x", "render:missing:component:tooltip");
        await svc.RaiseAsync("Error", "Render", "Missing", "x again", "render:missing:component:tooltip");
        await svc.RaiseAsync("Error", "Render", "Missing", "x third", "render:missing:component:tooltip");

        Assert.That(await svc.ListAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ResolveAsync_SetsStatusAndTimestamp()
    {
        var svc = NewService();
        await svc.RaiseAsync("Info", "System", "s", "b", "k1");
        var id = (await svc.ListAsync())[0].Id;

        Assert.That(await svc.ResolveAsync(id), Is.True);
        var row = (await svc.ListAsync())[0];
        Assert.That(row.Status, Is.EqualTo("Resolved"));
        Assert.That(row.ResolvedUtc, Is.Not.Null);
    }

    [Test]
    public async Task RaiseAsync_AfterResolve_ReopensToNew()
    {
        var svc = NewService();
        await svc.RaiseAsync("Warning", "Render", "s", "b", "k2");
        var id = (await svc.ListAsync())[0].Id;
        await svc.ResolveAsync(id);

        await svc.RaiseAsync("Warning", "Render", "s", "recurred", "k2");

        var rows = await svc.ListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));         // still one row (reopened, not duplicated)
        Assert.That(rows[0].Status, Is.EqualTo("New"));
        Assert.That(rows[0].ResolvedUtc, Is.Null);
    }

    [Test]
    public async Task UnreadCount_CountsOnlyNew()
    {
        var svc = NewService();
        await svc.RaiseAsync("Info", "System", "a", "b", "ka");
        await svc.RaiseAsync("Info", "System", "c", "d", "kb");
        await svc.ResolveAsync((await svc.ListAsync("New"))[0].Id);

        Assert.That(await svc.UnreadCountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task RaiseAsync_SameDedupKey_StillNew_RefreshesBody()
    {
        // Regression: when collapsing a repeat of a still-"New" message, Body was never updated, so
        // the first-ever page slug was permanently shown even if the problem moved to a different page.
        var svc = NewService();
        await svc.RaiseAsync("Warning", "Render", "Missing plugin", "first body", "render:missing:plugin:nav");
        await svc.RaiseAsync("Warning", "Render", "Missing plugin", "updated body", "render:missing:plugin:nav");

        var all = await svc.ListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(1), "still one row");
            Assert.That(all[0].Body, Is.EqualTo("updated body"), "body must be refreshed on still-New repeats");
        });
    }

    [Test]
    public async Task MarkRead_AlreadyResolved_ReturnsTrueIdempotent()
    {
        // Regression: MarkReadAsync returned false for already-Resolved messages, making it impossible
        // for callers to distinguish "not found" (a real error) from "already done" (a no-op success).
        // Idempotent success is the correct contract: calling MarkRead on a resolved message is harmless.
        var svc = NewService();
        await svc.RaiseAsync("Info", "System", "subject", "body", "key:idempotent");
        var id = (await svc.ListAsync())[0].Id;
        await svc.ResolveAsync(id);  // transition to Resolved

        var result = await svc.MarkReadAsync(id);

        Assert.That(result, Is.True, "MarkReadAsync on an already-Resolved message must return true (idempotent)");
    }

    [Test]
    public async Task RaiseAsync_ReopenAfterResolve_UpdatesSeverityCategoryAndSubject()
    {
        // Regression: the reopen path (status Resolved/Read → New) updated Status, Body, and CreatedUtc
        // but NOT Severity, Category, or Subject. An escalated recurrence (e.g. Warning→Error) would be
        // silently stored with the old, lower Severity, hiding the escalation from the admin.
        var svc = NewService();
        await svc.RaiseAsync("Warning", "Render", "Old Subject", "old body", "key:reopen-fields");
        var id = (await svc.ListAsync())[0].Id;
        await svc.ResolveAsync(id);

        // Reopen with escalated severity, different category, and new subject.
        await svc.RaiseAsync("Error", "System", "New Subject", "new body", "key:reopen-fields");

        var all = await svc.ListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(all, Has.Count.EqualTo(1), "still one row (reopened, not duplicated)");
            Assert.That(all[0].Status, Is.EqualTo("New"), "status must be reopened to New");
            Assert.That(all[0].Severity, Is.EqualTo("Error"), "Severity must be updated on reopen");
            Assert.That(all[0].Category, Is.EqualTo("System"), "Category must be updated on reopen");
            Assert.That(all[0].Subject, Is.EqualTo("New Subject"), "Subject must be updated on reopen");
            Assert.That(all[0].Body, Is.EqualTo("new body"), "Body must be updated on reopen");
        });
    }

    [Test]
    public async Task ResolveAsync_AlreadyResolved_IsIdempotent_AndPreservesOriginalTimestamp()
    {
        // Regression: ResolveAsync had no idempotency guard; a second call re-wrote ResolvedUtc to
        // DateTime.UtcNow, silently losing the original resolution timestamp. Now the first call wins
        // and subsequent calls return true without modifying the row.
        var svc = NewService();
        await svc.RaiseAsync("Warning", "System", "subject", "body", "key:resolve-idempotent");
        var id = (await svc.ListAsync())[0].Id;

        var first = await svc.ResolveAsync(id);
        var rowAfterFirst = (await svc.ListAsync())[0];
        var timestampAfterFirst = rowAfterFirst.ResolvedUtc;

        await Task.Delay(1);   // ensure clock advances if the guard is missing
        var second = await svc.ResolveAsync(id);
        var rowAfterSecond = (await svc.ListAsync())[0];

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.True, "second ResolveAsync on already-Resolved must return true (idempotent)");
            Assert.That(rowAfterSecond.ResolvedUtc, Is.EqualTo(timestampAfterFirst),
                "ResolvedUtc must not be overwritten by a repeated Resolve call");
        });
    }
}
