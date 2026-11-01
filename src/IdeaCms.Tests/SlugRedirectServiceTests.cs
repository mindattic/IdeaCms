using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Tests;

[TestFixture]
public class SlugRedirectServiceTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    private static IDbContextFactory<CmsDbContext> NewFactory() =>
        new InMemoryFactory("slug_" + Guid.NewGuid().ToString("N"));

    private static async Task<int> SeedPublishedPageAsync(
        IDbContextFactory<CmsDbContext> factory, string slug, int? siteId = null)
    {
        await using var db = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = slug, Title = "Test Page", Kind = PageKind.Data,
            BodyTrust = ContentTrust.Untrusted, SiteId = siteId,
            IsPublished = true, Enabled = true, CreatedUtc = DateTime.UtcNow,
        };
        db.Pages.Add(page);
        await db.SaveChangesAsync();
        return page.Id;
    }

    [Test]
    public async Task CheckRedirect_NoHistory_ReturnsNull()
    {
        var factory = NewFactory();
        var svc = new SlugRedirectService(factory);
        await SeedPublishedPageAsync(factory, "about");

        var result = await svc.CheckRedirectAsync(null, "old-about");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CheckRedirect_MatchingHistory_Returns301ToCurrentSlug()
    {
        var factory = NewFactory();
        var svc = new SlugRedirectService(factory);
        var pageId = await SeedPublishedPageAsync(factory, "about");

        await using var db = factory.CreateDbContext();
        db.PageSlugHistory.Add(new PageSlugHistory
        {
            PageId = pageId, OldSlug = "old-about", IsVanity = false, CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await svc.CheckRedirectAsync(null, "old-about");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.TargetSlug, Is.EqualTo("about"));
            Assert.That(result.StatusCode, Is.EqualTo(301));
        });
    }

    [Test]
    public async Task CheckRedirect_SameSlugInHistory_ReturnsNull()
    {
        // Edge case: old slug = current slug (shouldn't redirect to itself).
        var factory = NewFactory();
        var svc = new SlugRedirectService(factory);
        var pageId = await SeedPublishedPageAsync(factory, "about");

        await using var db = factory.CreateDbContext();
        db.PageSlugHistory.Add(new PageSlugHistory
        {
            PageId = pageId, OldSlug = "about", IsVanity = false, CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await svc.CheckRedirectAsync(null, "about");

        Assert.That(result, Is.Null, "no redirect when old slug == current slug");
    }

    [Test]
    public async Task CheckRedirect_UnpublishedPage_ReturnsNull()
    {
        var factory = NewFactory();
        var svc = new SlugRedirectService(factory);

        await using var setupDb = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "hidden", Title = "Hidden", Kind = PageKind.Data,
            BodyTrust = ContentTrust.Untrusted,
            IsPublished = false, Enabled = true, CreatedUtc = DateTime.UtcNow,
        };
        setupDb.Pages.Add(page);
        await setupDb.SaveChangesAsync();
        setupDb.PageSlugHistory.Add(new PageSlugHistory
        {
            PageId = page.Id, OldSlug = "old-hidden", IsVanity = false, CreatedUtc = DateTime.UtcNow,
        });
        await setupDb.SaveChangesAsync();

        var result = await svc.CheckRedirectAsync(null, "old-hidden");

        Assert.That(result, Is.Null, "unpublished pages should not redirect");
    }

    [Test]
    public async Task AddVanityRedirect_WritesIsVanityEntry()
    {
        var factory = NewFactory();
        var svc = new SlugRedirectService(factory);
        var pageId = await SeedPublishedPageAsync(factory, "about");

        var ok = await svc.AddVanityRedirectAsync(pageId, "company/about-us", "admin");

        Assert.That(ok, Is.True);

        var history = await svc.GetHistoryAsync(pageId);
        Assert.Multiple(() =>
        {
            Assert.That(history, Has.Count.EqualTo(1));
            Assert.That(history[0].IsVanity, Is.True);
            Assert.That(history[0].OldSlug, Is.EqualTo("company/about-us"));
        });
    }

    [Test]
    public async Task AddVanityRedirect_Idempotent_DoesNotDuplicate()
    {
        var factory = NewFactory();
        var svc = new SlugRedirectService(factory);
        var pageId = await SeedPublishedPageAsync(factory, "about");

        await svc.AddVanityRedirectAsync(pageId, "legacy", "admin");
        var ok = await svc.AddVanityRedirectAsync(pageId, "legacy", "admin");

        Assert.That(ok, Is.True);
        var history = await svc.GetHistoryAsync(pageId);
        Assert.That(history, Has.Count.EqualTo(1), "duplicate vanity not stored twice");
    }

    [Test]
    public async Task AddVanityRedirect_UnknownPage_ReturnsFalse()
    {
        var factory = NewFactory();
        var svc = new SlugRedirectService(factory);

        var ok = await svc.AddVanityRedirectAsync(9999, "legacy");

        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task AddVanityRedirect_CurrentSlug_IsNoOpWithNoHistoryEntry()
    {
        // Regression: AddVanityRedirectAsync only checked PageSlugHistory for idempotency; it did not
        // check whether vanitySlug == page.Slug (the live slug), so it wrote a confusing history row
        // that pointed a page's own slug back at itself.
        var factory = NewFactory();
        var svc = new SlugRedirectService(factory);
        var pageId = await SeedPublishedPageAsync(factory, "about");

        var ok = await svc.AddVanityRedirectAsync(pageId, "about");

        Assert.That(ok, Is.True, "same-as-current returns true (success, nothing to do)");
        var history = await svc.GetHistoryAsync(pageId);
        Assert.That(history, Is.Empty, "no history entry created for the page's own current slug");
    }

    [Test]
    public async Task CheckRedirect_LegacyMixedCaseSlug_ReturnsNormalizedTarget()
    {
        // Regression: CheckRedirectAsync returned match.Slug verbatim; a legacy page with a mixed-case
        // slug in the DB produced a non-lowercase 301 target, breaking URL canonicalization.
        var factory = NewFactory();
        var svc = new SlugRedirectService(factory);

        await using var setup = factory.CreateDbContext();
        var page = new CmsPage
        {
            // Stored with mixed case — simulates a legacy row written before slug normalisation was enforced.
            Slug = "About-Us", Title = "About", Kind = PageKind.Data,
            BodyTrust = ContentTrust.Untrusted,
            IsPublished = true, Enabled = true, CreatedUtc = DateTime.UtcNow,
        };
        setup.Pages.Add(page);
        await setup.SaveChangesAsync();
        setup.PageSlugHistory.Add(new PageSlugHistory
        {
            PageId = page.Id, OldSlug = "old-about", IsVanity = false, CreatedUtc = DateTime.UtcNow,
        });
        await setup.SaveChangesAsync();

        var result = await svc.CheckRedirectAsync(null, "old-about");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.TargetSlug, Is.EqualTo("about-us"), "target slug must be normalized to lowercase");
    }

    [Test]
    public async Task CheckRedirect_MixedCaseIncomingSlug_StillMatchesLowercaseStoredHistory()
    {
        // Regression: CheckRedirectAsync compared the raw incoming slug directly against OldSlug rows
        // (always stored lowercase), so mixed-case requests missed the redirect match.  The fix
        // lowercases the slug before the DB query.
        var factory = NewFactory();
        var svc = new SlugRedirectService(factory);
        var pageId = await SeedPublishedPageAsync(factory, "about");

        await using var db = factory.CreateDbContext();
        db.PageSlugHistory.Add(new PageSlugHistory
        {
            PageId = pageId, OldSlug = "old-about", IsVanity = false, CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        // Request arrives with mixed-case path; stored history has lowercase "old-about".
        var result = await svc.CheckRedirectAsync(null, "OLD-ABOUT");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null, "mixed-case incoming slug must match lowercase history entry");
            Assert.That(result!.TargetSlug, Is.EqualTo("about"));
        });
    }

    [Test]
    public async Task AddVanityRedirect_SoftDeletedPage_ReturnsTrueAndRecordsEntry()
    {
        // Regression: AddVanityRedirectAsync used the default EF filter (no IgnoreQueryFilters) on the
        // page-slug lookup, so it returned false for any soft-deleted page, making it impossible for an
        // admin to set up a vanity alias before restoring the page.
        var factory = NewFactory();
        var svc = new SlugRedirectService(factory);

        await using var setup = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "archived", Title = "Archived", Kind = PageKind.Data,
            BodyTrust = ContentTrust.Untrusted,
            IsPublished = false, Enabled = false,
            IsDeleted = true, DeletedUtc = DateTime.UtcNow, CreatedUtc = DateTime.UtcNow,
        };
        setup.Pages.Add(page);
        await setup.SaveChangesAsync();

        var ok = await svc.AddVanityRedirectAsync(page.Id, "legacy-archived", "admin");

        Assert.That(ok, Is.True, "AddVanityRedirectAsync must succeed even for a soft-deleted page");
        var history = await svc.GetHistoryAsync(page.Id);
        Assert.That(history, Has.Count.EqualTo(1), "vanity history entry must be recorded");
        Assert.That(history[0].OldSlug, Is.EqualTo("legacy-archived"));
    }
}
