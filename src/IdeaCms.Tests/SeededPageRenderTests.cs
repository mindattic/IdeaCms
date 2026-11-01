using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Rendering;
using IdeaCms.Core.Services;
using IdeaCms.Packaging;
using IdeaCms.Tests.Packaging;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Tests;

/// <summary>
/// Proves the automatable mechanics of M.IDEA-US-A6 for the seeded FRONTPAGE (the mindattic.com
/// recreation in SeedService): the Data-page body uses the correct token grammar, the install →
/// catalog → IncludeExpander pipeline resolves those tokens to Component frames (not
/// MissingContent placeholders), and the seed's create/migrate behavior holds (fresh DB → Data
/// frontpage; stock compiled frontpage migrates in place; admin-edited pages are never clobbered;
/// the stock "" home page is soft-disabled, never deleted).
///
/// The live render through the running host (Cyberspace theme + Blazor circuit) is not
/// automatable and remains the "attended" portion of A6.
/// </summary>
[TestFixture]
public class SeededPageRenderTests
{
    // Sample PascalCase tags (no data-version = float to latest) to verify the parser handles each kind correctly.
    private const string SeedTabsTag    = "<Component.Tabs />";
    private const string SeedGalleryTag = "<Component.Gallery />";
    private const string SeedFooterTag  = "<Plugin.Footer />";

    [TestCase(SeedTabsTag,    "tabs",    ContentKind.Component)]
    [TestCase(SeedGalleryTag, "gallery", ContentKind.Component)]
    [TestCase(SeedFooterTag,  "footer",  ContentKind.Plugin)]
    public void SeedBodyTags_ParseToCorrectKind_FloatingVersion(
        string seedTag, string expectedKey, ContentKind expectedKind)
    {
        var refs = IncludeReferenceParser.Parse(seedTag);

        Assert.That(refs, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(refs[0].Kind,    Is.EqualTo(expectedKind));
            Assert.That(refs[0].Key,     Is.EqualTo(expectedKey));
            Assert.That(refs[0].Version, Is.Null, "floating tags have no version pin");
        });
    }

    [Test]
    public async Task FrontpageBody_AllSeedTokens_ParseFromTheRealSeededPage()
    {
        // Parse the ACTUAL seeded body: the frontpage must reference the self-contained
        // MindAtticFrontpage widget (which carries Tabs/Gallery/Footer internally).
        var factory = new InMemoryFactory("seed_" + Guid.NewGuid().ToString("N"));
        await new SeedService(factory).SeedAsync();

        await using var db = factory.CreateDbContext();
        var front = await db.Pages.SingleAsync(p => p.Slug == "frontpage");
        var refs = IncludeReferenceParser.Parse(front.BodyHtml);

        Assert.Multiple(() =>
        {
            Assert.That(front.Kind, Is.EqualTo(PageKind.Data), "the frontpage is a Data page");
            Assert.That(front.IsPublished && front.Enabled, Is.True);
            Assert.That(refs.Select(r => (r.Kind, r.Key, r.Version)), Is.EquivalentTo(new[]
            {
                (ContentKind.Component, "ideacmsfrontpage", (int?)null),
            }));
        });
    }

    [Test]
    public async Task Seed_CreatesPersonasPage_CollapsingLegionFrontendIntoOneToken()
    {
        // M.IDEA-US-F7: the MindAttic.Legion.Frontend standalone app is one Data page
        // composing the LegionPersonas widget .idea by token.
        var factory = new InMemoryFactory("seed_" + Guid.NewGuid().ToString("N"));
        await new SeedService(factory).SeedAsync();

        await using var db = factory.CreateDbContext();
        var personas = await db.Pages.SingleAsync(p => p.Slug == "personas");
        var refs = IncludeReferenceParser.Parse(personas.BodyHtml);

        Assert.Multiple(() =>
        {
            Assert.That(personas.Kind, Is.EqualTo(PageKind.Data));
            Assert.That(personas.IsPublished && personas.Enabled, Is.True);
            Assert.That(refs.Select(r => (r.Kind, r.Key, r.Version)), Is.EquivalentTo(new[]
            {
                (ContentKind.Component, "legionpersonas", (int?)null),
            }));
        });
    }

    [TestCase("MindAttic.Ideas.Page.Frontpage.V1")]
    [TestCase("MindAttic.Ideas.Page.MindAtticFrontpage.V1")]   // pre-rename stock name in older DBs
    public async Task Seed_MigratesStockCodeFrontpage_ToDataPage_ButNeverAnAdminPage(string stockTypeName)
    {
        var factory = new InMemoryFactory("seed_" + Guid.NewGuid().ToString("N"));

        // Arrange a pre-existing DB carrying the OLD stock compiled frontpage and an admin-authored
        // code page beside it.
        await using (var db = factory.CreateDbContext())
        {
            var site = new Site
            {
                Key = "default", Name = "MindAttic", HostBindings = "",
                DefaultThemeKey = "cyberspace", DefaultThemeVersion = 1, IsDefault = true,
                CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
            };
            db.Sites.Add(site);
            await db.SaveChangesAsync();
            db.Pages.Add(new CmsPage
            {
                SiteId = site.Id, Slug = "frontpage", Title = "MindAttic",
                Kind = PageKind.Code,
                ComponentTypeName = stockTypeName,
                AssemblyName = "MindAttic.Ideas.Page.Frontpage",
                IsPublished = true, Enabled = true,
                CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
            });
            db.Pages.Add(new CmsPage
            {
                SiteId = site.Id, Slug = "custom", Title = "Custom",
                Kind = PageKind.Code,
                ComponentTypeName = "My.Custom.Page.V1", AssemblyName = "My.Custom",
                IsPublished = true, Enabled = true,
                CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await new SeedService(factory).SeedAsync();

        await using (var db = factory.CreateDbContext())
        {
            var front = await db.Pages.SingleAsync(p => p.Slug == "frontpage");
            var custom = await db.Pages.SingleAsync(p => p.Slug == "custom");
            Assert.Multiple(() =>
            {
                Assert.That(front.Kind, Is.EqualTo(PageKind.Data), "stock compiled frontpage migrates to Data");
                Assert.That(front.ComponentTypeName, Is.Null);
                Assert.That(front.BodyHtml, Is.EqualTo("<Component.IdeaCmsFrontpage />"));
                Assert.That(custom.Kind, Is.EqualTo(PageKind.Code), "admin page is never clobbered");
                Assert.That(custom.ComponentTypeName, Is.EqualTo("My.Custom.Page.V1"));
            });
        }
    }

    [Test]
    public async Task Seed_SoftDisablesStockHomePage_AndNeverAnEditedOne()
    {
        var factory = new InMemoryFactory("seed_" + Guid.NewGuid().ToString("N"));

        // First seed run on a fresh DB: no "" page is created at all (the bare route forwards).
        await new SeedService(factory).SeedAsync();
        await using (var db = factory.CreateDbContext())
        {
            Assert.That(await db.Pages.AnyAsync(p => p.Slug == ""), Is.False,
                "a fresh seed no longer creates a bare-slug home page");

            // Arrange the legacy stock home page (as an old DB would carry) plus an edited variant check.
            var site = await db.Sites.SingleAsync();
            db.Pages.Add(new CmsPage
            {
                SiteId = site.Id, Slug = "", Title = "Home",
                Kind = PageKind.Data, BodyHtml = LegacyHomeBody(), BodyTrust = ContentTrust.Author,
                IsPublished = true, Enabled = true,
                CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await new SeedService(factory).SeedAsync();
        await using (var db2 = factory.CreateDbContext())
        {
            var home = await db2.Pages.SingleAsync(p => p.Slug == "");
            Assert.That(home.Enabled, Is.False, "the stock home page is soft-disabled (HOUSE-LAW-2), not deleted");

            home.BodyHtml = "<p>admin edited</p>";
            home.Enabled = true;
            await db2.SaveChangesAsync();
        }

        await new SeedService(factory).SeedAsync();
        await using (var db3 = factory.CreateDbContext())
        {
            var home = await db3.Pages.SingleAsync(p => p.Slug == "");
            Assert.That(home.Enabled, Is.True, "an admin-edited home page is never touched");
        }
    }

    [Test]
    public async Task SeedBody_InstalledTabsComponent_ExpandsToResolvedFrame()
    {
        // Proves the full pipeline for the A6 scenario: install a component with key "tabs"
        // (matching the seed body's <Component.Tabs /> tag), then verify IncludeExpander resolves it.
        var (svc, catalog) = BuildPipeline();

        var archive = IdeaTestArchive.Build(new Dictionary<string, string>
        {
            ["idea.json"] = ManifestReader.Write(new IdeaManifest
            {
                ManifestVersion = 1, Category = "Component", Kind = "code",
                Key = "tabs", Version = 1, DisplayName = "Tabs", Sdk = 1,
                EntryType    = typeof(MindAttic.Ideas.Abstractions.ComponentBase).FullName!,
                AssemblyName = "Demo",    // non-host fake; DefaultTypeResolver finds ComponentBase via scan
            }),
            ["bin/Demo.dll"] = "MZ-fake",
        });
        await svc.InstallAsync(archive, allowOverride: false);

        // The floating PascalCase tag "<Component.Tabs />" (no data-version = float to latest).
        var builder = new RenderTreeBuilder();
        var seq = 0;
        IncludeExpander.Expand(builder, ref seq, SeedTabsTag, catalog, new PassGate(), ContentTrust.Author);

        var frames = builder.GetFrames();
        bool hasResolved = false, hasMissing = false;
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType != RenderTreeFrameType.Component) continue;
            if (frames.Array[i].ComponentType == typeof(MissingContent)) hasMissing = true;
            else hasResolved = true;
        }

        Assert.Multiple(() =>
        {
            Assert.That(hasMissing,  Is.False, "seed Tabs tag must not degrade to MissingContent");
            Assert.That(hasResolved, Is.True,  "seed Tabs tag must resolve to a Component frame");
        });
    }

    // The retired stock home body, reproduced for the migration test (must match
    // SeedService.LegacyHomeBodyHtml byte for byte).
    private static string LegacyHomeBody() =>
        """
        <div class="ma-home">
          <h1>MindAttic.Ideas</h1>
          <p>This page is data — free-form HTML rendered through the Cyberspace theme.</p>

          <p>A <strong>Plugin</strong> switches on a capability (it loads the tooltip engine), so any
             element with <code>data-tooltip</code> works. No version = latest:</p>
          {{ MindAttic.Ideas.Plugin.Tooltip }}
          <p><button type="button" data-tooltip="Composed from MindAttic.UiUx — latest version.">Hover me</button></p>

          <p>A <strong>Component</strong> is an atomic element placed by token (attributes flow through):</p>
          <p>{{ MindAttic.Ideas.Component.Textbox placeholder="Type here…" }}</p>
        </div>
        """;

    // ---- shared pipeline infra ----

    private sealed class InMemoryFactory(string db) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(db).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class PassGate : IRawContentGate
    {
        public MarkupString Emit(string? html, ContentTrust trust) => new(html ?? "");
    }

    private static (PackageInstallService Svc, ContentCatalog Catalog) BuildPipeline()
    {
        var factory   = new InMemoryFactory("seed_" + Guid.NewGuid().ToString("N"));
        var resolver  = new DefaultTypeResolver();
        var catalog   = new ContentCatalog(resolver);
        var discovery = new DiscoveryService(factory, [], catalog);
        var blobs     = new InMemoryPackageBlobStore();
        var svc       = new PackageInstallService(factory, discovery, blobs, new NullPackageExtractor(), new NullRenderAlertSink(),
            new TestPackageSigningTrust(), new AdminInboxService(factory));
        return (svc, catalog);
    }
}
