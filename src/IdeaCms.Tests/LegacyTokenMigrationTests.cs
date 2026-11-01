using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Rendering;
using IdeaCms.Core.Services;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Tests;

/// <summary>
/// The retired brace grammar ({{ Kind.Key }}) is migrated to component tags at startup. Regression focus:
/// the brace form also carried PARAMETERS, and the original migration regex only matched the bare form —
/// so {{ MindAttic.Ideas.Component.TabBoard alwaysShowTabPage=true }} was left untouched and rendered on
/// the MindAttic front page as literal text.
/// </summary>
[TestFixture]
public class LegacyTokenMigrationTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    /// <summary>Run SeedAsync over a page carrying <paramref name="body"/> and return its migrated body.</summary>
    private static async Task<string> MigrateAsync(string body)
    {
        var factory = new InMemoryFactory("legacy_" + Guid.NewGuid().ToString("N"));
        await using (var db = factory.CreateDbContext())
        {
            var site = new Site { Key = "default", Name = "MindAttic", IsDefault = true, DefaultThemeKey = "cyberspace", CreatedUtc = DateTime.UtcNow };
            db.Sites.Add(site);
            await db.SaveChangesAsync();
            db.Pages.Add(new CmsPage
            {
                SiteId = site.Id, Slug = "legacy-page", Title = "Legacy",
                Kind = PageKind.Data, BodyHtml = body, BodyTrust = ContentTrust.Author,
                IsPublished = true, Enabled = true, CreatedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await new SeedService(factory).SeedAsync();

        await using var read = factory.CreateDbContext();
        var page = await read.Pages.IgnoreQueryFilters().FirstAsync(p => p.Slug == "legacy-page");
        return page.BodyHtml!;
    }

    [Test]
    public async Task BareBraceToken_BecomesComponentTag()
    {
        Assert.That(await MigrateAsync("{{ MindAttic.Ideas.Component.IdeasBrochure }}"),
            Is.EqualTo("<Component.IdeasBrochure />"));
    }

    [Test]
    public async Task BraceTokenWithParameter_IsMigratedAndKeepsTheParameter()
    {
        // The exact token that survived on /frontpage.
        var migrated = await MigrateAsync("{{ MindAttic.Ideas.Component.TabBoard alwaysShowTabPage=true }}");

        Assert.That(migrated, Is.EqualTo("<Component.TabBoard alwaysShowTabPage=\"true\" />"));
    }

    [Test]
    public async Task BraceTokenWithMultipleAndQuotedParameters_IsPreserved()
    {
        var migrated = await MigrateAsync("""{{ Component.Hero title="Hello World" compact=true }}""");

        Assert.That(migrated, Is.EqualTo("""<Component.Hero title="Hello World" compact="true" />"""));
    }

    [Test]
    public async Task VersionedBraceTokenWithParameter_KeepsBothVersionAndParameter()
    {
        var migrated = await MigrateAsync("{{ MindAttic.Ideas.Component.TabBoard.V2 compact=true }}");

        Assert.That(migrated, Is.EqualTo("<Component.TabBoard data-version=\"2\" compact=\"true\" />"));
    }

    [Test]
    public async Task MigratedTokenIsParseableAsAnIncludeReference()
    {
        // The point of migrating is that the guard/hoist path can SEE the reference; a token that merely
        // stops looking like a moustache but parses to nothing would be a silent regression.
        var migrated = await MigrateAsync("{{ MindAttic.Ideas.Component.TabBoard alwaysShowTabPage=true }}");

        var refs = IncludeReferenceParser.Parse(migrated);

        Assert.Multiple(() =>
        {
            Assert.That(refs, Has.Count.EqualTo(1));
            Assert.That(refs[0].Kind, Is.EqualTo(ContentKind.Component));
            Assert.That(refs[0].Key, Is.EqualTo("tabboard"));
        });
    }

    [Test]
    public async Task NoBraceTokensLeftBehind()
    {
        var migrated = await MigrateAsync(
            "<p>intro</p>{{ MindAttic.Ideas.Component.TabBoard alwaysShowTabPage=true }}<p>outro</p>");

        Assert.Multiple(() =>
        {
            Assert.That(migrated, Does.Not.Contain("{{"));
            Assert.That(migrated, Does.Contain("<p>intro</p>"));
            Assert.That(migrated, Does.Contain("<p>outro</p>"));
        });
    }
}
