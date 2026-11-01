using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Tests;

/// <summary>
/// Tests for <see cref="PageHistoryService"/>. <see cref="IPageHistoryService.GetHistoryAsync"/> uses
/// EF Core <c>TemporalAll()</c>, which requires a SQL Server provider and cannot be tested here with
/// InMemory. <see cref="IPageHistoryService.RestoreAsync"/> takes a pre-fetched snapshot and is DB-agnostic,
/// so it is fully covered below.
/// </summary>
[TestFixture]
public class PageHistoryServiceTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    private static IDbContextFactory<CmsDbContext> NewFactory() =>
        new InMemoryFactory("hist_" + Guid.NewGuid().ToString("N"));

    private static async Task<CmsPage> SeedPageAsync(IDbContextFactory<CmsDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "about", Title = "Current Title", Kind = PageKind.Data,
            BodyHtml = "<p>current</p>", IsPublished = true, Enabled = true,
            BodyTrust = ContentTrust.Untrusted, CreatedUtc = DateTime.UtcNow,
        };
        db.Pages.Add(page);
        await db.SaveChangesAsync();
        return page;
    }

    [Test]
    public async Task RestoreAsync_CopiesSnapshotContentFields_OntoCurrentPage()
    {
        var factory = NewFactory();
        var page = await SeedPageAsync(factory);
        var snapshot = new PageHistoryEntry(
            page.Id, "about", "Old Title", false, true, PageKind.Data,
            "cyberspace", 2, "<p>old body</p>", "body{color:red}", null,
            ContentTrust.Untrusted,
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1));

        var ok = await new PageHistoryService(factory).RestoreAsync(snapshot, new ClaimsPrincipal());

        await using var verify = factory.CreateDbContext();
        var restored = await verify.Pages.FindAsync(page.Id);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(restored!.Title, Is.EqualTo("Old Title"));
            Assert.That(restored.BodyHtml, Is.EqualTo("<p>old body</p>"));
            Assert.That(restored.PageCss, Is.EqualTo("body{color:red}"));
            Assert.That(restored.ThemeKey, Is.EqualTo("cyberspace"));
            Assert.That(restored.ThemeVersion, Is.EqualTo(2));
            Assert.That(restored.IsPublished, Is.False);
        });
    }

    [Test]
    public async Task RestoreAsync_ReStampsTrust_FromRestoringUserClaims()
    {
        var factory = NewFactory();
        var page = await SeedPageAsync(factory);
        var snapshot = new PageHistoryEntry(
            page.Id, "about", "Old", true, true, PageKind.Data,
            null, null, "<script>alert(1)</script>", null, null,
            ContentTrust.Untrusted,
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);

        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(CmsClaims.AuthorRawMarkup, "true"),
            new Claim("ma:uid", "admin-42"),
        ], "Test"));

        await new PageHistoryService(factory).RestoreAsync(snapshot, admin);

        await using var verify = factory.CreateDbContext();
        var restored = await verify.Pages.FindAsync(page.Id);
        Assert.Multiple(() =>
        {
            Assert.That(restored!.BodyTrust, Is.EqualTo(ContentTrust.Author));
            Assert.That(restored.AuthoredByUserId, Is.EqualTo("admin-42"));
        });
    }

    [Test]
    public async Task RestoreAsync_NonAdminUser_StampsUntrusted()
    {
        var factory = NewFactory();
        var page = await SeedPageAsync(factory);
        var snapshot = new PageHistoryEntry(
            page.Id, "about", "Old", true, true, PageKind.Data,
            null, null, "<p>safe</p>", null, null,
            ContentTrust.Author,
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);

        // A user without Cms.AuthorRawMarkup
        var viewer = new ClaimsPrincipal(new ClaimsIdentity([new Claim("ma:uid", "viewer-1")], "Test"));

        await new PageHistoryService(factory).RestoreAsync(snapshot, viewer);

        await using var verify = factory.CreateDbContext();
        var restored = await verify.Pages.FindAsync(page.Id);
        Assert.That(restored!.BodyTrust, Is.EqualTo(ContentTrust.Untrusted));
    }

    [Test]
    public async Task RestoreAsync_UnknownPage_ReturnsFalse()
    {
        var snapshot = new PageHistoryEntry(
            999, "missing", "Gone", false, false, PageKind.Data,
            null, null, null, null, null,
            ContentTrust.Untrusted,
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);

        var ok = await new PageHistoryService(NewFactory()).RestoreAsync(snapshot, new ClaimsPrincipal());

        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task RestoreAsync_RestoresMissingFields_SeoTitle_ActivePlugins_IsRestricted_Kind()
    {
        // Regression: RestoreAsync omitted SeoTitle, ActivePluginsJson, IsRestricted, and Kind,
        // so reverting a snapshot left those fields at their current (post-edit) values.
        var factory = NewFactory();
        var page = await SeedPageAsync(factory);
        var snapshot = new PageHistoryEntry(
            page.Id, "about", "Old Title", true, true, PageKind.Code,
            null, null, "<p>body</p>", null, null, ContentTrust.Untrusted,
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1),
            SeoTitle: "Old SEO", ActivePluginsJson: """["Plugin.nav"]""", IsRestricted: true);

        var ok = await new PageHistoryService(factory).RestoreAsync(snapshot, new ClaimsPrincipal());

        await using var verify = factory.CreateDbContext();
        var restored = await verify.Pages.FindAsync(page.Id);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(restored!.Kind, Is.EqualTo(PageKind.Code), "Kind must be restored");
            Assert.That(restored.SeoTitle, Is.EqualTo("Old SEO"), "SeoTitle must be restored");
            Assert.That(restored.ActivePluginsJson, Is.EqualTo("""["Plugin.nav"]"""), "ActivePluginsJson must be restored");
            Assert.That(restored.IsRestricted, Is.True, "IsRestricted must be restored");
        });
    }

    [Test]
    public void GetHistoryAsync_RequiresSqlServer_ThrowsOnInMemoryDb()
    {
        // GetHistoryAsync uses EF Core TemporalAll() which only works with SQL Server temporal
        // tables. On InMemory this throws, documenting the SQL Server requirement for M.IDEA-US-B5.
        var svc = new PageHistoryService(NewFactory());
        Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetHistoryAsync(1));
    }

    [Test]
    public async Task RestoreAsync_WorkflowStateDraft_OverridesIsPublishedToFalse()
    {
        // Regression: RestoreAsync applied snapshot.IsPublished and snapshot.WorkflowState independently;
        // restoring a snapshot with WorkflowState="Draft" but IsPublished=true produced an inconsistent page.
        // Now WorkflowState drives IsPublished when non-null.
        var factory = NewFactory();
        var page = await SeedPageAsync(factory);   // IsPublished = true

        var snapshot = new PageHistoryEntry(
            page.Id, "about", "Old", IsPublished: true, Enabled: true, PageKind.Data,
            null, null, "<p>x</p>", null, null, ContentTrust.Untrusted,
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1),
            WorkflowState: "Draft");

        await new PageHistoryService(factory).RestoreAsync(snapshot, new ClaimsPrincipal());

        await using var verify = factory.CreateDbContext();
        var restored = await verify.Pages.FindAsync(page.Id);
        Assert.Multiple(() =>
        {
            Assert.That(restored!.WorkflowState, Is.EqualTo("Draft"));
            Assert.That(restored.IsPublished, Is.False, "WorkflowState=Draft must override IsPublished to false");
        });
    }

    [Test]
    public async Task RestoreAsync_WorkflowStatePublished_OverridesIsPublishedToTrue()
    {
        // Counterpart: WorkflowState="Published" must force IsPublished=true even if snapshot.IsPublished=false.
        var factory = NewFactory();
        var page = await SeedPageAsync(factory);

        // Unpublish so we can verify the restore drives it back to published via WorkflowState.
        await using (var unpublish = factory.CreateDbContext())
        {
            var row = await unpublish.Pages.FindAsync(page.Id);
            row!.IsPublished = false;
            await unpublish.SaveChangesAsync();
        }

        var snapshot = new PageHistoryEntry(
            page.Id, "about", "Old", IsPublished: false, Enabled: true, PageKind.Data,
            null, null, "<p>x</p>", null, null, ContentTrust.Untrusted,
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1),
            WorkflowState: "Published");

        await new PageHistoryService(factory).RestoreAsync(snapshot, new ClaimsPrincipal());

        await using var verify = factory.CreateDbContext();
        var restored = await verify.Pages.FindAsync(page.Id);
        Assert.Multiple(() =>
        {
            Assert.That(restored!.WorkflowState, Is.EqualTo("Published"));
            Assert.That(restored.IsPublished, Is.True, "WorkflowState=Published must override IsPublished to true");
        });
    }

    [Test]
    public async Task RestoreAsync_SoftDeletedPage_Succeeds()
    {
        // Regression: RestoreAsync used the default EF filter and returned false when the target page was
        // soft-deleted, making it impossible to restore a page's content as part of an undelete flow.
        // IgnoreQueryFilters() ensures a snapshot can always be applied to any page by id.
        var factory = NewFactory();

        // Insert a soft-deleted page directly — cannot use SeedPageAsync because that inserts a live row.
        await using (var setup = factory.CreateDbContext())
        {
            setup.Pages.Add(new CmsPage
            {
                Slug = "soft-del", Title = "Soft Deleted", Kind = PageKind.Data,
                BodyHtml = "<p>current</p>", IsPublished = true, Enabled = true,
                IsDeleted = true, DeletedUtc = DateTime.UtcNow,
                BodyTrust = ContentTrust.Untrusted, CreatedUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        await using var dbForId = factory.CreateDbContext();
        var pageId = await dbForId.Pages.IgnoreQueryFilters()
            .Where(p => p.Slug == "soft-del").Select(p => p.Id).SingleAsync();

        var snapshot = new PageHistoryEntry(
            pageId, "soft-del", "Restored Title", false, true, PageKind.Data,
            null, null, "<p>restored</p>", null, null, ContentTrust.Untrusted,
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow);

        var ok = await new PageHistoryService(factory).RestoreAsync(snapshot, new ClaimsPrincipal());

        Assert.That(ok, Is.True, "RestoreAsync must succeed even when the target page is soft-deleted");
    }
}
