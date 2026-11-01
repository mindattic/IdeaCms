using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests;

/// <summary>
/// PageAdminService — the security write. Trust is stamped from the author principal (Author iff the
/// Cms.AuthorRawMarkup claim is present, else Untrusted); duplicate slugs return a friendly error (not an
/// exception); flags toggle; soft-deleted pages drop out of the list.
/// </summary>
[TestFixture]
public class PageAdminServiceTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
    }

    private static ClaimsPrincipal Author(bool withClaim, string uid = "user-1")
    {
        var claims = new List<Claim> { new("ma:uid", uid) };
        if (withClaim) claims.Add(new Claim(CmsClaims.AuthorRawMarkup, "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static async Task<PageAdminService> NewServiceAsync()
    {
        var factory = new InMemoryFactory("page_" + Guid.NewGuid().ToString("N"));
        await using (var db = factory.CreateDbContext())
        {
            db.Sites.Add(new Site { Key = "default", Name = "Default", IsDefault = true, CreatedUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        return new PageAdminService(factory);
    }

    private static async Task<(PageAdminService Svc, InMemoryFactory Factory)> NewServiceWithFactoryAsync()
    {
        var factory = new InMemoryFactory("page_" + Guid.NewGuid().ToString("N"));
        await using (var db = factory.CreateDbContext())
        {
            db.Sites.Add(new Site { Key = "default", Name = "Default", IsDefault = true, CreatedUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        return (new PageAdminService(factory), factory);
    }

    private static async Task<int> InsertSoftDeletedPageAsync(InMemoryFactory factory, string slug = "deleted-page")
    {
        await using var db = factory.CreateDbContext();
        var site = db.Sites.First();
        var page = new IdeaCms.Core.Entities.Page
        {
            SiteId = site.Id, Slug = slug, Title = "Deleted Page",
            Kind = PageKind.Data, BodyTrust = ContentTrust.Untrusted,
            IsPublished = true, Enabled = true,
            IsDeleted = true, DeletedUtc = DateTime.UtcNow,
            CreatedUtc = DateTime.UtcNow,
        };
        db.Pages.Add(page);
        await db.SaveChangesAsync();
        return page.Id;
    }

    [Test]
    public async Task Save_WithAuthorClaim_StampsAuthorTrust_AndCapturesAuthor()
    {
        var svc = await NewServiceAsync();
        var result = await svc.SaveAsync(new PageEditModel { Slug = "about", Title = "About", BodyHtml = "<p>hi</p>", IsPublished = true },
            Author(withClaim: true, uid: "admin-uid"));

        Assert.That(result.Ok, Is.True);
        Assert.That(result.StampedTrust, Is.EqualTo(ContentTrust.Author));
        var saved = await svc.GetAsync(result.Id);
        Assert.That(saved, Is.Not.Null);
        // Read the persisted trust via a fresh summary.
        var summary = (await svc.ListAsync()).Single(p => p.Id == result.Id);
        Assert.That(summary.BodyTrust, Is.EqualTo(ContentTrust.Author));
    }

    [Test]
    public async Task Save_WithoutClaim_StampsUntrusted()
    {
        var svc = await NewServiceAsync();
        var result = await svc.SaveAsync(new PageEditModel { Slug = "guest", Title = "Guest", BodyHtml = "<script>x()</script>" },
            Author(withClaim: false));
        Assert.That(result.Ok, Is.True);
        Assert.That(result.StampedTrust, Is.EqualTo(ContentTrust.Untrusted));
    }

    [Test]
    public async Task Save_DuplicateSlug_ReturnsFriendlyError_NotException()
    {
        var svc = await NewServiceAsync();
        var first = await svc.SaveAsync(new PageEditModel { Slug = "dup", Title = "One" }, Author(true));
        Assert.That(first.Ok, Is.True);

        var second = await svc.SaveAsync(new PageEditModel { Slug = "dup", Title = "Two" }, Author(true));
        Assert.That(second.Ok, Is.False);
        Assert.That(second.Error, Does.Contain("already exists"));
    }

    [Test]
    public async Task SetPublishedAndEnabled_FlipFlags()
    {
        var svc = await NewServiceAsync();
        var r = await svc.SaveAsync(new PageEditModel { Slug = "p", Title = "P", IsPublished = false, Enabled = true }, Author(true));

        Assert.That(await svc.SetPublishedAsync(r.Id, true), Is.True);
        Assert.That(await svc.SetEnabledAsync(r.Id, false), Is.True);
        var summary = (await svc.ListAsync()).Single(p => p.Id == r.Id);
        Assert.That(summary.IsPublished, Is.True);
        Assert.That(summary.Enabled, Is.False);
    }

    [Test]
    public async Task SoftDelete_RemovesFromList_ButDoesNotHardDelete()
    {
        var svc = await NewServiceAsync();
        var r = await svc.SaveAsync(new PageEditModel { Slug = "trash", Title = "Trash" }, Author(true));

        Assert.That(await svc.SoftDeleteAsync(r.Id), Is.True);
        Assert.That((await svc.ListAsync()).Any(p => p.Id == r.Id), Is.False);   // filtered by !IsDeleted
    }

    [Test]
    public async Task Move_NestsPageUnderParent()
    {
        var svc = await NewServiceAsync();
        var parent = await svc.SaveAsync(new PageEditModel { Slug = "docs", Title = "Docs" }, Author(true));
        var child = await svc.SaveAsync(new PageEditModel { Slug = "docs/intro", Title = "Intro" }, Author(true));

        Assert.That(await svc.MoveAsync(child.Id, parent.Id, sortOrder: 0), Is.True);
        var summary = (await svc.ListAsync()).Single(p => p.Id == child.Id);
        Assert.That(summary.ParentId, Is.EqualTo(parent.Id));
    }

    [Test]
    public async Task Move_RejectsCycle_AndSelfParent()
    {
        var svc = await NewServiceAsync();
        var a = await svc.SaveAsync(new PageEditModel { Slug = "a", Title = "A" }, Author(true));
        var b = await svc.SaveAsync(new PageEditModel { Slug = "b", Title = "B" }, Author(true));
        await svc.MoveAsync(b.Id, a.Id, 0);                    // b under a

        Assert.That(await svc.MoveAsync(a.Id, b.Id, 0), Is.False, "moving a under its own descendant b is a cycle");
        Assert.That(await svc.MoveAsync(a.Id, a.Id, 0), Is.False, "a page cannot be its own parent");
        Assert.That((await svc.ListAsync()).Single(p => p.Id == a.Id).ParentId, Is.Null, "a stays at top level");
    }

    [Test]
    public async Task Save_WithSeoFields_PersistsThroughGetAsync()
    {
        var svc = await NewServiceAsync();
        var result = await svc.SaveAsync(new PageEditModel
        {
            Slug = "seo-page", Title = "SEO Page",
            SeoTitle = "Custom SEO Title",
            SeoDescription = "This is the meta description.",
        }, Author(withClaim: true));

        Assert.That(result.Ok, Is.True);
        var loaded = await svc.GetAsync(result.Id);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.SeoTitle, Is.EqualTo("Custom SEO Title"));
        Assert.That(loaded.SeoDescription, Is.EqualTo("This is the meta description."));
    }

    [Test]
    public async Task SwapSortOrder_UpdatesBothPagesInOneCall()
    {
        // Regression: Nudge called MoveAsync twice in separate DbContexts; if the second call failed
        // the sort order was torn. SwapSortOrderAsync does both in one SaveChangesAsync.
        var svc = await NewServiceAsync();
        var a = await svc.SaveAsync(new PageEditModel { Slug = "page-a", Title = "A", SortOrder = 0 }, Author(true));
        var b = await svc.SaveAsync(new PageEditModel { Slug = "page-b", Title = "B", SortOrder = 1 }, Author(true));

        var ok = await svc.SwapSortOrderAsync(a.Id, 1, b.Id, 0);

        Assert.That(ok, Is.True);
        var list = await svc.ListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(list.Single(p => p.Id == a.Id).SortOrder, Is.EqualTo(1));
            Assert.That(list.Single(p => p.Id == b.Id).SortOrder, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task SwapSortOrder_MissingPage_ReturnsFalse()
    {
        var svc = await NewServiceAsync();
        var a = await svc.SaveAsync(new PageEditModel { Slug = "only-page", Title = "A" }, Author(true));
        var ok = await svc.SwapSortOrderAsync(a.Id, 1, 99999, 0);
        Assert.That(ok, Is.False);
    }

    [Test]
    public async Task Save_HomePageRename_RecordsSlugHistory()
    {
        // Regression: SaveAsync skipped slug-history when found.Slug was "" (home page),
        // so renaming "/" → "/about" left no redirect and caused a 404 on the old root URL.
        var dbName = "page_" + Guid.NewGuid().ToString("N");
        var factory = new InMemoryFactory(dbName);
        await using (var setup = factory.CreateDbContext())
        {
            setup.Sites.Add(new Site { Key = "default", Name = "Default", IsDefault = true, CreatedUtc = DateTime.UtcNow });
            await setup.SaveChangesAsync();
        }
        var pageSvc = new PageAdminService(factory);
        var slugSvc = new SlugRedirectService(factory);

        var r = await pageSvc.SaveAsync(new PageEditModel { Slug = "", Title = "Home" }, Author(true));
        Assert.That(r.Ok, Is.True, "create home page");

        var renamed = await pageSvc.SaveAsync(new PageEditModel { Id = r.Id, Slug = "about", Title = "Home" }, Author(true));
        Assert.That(renamed.Ok, Is.True, "rename succeeds");

        // A PageSlugHistory row for old slug "" must now exist so the router can redirect it.
        var history = await slugSvc.GetHistoryAsync(r.Id);
        Assert.That(history, Is.Not.Empty, "slug history must contain an entry for the old home slug");
        Assert.That(history.Any(h => h.OldSlug == ""), Is.True, "old home slug (empty string) must be recorded");
    }

    [Test]
    public async Task Save_CycleViaParentId_IsPreventedSilently()
    {
        // Regression: SaveAsync only guarded direct self-parent (A→A), not ancestor cycles (A→B when B is
        // under A). MoveAsync had the cycle guard; SaveAsync did not.
        var svc = await NewServiceAsync();
        var a = await svc.SaveAsync(new PageEditModel { Slug = "a", Title = "A" }, Author(true));
        var b = await svc.SaveAsync(new PageEditModel { Slug = "b", Title = "B" }, Author(true));

        // Establish A→B hierarchy via MoveAsync (which has the cycle guard).
        await svc.MoveAsync(b.Id, a.Id, sortOrder: 0);

        // Now try to set A's parent to B via SaveAsync — this would create A→B→A.
        var saved = await svc.SaveAsync(
            new PageEditModel { Id = a.Id, Slug = "a", Title = "A", ParentId = b.Id }, Author(true));

        Assert.That(saved.Ok, Is.True, "save succeeds but cycle is silently cleared");
        var summary = (await svc.ListAsync()).Single(p => p.Id == a.Id);
        Assert.That(summary.ParentId, Is.Null, "cycle is silently cleared to null");
    }

    [Test]
    public async Task Save_WithNullSeoFields_ReturnsNullOnLoad()
    {
        var svc = await NewServiceAsync();
        var result = await svc.SaveAsync(new PageEditModel
        {
            Slug = "no-seo", Title = "No SEO",
            SeoTitle = null,
            SeoDescription = null,
        }, Author(withClaim: true));

        Assert.That(result.Ok, Is.True);
        var loaded = await svc.GetAsync(result.Id);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.SeoTitle, Is.Null);
        Assert.That(loaded.SeoDescription, Is.Null);
    }

    [Test]
    public async Task Save_WorkflowStateDraft_OverridesIsPublishedTrue()
    {
        // Regression: SaveAsync wrote WorkflowState and IsPublished independently, so a form submission
        // with WorkflowState="Draft" and IsPublished=true persisted both as-is, violating the invariant
        // WorkflowState=="Published" ↔ IsPublished. Now WorkflowState drives IsPublished when non-null.
        var svc = await NewServiceAsync();
        var created = await svc.SaveAsync(
            new PageEditModel { Slug = "wf-test", Title = "WF", IsPublished = false }, Author(true));

        var updated = await svc.SaveAsync(new PageEditModel
        {
            Id = created.Id, Slug = "wf-test", Title = "WF",
            WorkflowState = "Draft",
            IsPublished = true,   // contradicts WorkflowState — the service must ignore this
        }, Author(true));

        Assert.That(updated.Ok, Is.True);
        var loaded = await svc.GetAsync(created.Id);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.WorkflowState, Is.EqualTo("Draft"));
            Assert.That(loaded.IsPublished, Is.False, "WorkflowState=Draft must override IsPublished to false");
        });
    }

    [Test]
    public async Task SetPublished_True_SyncsWorkflowStateToPublished()
    {
        // Regression: SetPublishedAsync called FlagAsync(p => p.IsPublished = published) which did not
        // touch WorkflowState, leaving it as e.g. "Draft" even after the admin toggled the page live.
        var svc = await NewServiceAsync();
        var created = await svc.SaveAsync(new PageEditModel
        {
            Slug = "pub-sync", Title = "Pub Sync", WorkflowState = "Draft", IsPublished = false,
        }, Author(true));

        var ok = await svc.SetPublishedAsync(created.Id, true);

        Assert.That(ok, Is.True);
        var loaded = await svc.GetAsync(created.Id);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.IsPublished, Is.True);
            Assert.That(loaded.WorkflowState, Is.EqualTo("Published"),
                "SetPublishedAsync(true) must set WorkflowState to \"Published\"");
        });
    }

    [Test]
    public async Task SetPublished_False_ClearsWorkflowStateWhenWasPublished()
    {
        // Counterpart: SetPublishedAsync(false) must clear WorkflowState when it was "Published", so the
        // invariant WorkflowState=="Published" ↔ IsPublished is maintained.
        var svc = await NewServiceAsync();
        var created = await svc.SaveAsync(new PageEditModel
        {
            Slug = "unpub-sync", Title = "Unpub Sync", WorkflowState = "Published", IsPublished = true,
        }, Author(true));

        var ok = await svc.SetPublishedAsync(created.Id, false);

        Assert.That(ok, Is.True);
        var loaded = await svc.GetAsync(created.Id);
        Assert.Multiple(() =>
        {
            Assert.That(loaded!.IsPublished, Is.False);
            Assert.That(loaded.WorkflowState, Is.Null,
                "SetPublishedAsync(false) must clear WorkflowState when it was \"Published\"");
        });
    }

    [Test]
    public async Task GetAsync_SoftDeletedPage_ReturnsModel()
    {
        // Regression: GetAsync used the default EF filter (!IsDeleted), so an admin who navigated to a
        // soft-deleted page's edit URL received a null model and could not inspect or recover the page.
        // IgnoreQueryFilters() allows the admin layer to always load any page by id.
        var (svc, factory) = await NewServiceWithFactoryAsync();
        var id = await InsertSoftDeletedPageAsync(factory, "gone-page");

        var model = await svc.GetAsync(id);

        Assert.That(model, Is.Not.Null, "GetAsync must return the model even for a soft-deleted page");
        Assert.That(model!.Slug, Is.EqualTo("gone-page"));
    }

    [Test]
    public async Task SetEnabled_SoftDeletedPage_Succeeds()
    {
        // Regression: FlagAsync used the default EF filter; calling SetEnabledAsync on a soft-deleted page
        // returned false (not found) instead of succeeding, making it impossible to re-enable a page before
        // undeleting it. IgnoreQueryFilters() fixes the lookup.
        var (svc, factory) = await NewServiceWithFactoryAsync();
        var id = await InsertSoftDeletedPageAsync(factory, "disabled-deleted");

        var ok = await svc.SetEnabledAsync(id, false);

        Assert.That(ok, Is.True, "SetEnabledAsync must succeed on a soft-deleted page");
    }

    [Test]
    public async Task SetPublished_SoftDeletedPage_Succeeds()
    {
        // Regression: SetPublishedAsync used the default EF filter; calling it on a soft-deleted page
        // returned false (not found). IgnoreQueryFilters() allows the admin to update any page by id.
        var (svc, factory) = await NewServiceWithFactoryAsync();
        var id = await InsertSoftDeletedPageAsync(factory, "pub-deleted");

        var ok = await svc.SetPublishedAsync(id, false);

        Assert.That(ok, Is.True, "SetPublishedAsync must succeed on a soft-deleted page");
    }

    [Test]
    public async Task Move_SoftDeletedPage_Succeeds()
    {
        // Regression: MoveAsync used the default EF filter for the target page lookup (the cycle-guard walk
        // already used IgnoreQueryFilters but the initial page fetch did not). Moving a soft-deleted page —
        // e.g. to re-parent it before restoring — silently returned false. IgnoreQueryFilters() fixes it.
        var (svc, factory) = await NewServiceWithFactoryAsync();
        var parent = await svc.SaveAsync(new PageEditModel { Slug = "parent", Title = "Parent" }, Author(true));
        var id = await InsertSoftDeletedPageAsync(factory, "move-deleted");

        var ok = await svc.MoveAsync(id, parent.Id, sortOrder: 5);

        Assert.That(ok, Is.True, "MoveAsync must succeed on a soft-deleted page");
    }

    [Test]
    public async Task SaveAsync_SoftDeletedPage_UpdatesBodySuccessfully()
    {
        // Regression: SaveAsync used the default EF filter on the found-page lookup (line 145), so editing
        // a soft-deleted page returned "Page not found." — an admin could view but not save to it.
        // IgnoreQueryFilters() allows the admin layer to save any page by id regardless of IsDeleted.
        var (svc, factory) = await NewServiceWithFactoryAsync();
        var id = await InsertSoftDeletedPageAsync(factory, "edit-deleted");

        var result = await svc.SaveAsync(new PageEditModel
        {
            Id = id, Slug = "edit-deleted", Title = "Updated Title",
            BodyHtml = "<p>updated body</p>",
        }, Author(withClaim: true));

        Assert.That(result.Ok, Is.True, "SaveAsync must succeed for a soft-deleted page");
        await using var verify = factory.CreateDbContext();
        var row = await verify.Pages.IgnoreQueryFilters().SingleAsync(p => p.Id == id);
        Assert.That(row.Title, Is.EqualTo("Updated Title"));
    }

    [Test]
    public async Task SaveAsync_SlugRename_HistorySlugIsStoredLowercase()
    {
        // Regression: SaveAsync stored found.Slug verbatim (e.g. "About-Us") in PageSlugHistory. The
        // duplicate-check compared h.OldSlug == found.Slug, which would miss a history row that was stored
        // lowercase — creating duplicate history entries and a mixed-case redirect target.
        // Now both the dedup check and the stored OldSlug use found.Slug.ToLowerInvariant().
        var (svc, factory) = await NewServiceWithFactoryAsync();
        await using (var setup = factory.CreateDbContext())
        {
            var site = setup.Sites.First();
            // Insert a page whose stored slug has mixed case (simulates a legacy row).
            setup.Pages.Add(new IdeaCms.Core.Entities.Page
            {
                SiteId = site.Id, Slug = "About-Us", Title = "About",
                Kind = PageKind.Data, BodyTrust = ContentTrust.Untrusted,
                Enabled = true, IsPublished = true, CreatedUtc = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }
        await using var lookupDb = factory.CreateDbContext();
        var page = await lookupDb.Pages.IgnoreQueryFilters().SingleAsync(p => p.Slug == "About-Us");

        // Rename the page to a new slug — triggers slug history recording.
        var result = await svc.SaveAsync(new PageEditModel
        {
            Id = page.Id, Slug = "about-us-new", Title = "About",
        }, Author(withClaim: true));

        Assert.That(result.Ok, Is.True, "rename must succeed");
        await using var verify = factory.CreateDbContext();
        var history = await verify.PageSlugHistory.ToListAsync();
        Assert.That(history, Has.Count.EqualTo(1), "exactly one history entry expected");
        Assert.That(history[0].OldSlug, Is.EqualTo("about-us"), "history slug must be normalized to lowercase");
    }

    [Test]
    public async Task SaveAsync_SoftDeletedRestrictedPage_SetsAclEntryInDb()
    {
        // Regression: the ACL rollback path at SaveAsync line 253 used the default EF filter to look up
        // the page by Id — a soft-deleted page was invisible, so the rollback silently failed and left
        // the page restricted with no ACL entries. IgnoreQueryFilters() ensures the rollback finds it.
        // This test verifies the happy path: a soft-deleted page with IsRestricted=true successfully
        // saves its ACL entries, proving the IgnoreQueryFilters fix in the rollback path is consistent.
        var (svc, factory) = await NewServiceWithFactoryAsync();
        var id = await InsertSoftDeletedPageAsync(factory, "restricted-deleted");

        var result = await svc.SaveAsync(new PageEditModel
        {
            Id = id, Slug = "restricted-deleted", Title = "Restricted",
            IsRestricted = true,
            AllowedRoles = ["Editor"],
        }, Author(withClaim: true));

        Assert.That(result.Ok, Is.True, "SaveAsync must succeed for a soft-deleted restricted page");
        await using var verify = factory.CreateDbContext();
        var acl = await verify.PageRoleAccess.Where(r => r.PageId == id).ToListAsync();
        Assert.That(acl, Has.Count.EqualTo(1), "PageRoleAccess entry must be persisted");
        Assert.That(acl[0].RoleName, Is.EqualTo("Editor"));
    }

    // ---- multi-site: an existing page keeps its OWN site (M.IDEA-§4.10 host-bound sites) ----

    /// <summary>Adds a second, non-default site and returns its id.</summary>
    private static async Task<int> AddSecondSiteAsync(InMemoryFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var site = new Site { Key = "other", Name = "Other", IsDefault = false, CreatedUtc = DateTime.UtcNow };
        db.Sites.Add(site);
        await db.SaveChangesAsync();
        return site.Id;
    }

    private static async Task<int> InsertPageAsync(InMemoryFactory factory, int siteId, string slug)
    {
        await using var db = factory.CreateDbContext();
        var page = new IdeaCms.Core.Entities.Page
        {
            SiteId = siteId, Slug = slug, Title = slug,
            Kind = PageKind.Data, BodyTrust = ContentTrust.Untrusted,
            IsPublished = true, Enabled = true, CreatedUtc = DateTime.UtcNow,
        };
        db.Pages.Add(page);
        await db.SaveChangesAsync();
        return page.Id;
    }

    [Test]
    public async Task Save_ExistingPageInNonDefaultSite_DetectsCollisionInItsOwnSite()
    {
        // Regression: SaveAsync derived the site from IsDefault for EVERY save, so editing a page that
        // lives in another site checked the DEFAULT site's slugs. A genuine collision inside the page's
        // own site slipped past the friendly pre-check and surfaced as "Save failed - please try again."
        var (svc, factory) = await NewServiceWithFactoryAsync();
        var otherSiteId = await AddSecondSiteAsync(factory);
        await InsertPageAsync(factory, otherSiteId, "taken");
        var editing = await InsertPageAsync(factory, otherSiteId, "movable");

        var result = await svc.SaveAsync(
            new PageEditModel { Id = editing, Slug = "taken", Title = "Movable" }, Author(withClaim: true));

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Does.Contain("already exists"),
            "the collision inside the page's own site must produce the friendly error");
    }

    [Test]
    public async Task Save_ExistingPageInNonDefaultSite_IgnoresSameSlugInTheDefaultSite()
    {
        // The mirror image: a slug that is taken in the DEFAULT site says nothing about a page living in
        // another site, so the save must go through (the unique index is on (SiteId, Slug)).
        var (svc, factory) = await NewServiceWithFactoryAsync();
        var otherSiteId = await AddSecondSiteAsync(factory);
        await using (var db = factory.CreateDbContext())
        {
            var defaultSiteId = db.Sites.First(s => s.IsDefault).Id;
            await InsertPageAsync(factory, defaultSiteId, "shared-slug");
        }
        var editing = await InsertPageAsync(factory, otherSiteId, "temp");

        var result = await svc.SaveAsync(
            new PageEditModel { Id = editing, Slug = "shared-slug", Title = "Shared" }, Author(withClaim: true));

        Assert.That(result.Ok, Is.True, result.Error);
        await using var verify = factory.CreateDbContext();
        var saved = await verify.Pages.IgnoreQueryFilters().FirstAsync(p => p.Id == editing);
        Assert.Multiple(() =>
        {
            Assert.That(saved.Slug, Is.EqualTo("shared-slug"));
            Assert.That(saved.SiteId, Is.EqualTo(otherSiteId), "the page must stay in its own site");
        });
    }

    [Test]
    public async Task Save_UnknownId_ReturnsPageNotFound()
    {
        var svc = await NewServiceAsync();
        var result = await svc.SaveAsync(new PageEditModel { Id = 4242, Slug = "ghost", Title = "Ghost" },
            Author(withClaim: true));

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Does.Contain("not found"));
    }
}
