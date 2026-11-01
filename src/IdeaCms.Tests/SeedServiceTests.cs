using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Tests;

/// <summary>
/// SeedService idempotency: re-running against a DB that already has (possibly soft-deleted) rows
/// at the seeded slugs must not throw or create duplicate rows.
/// </summary>
[TestFixture]
public class SeedServiceTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    [Test]
    public async Task SeedAsync_SoftDeletedSeedPage_SkipsReinsert_NoDuplicate()
    {
        // Regression: AnyAsync page-slug guards used the default EF query filter, so a soft-deleted
        // page at e.g. "personas" was invisible → AnyAsync returned false → SeedAsync tried to INSERT
        // a second row → DbUpdateException on the unique (SiteId, Slug) index on real databases.
        // InMemory doesn't enforce the constraint, so the bug is verified by checking that exactly
        // one "personas" row exists after SeedAsync: with IgnoreQueryFilters the AnyAsync finds the
        // soft-deleted row and skips the INSERT; without the fix a second row would be inserted.
        var factory = new InMemoryFactory("seed_soft_" + Guid.NewGuid().ToString("N"));

        int siteId;
        await using (var setup = factory.CreateDbContext())
        {
            var site = new Site
            {
                Key = "default", Name = "MindAttic", HostBindings = "",
                DefaultThemeKey = "cyberspace", DefaultThemeVersion = 1,
                IsDefault = true, CreatedUtc = DateTime.UtcNow,
            };
            setup.Sites.Add(site);
            await setup.SaveChangesAsync();
            siteId = site.Id;

            // Pre-seed a soft-deleted page at "personas" — simulates a slug the admin deleted.
            setup.Pages.Add(new CmsPage
            {
                SiteId = siteId, Slug = "personas", Title = "Old Personas",
                Kind = PageKind.Data, BodyHtml = "<p>old</p>",
                BodyTrust = ContentTrust.Untrusted,
                IsPublished = false, Enabled = true, IsDeleted = true,
                CreatedUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        Assert.DoesNotThrowAsync(async () => await new SeedService(factory).SeedAsync(),
            "SeedAsync must not throw when a soft-deleted page occupies a seeded slug");

        // Verify: exactly one row at "personas", not two.
        await using var verify = factory.CreateDbContext();
        var count = await verify.Pages.IgnoreQueryFilters().CountAsync(p => p.SiteId == siteId && p.Slug == "personas");
        Assert.That(count, Is.EqualTo(1), "soft-deleted 'personas' slug must not receive a second row from SeedAsync");
    }

    [Test]
    public async Task SeedAsync_PageWithUnknownBody_IsNotTouchedAndDoesNotThrow()
    {
        // Regression guard: SeedAsync no longer runs any body-migration pass. A page with an
        // unrecognisable body (e.g. a legacy XML include tag from a very old install) is left
        // untouched and SeedAsync must complete without throwing.
        var factory = new InMemoryFactory("seed_legacytag_" + Guid.NewGuid().ToString("N"));
        int siteId;
        const string originalBody = "<p><MindAttic.Ideas.Component.Textbox /></p>";
        await using (var setup = factory.CreateDbContext())
        {
            var site = new Site
            {
                Key = "default", Name = "Test", HostBindings = "",
                DefaultThemeKey = "cyberspace", DefaultThemeVersion = 1,
                IsDefault = true, CreatedUtc = DateTime.UtcNow,
            };
            setup.Sites.Add(site);
            await setup.SaveChangesAsync();
            siteId = site.Id;

            setup.Pages.Add(new CmsPage
            {
                SiteId = siteId, Slug = "legacy-body", Title = "Legacy Body",
                Kind = PageKind.Data,
                BodyHtml = originalBody,
                BodyTrust = ContentTrust.Untrusted,
                IsPublished = false, Enabled = false,
                IsDeleted = true, DeletedUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        Assert.DoesNotThrowAsync(async () => await new SeedService(factory).SeedAsync(),
            "SeedAsync must not throw even when a page has an unrecognised body");

        // Body is NOT migrated — SeedAsync no longer transforms existing page bodies.
        await using var verify = factory.CreateDbContext();
        var page = await verify.Pages.IgnoreQueryFilters()
            .SingleAsync(p => p.SiteId == siteId && p.Slug == "legacy-body");
        Assert.That(page.BodyHtml, Is.EqualTo(originalBody),
            "SeedAsync must not alter a page body it did not seed");
    }

    [Test]
    public async Task SeedAsync_FreshDb_CreatesExpectedSeedPages()
    {
        // Smoke test: a fresh DB gets the expected seeded pages from SeedAsync.
        var factory = new InMemoryFactory("seed_fresh_" + Guid.NewGuid().ToString("N"));
        await new SeedService(factory).SeedAsync();

        await using var db = factory.CreateDbContext();
        var slugs = await db.Pages.Select(p => p.Slug).ToListAsync();
        Assert.That(slugs, Does.Contain("frontpage"), "frontpage must be seeded on a fresh DB");
    }

    [Test]
    public async Task WithAnIdealist_SeedsOnlyTheStructuralMinimum()
    {
        // A custom instance (a public demo, say) owns its content through its idealist: it must not come
        // up carrying MindAttic's own pages, nav or chrome.
        var factory = new InMemoryFactory("seed_idealist_" + Guid.NewGuid().ToString("N"));
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["IdeaCms:Idealist"] = "seed/demo.idealist" }).Build();

        await new SeedService(factory, config).SeedAsync();

        await using var db = factory.CreateDbContext();
        var site = await db.Sites.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(db.Pages.Count(), Is.Zero, "no MindAttic pages");
            Assert.That(db.Settings.Select(s => s.Key).ToList(), Is.EqualTo(new[] { "css.global" }), "no nav/footer/plugin chrome");
            Assert.That(site.Name, Is.EqualTo("IdeaCms"));
            Assert.That(site.DefaultThemeKey, Is.EqualTo("ideas"));
        });
    }
}
