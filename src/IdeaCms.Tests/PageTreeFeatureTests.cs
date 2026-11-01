using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Rendering;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Tests;

/// <summary>
/// PageTreeFeature — IPageTree host impl used by the TableOfContents widget (and any nav citizen) to
/// list a page's published+enabled children without a compile-time host reference. Covers: ordered
/// results, disabled/deleted filtering, and the unknown-page empty-return guarantee.
/// </summary>
[TestFixture]
public class PageTreeFeatureTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
    }

    private static async Task<(PageTreeFeature Feature, Guid ParentUid)> SeedAsync()
    {
        var factory = new InMemoryFactory("tree_" + Guid.NewGuid().ToString("N"));
        await using var db = factory.CreateDbContext();

        var site = new Site { Key = "s", Name = "S", IsDefault = true, CreatedUtc = DateTime.UtcNow };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        var parent = new CmsPage
        {
            SiteId = site.Id, Slug = "parent", Title = "Parent",
            Kind = PageKind.Data, IsPublished = true, Enabled = true,
            CreatedUtc = DateTime.UtcNow,
        };
        db.Pages.Add(parent);
        await db.SaveChangesAsync();

        // child-b SortOrder=0 → appears first; child-a SortOrder=1 → appears second
        db.Pages.Add(new CmsPage { SiteId = site.Id, ParentId = parent.Id, Slug = "child-b", Title = "Child B", Kind = PageKind.Data, IsPublished = true, Enabled = true, SortOrder = 0, CreatedUtc = DateTime.UtcNow });
        db.Pages.Add(new CmsPage { SiteId = site.Id, ParentId = parent.Id, Slug = "child-a", Title = "Child A", Kind = PageKind.Data, IsPublished = true, Enabled = true, SortOrder = 1, CreatedUtc = DateTime.UtcNow });
        // disabled child — excluded because Enabled=false
        db.Pages.Add(new CmsPage { SiteId = site.Id, ParentId = parent.Id, Slug = "disabled", Title = "Disabled", Kind = PageKind.Data, IsPublished = true, Enabled = false, SortOrder = 2, CreatedUtc = DateTime.UtcNow });
        // soft-deleted child — excluded because IsDeleted=true
        db.Pages.Add(new CmsPage { SiteId = site.Id, ParentId = parent.Id, Slug = "deleted", Title = "Deleted", Kind = PageKind.Data, IsPublished = true, Enabled = true, IsDeleted = true, SortOrder = 3, CreatedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        return (new PageTreeFeature(factory), parent.Uid);
    }

    [Test]
    public async Task ChildrenOfAsync_ReturnsPublishedEnabled_OrderedBySortOrder()
    {
        var (feature, parentUid) = await SeedAsync();
        var children = await feature.ChildrenOfAsync(parentUid);
        Assert.That(children.Select(c => c.Slug), Is.EqualTo(new[] { "child-b", "child-a" }));
    }

    [Test]
    public async Task ChildrenOfAsync_ExcludesDisabledAndDeletedChildren()
    {
        var (feature, parentUid) = await SeedAsync();
        var children = await feature.ChildrenOfAsync(parentUid);
        Assert.That(children.Any(c => c.Slug == "disabled"), Is.False);
        Assert.That(children.Any(c => c.Slug == "deleted"), Is.False);
    }

    [Test]
    public async Task ChildrenOfAsync_ReturnsEmpty_ForUnknownPageId()
    {
        var factory = new InMemoryFactory("tree_unknown_" + Guid.NewGuid().ToString("N"));
        var feature = new PageTreeFeature(factory);
        var children = await feature.ChildrenOfAsync(Guid.NewGuid());
        Assert.That(children, Is.Empty);
    }

    [Test]
    public async Task ChildrenOf_PopulatesPageId_SoMetadataCanBeJoined()
    {
        // ProjectGrid joins each child to IComponentMetadataStore by this id; a default Guid would make
        // every card silently lose its metadata.
        var (feature, parentUid) = await SeedAsync();

        var children = await feature.ChildrenOfAsync(parentUid);

        Assert.Multiple(() =>
        {
            Assert.That(children, Is.Not.Empty);
            Assert.That(children.Select(c => c.PageId), Has.All.Not.EqualTo(Guid.Empty));
            Assert.That(children.Select(c => c.PageId).Distinct().Count(), Is.EqualTo(children.Count),
                "each child must carry its own id");
        });
    }

    [Test]
    public async Task ChildrenOfSlug_ResolvesTheSamePageAsChildrenOfUid()
    {
        // A home page lists the projects index's children by SLUG, because a slug is what an author can
        // type into a tag attribute. It must agree with the uid-based lookup.
        var (feature, parentUid) = await SeedAsync();

        var byUid = await feature.ChildrenOfAsync(parentUid);
        var bySlug = await feature.ChildrenOfSlugAsync("parent");

        Assert.That(bySlug.Select(c => c.Slug), Is.EqualTo(byUid.Select(c => c.Slug)));
    }

    [Test]
    public async Task ChildrenOfSlug_ToleratesSurroundingSlashes()
    {
        var (feature, _) = await SeedAsync();

        Assert.That(await feature.ChildrenOfSlugAsync("/parent/"), Is.Not.Empty);
    }

    [Test]
    public async Task ChildrenOfSlug_UnknownSlug_ReturnsEmpty()
    {
        // A mistyped From="" attribute must render an empty grid, never throw into the render.
        var (feature, _) = await SeedAsync();

        Assert.That(await feature.ChildrenOfSlugAsync("no-such-page"), Is.Empty);
    }

    // ---- multi-site: a slug is unique only within a site (M.IDEA-§4.10 host-bound sites) ----

    /// <summary>
    /// Two sites, each with a page at the SAME slug and its own children. Returns the feature plus both
    /// site uids so a test can ask for one site's tree and prove it did not get the other's.
    /// </summary>
    private static async Task<(PageTreeFeature Feature, Guid SiteAUid, Guid SiteBUid)> SeedTwoSitesAsync()
    {
        var factory = new InMemoryFactory("tree_" + Guid.NewGuid().ToString("N"));
        await using var db = factory.CreateDbContext();

        var siteA = new Site { Key = "a", Name = "A", IsDefault = true, CreatedUtc = DateTime.UtcNow };
        var siteB = new Site { Key = "b", Name = "B", IsDefault = false, CreatedUtc = DateTime.UtcNow };
        db.Sites.AddRange(siteA, siteB);
        await db.SaveChangesAsync();

        CmsPage Parent(int siteId) => new()
        {
            SiteId = siteId, Slug = "projects", Title = "Projects",
            Kind = PageKind.Data, IsPublished = true, Enabled = true, CreatedUtc = DateTime.UtcNow,
        };
        var parentA = Parent(siteA.Id);
        var parentB = Parent(siteB.Id);
        db.Pages.AddRange(parentA, parentB);
        await db.SaveChangesAsync();

        db.Pages.Add(new CmsPage { SiteId = siteA.Id, ParentId = parentA.Id, Slug = "a-one", Title = "A One", Kind = PageKind.Data, IsPublished = true, Enabled = true, CreatedUtc = DateTime.UtcNow });
        db.Pages.Add(new CmsPage { SiteId = siteB.Id, ParentId = parentB.Id, Slug = "b-one", Title = "B One", Kind = PageKind.Data, IsPublished = true, Enabled = true, CreatedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        return (new PageTreeFeature(factory), siteA.Uid, siteB.Uid);
    }

    [Test]
    public async Task ChildrenOfSlug_ScopedToSite_ReturnsOnlyThatSitesChildren()
    {
        // Regression: the lookup matched on Slug alone, but the Pages unique index is (SiteId, Slug) —
        // so <Component.ProjectGrid From="projects" /> on site B could list site A's pages, picked by
        // nothing more principled than row order.
        var (feature, siteAUid, siteBUid) = await SeedTwoSitesAsync();

        var fromA = await feature.ChildrenOfSlugAsync(siteAUid, "projects");
        var fromB = await feature.ChildrenOfSlugAsync(siteBUid, "projects");

        Assert.Multiple(() =>
        {
            Assert.That(fromA.Select(c => c.Slug), Is.EqualTo(new[] { "a-one" }));
            Assert.That(fromB.Select(c => c.Slug), Is.EqualTo(new[] { "b-one" }));
        });
    }

    [Test]
    public async Task ChildrenOfSlug_UnknownSite_FallsBackToTheUnscopedLookup()
    {
        // Guid.Empty means "site unknown" (an ISiteContext with no resolved site). Rather than blanking a
        // host's nav, it degrades to the pre-existing unscoped behaviour — now deterministic, so it is
        // always the lowest-id site's page that answers.
        var (feature, siteAUid, _) = await SeedTwoSitesAsync();

        var unscoped = await feature.ChildrenOfSlugAsync(Guid.Empty, "projects");
        var siteA = await feature.ChildrenOfSlugAsync(siteAUid, "projects");

        Assert.That(unscoped.Select(c => c.Slug), Is.EqualTo(siteA.Select(c => c.Slug)));
    }

    [Test]
    public async Task ChildrenOfSlug_SiteWithNoSuchSlug_ReturnsEmpty_NotAnotherSitesPage()
    {
        // Site B has no "solo" page; site A does. Scoped to B the answer is empty — never A's children.
        var (feature, siteAUid, siteBUid) = await SeedTwoSitesAsync();

        var onlyInA = await feature.ChildrenOfSlugAsync(siteAUid, "projects");
        Assert.That(onlyInA, Is.Not.Empty, "guard: site A really does have this page");

        var inB = await feature.ChildrenOfSlugAsync(siteBUid, "no-such-page");
        Assert.That(inB, Is.Empty);
    }

    [Test]
    public void IPageTree_DefaultOverload_DelegatesToTheSlugOnlyForm()
    {
        // The SDK is append-only (M.IDEA-LAW-2): the new overload ships as a DEFAULT method, so a host that
        // implements only the slug-only form still answers rather than breaking.
        IPageTree legacy = new LegacySlugOnlyTree();

        var viaOverload = legacy.ChildrenOfSlugAsync(Guid.NewGuid(), "anything").GetAwaiter().GetResult();

        Assert.That(viaOverload.Select(c => c.Slug), Is.EqualTo(new[] { "legacy" }));
    }

    /// <summary>A host predating the site-scoped overload: implements only the slug-only form.</summary>
    private sealed class LegacySlugOnlyTree : IPageTree
    {
        public Task<IReadOnlyList<ChildPage>> ChildrenOfAsync(Guid pageId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChildPage>>(Array.Empty<ChildPage>());

        public Task<IReadOnlyList<ChildPage>> ChildrenOfSlugAsync(string slug, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChildPage>>([new ChildPage("legacy", "Legacy")]);
    }
}
