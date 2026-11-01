using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Tests;

[TestFixture]
public class WorkflowServiceTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<CmsDbContext>
    {
        private readonly DbContextOptions<CmsDbContext> _opts =
            new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(dbName).Options;
        public CmsDbContext CreateDbContext() => new(_opts);
        public Task<CmsDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }

    private static IDbContextFactory<CmsDbContext> NewFactory() =>
        new InMemoryFactory("wf_" + Guid.NewGuid().ToString("N"));

    private static ClaimsPrincipal Admin() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test"));

    private static ClaimsPrincipal Editor() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Editor")], "test"));

    private static ClaimsPrincipal Viewer() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Viewer")], "test"));

    private static async Task<(WorkflowService Svc, int DefId, int PageId)> SetupEditorialWorkflowAsync(
        IDbContextFactory<CmsDbContext> factory)
    {
        var svc = new WorkflowService(factory);

        var def = await svc.CreateDefinitionAsync("Editorial", "Draft", isDefault: true);
        await svc.AddTransitionAsync(def.Id, "Draft", "Review", requiredRole: "Editor", label: "Submit for Review");
        await svc.AddTransitionAsync(def.Id, "Review", "Published", requiredRole: "Admin", label: "Publish");
        await svc.AddTransitionAsync(def.Id, "Published", "Draft", requiredRole: "Admin", label: "Revert to Draft");

        await using var db = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "test-page", Title = "Test", Kind = IdeaCms.Abstractions.PageKind.Data,
            BodyTrust = IdeaCms.Abstractions.ContentTrust.Untrusted,
            WorkflowDefinitionId = def.Id, WorkflowState = "Draft",
            IsPublished = false, Enabled = true, CreatedUtc = DateTime.UtcNow,
        };
        db.Pages.Add(page);
        await db.SaveChangesAsync();

        return (svc, def.Id, page.Id);
    }

    [Test]
    public async Task CreateDefinition_Persists_WithInitialStateAndTransitions()
    {
        var factory = NewFactory();
        var svc = new WorkflowService(factory);

        var def = await svc.CreateDefinitionAsync("Default", "Draft");
        await svc.AddTransitionAsync(def.Id, "Draft", "Published", label: "Publish");

        var loaded = await svc.GetDefinitionAsync(def.Id);
        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Name, Is.EqualTo("Default"));
            Assert.That(loaded.InitialState, Is.EqualTo("Draft"));
            Assert.That(loaded.Transitions, Has.Count.EqualTo(1));
            Assert.That(loaded.Transitions.First().ToState, Is.EqualTo("Published"));
        });
    }

    [Test]
    public async Task CreateDefinition_IsDefault_DemotesPreviousDefault()
    {
        var factory = NewFactory();
        var svc = new WorkflowService(factory);

        var first = await svc.CreateDefinitionAsync("First", "Draft", isDefault: true);
        var second = await svc.CreateDefinitionAsync("Second", "Draft", isDefault: true);

        var all = await svc.GetDefinitionsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(all.First(d => d.Id == first.Id).IsDefault, Is.False, "first was demoted");
            Assert.That(all.First(d => d.Id == second.Id).IsDefault, Is.True, "second is default");
        });
    }

    [Test]
    public async Task TransitionPage_ValidTransition_ChangesWorkflowState()
    {
        var factory = NewFactory();
        var (svc, _, pageId) = await SetupEditorialWorkflowAsync(factory);

        var (ok, err) = await svc.TransitionPageAsync(pageId, "Review", Editor());

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True, err);
            Assert.That(err, Is.Null);
        });

        await using var db = factory.CreateDbContext();
        var page = await db.Pages.FindAsync(pageId);
        Assert.That(page!.WorkflowState, Is.EqualTo("Review"));
    }

    [Test]
    public async Task TransitionPage_ToPublished_SetsIsPublishedTrue()
    {
        var factory = NewFactory();
        var (svc, _, pageId) = await SetupEditorialWorkflowAsync(factory);

        await svc.TransitionPageAsync(pageId, "Review", Editor());
        var (ok, _) = await svc.TransitionPageAsync(pageId, "Published", Admin());

        await using var db = factory.CreateDbContext();
        var page = await db.Pages.FindAsync(pageId);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(page!.IsPublished, Is.True, "Published state syncs IsPublished");
            Assert.That(page.WorkflowState, Is.EqualTo("Published"));
        });
    }

    [Test]
    public async Task TransitionPage_FromPublishedToDraft_SetsIsPublishedFalse()
    {
        var factory = NewFactory();
        var (svc, _, pageId) = await SetupEditorialWorkflowAsync(factory);

        // Get to Published first.
        await svc.TransitionPageAsync(pageId, "Review", Editor());
        await svc.TransitionPageAsync(pageId, "Published", Admin());

        // Revert.
        var (ok, _) = await svc.TransitionPageAsync(pageId, "Draft", Admin());

        await using var db = factory.CreateDbContext();
        var page = await db.Pages.FindAsync(pageId);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(page!.IsPublished, Is.False, "Draft state clears IsPublished");
        });
    }

    [Test]
    public async Task TransitionPage_MissingTransition_ReturnsError()
    {
        var factory = NewFactory();
        var (svc, _, pageId) = await SetupEditorialWorkflowAsync(factory);

        var (ok, err) = await svc.TransitionPageAsync(pageId, "Published", Editor());

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(err, Does.Contain("not defined in workflow"));
        });
    }

    [Test]
    public async Task TransitionPage_InsufficientRole_ReturnsError()
    {
        var factory = NewFactory();
        var (svc, _, pageId) = await SetupEditorialWorkflowAsync(factory);

        // Viewer cannot submit for Review (requires Editor).
        var (ok, err) = await svc.TransitionPageAsync(pageId, "Review", Viewer());

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(err, Does.Contain("requires the 'Editor' role"));
        });
    }

    [Test]
    public async Task TransitionPage_AdminBypassesRoleGate()
    {
        var factory = NewFactory();
        var (svc, _, pageId) = await SetupEditorialWorkflowAsync(factory);

        // Admin can do any transition even if RequiredRole is "Editor".
        var (ok, err) = await svc.TransitionPageAsync(pageId, "Review", Admin());
        Assert.That(ok, Is.True, err);
    }

    [Test]
    public async Task AssignWorkflow_SetsWorkflowAndInitialState()
    {
        var factory = NewFactory();
        var svc = new WorkflowService(factory);
        var def = await svc.CreateDefinitionAsync("Simple", "Draft");

        await using var setupDb = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "my-page", Title = "My Page",
            Kind = IdeaCms.Abstractions.PageKind.Data,
            BodyTrust = IdeaCms.Abstractions.ContentTrust.Untrusted,
            Enabled = true, CreatedUtc = DateTime.UtcNow,
        };
        setupDb.Pages.Add(page);
        await setupDb.SaveChangesAsync();

        var ok = await svc.AssignWorkflowAsync(page.Id, def.Id);

        await using var db = factory.CreateDbContext();
        var updated = await db.Pages.FindAsync(page.Id);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(updated!.WorkflowDefinitionId, Is.EqualTo(def.Id));
            Assert.That(updated.WorkflowState, Is.EqualTo("Draft"));
        });
    }

    [Test]
    public async Task AssignWorkflow_UnpublishesPageWhenInitialStateIsNotPublished()
    {
        // Regression: AssignWorkflowAsync reset WorkflowState but did not clear IsPublished,
        // leaving a published page live even though it was now in "Draft" initial state.
        var factory = NewFactory();
        var svc = new WorkflowService(factory);
        var def = await svc.CreateDefinitionAsync("Draft-first", "Draft");

        await using var setupDb = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "live-page", Title = "Live",
            Kind = IdeaCms.Abstractions.PageKind.Data,
            BodyTrust = IdeaCms.Abstractions.ContentTrust.Untrusted,
            IsPublished = true, Enabled = true, CreatedUtc = DateTime.UtcNow,
        };
        setupDb.Pages.Add(page);
        await setupDb.SaveChangesAsync();

        await svc.AssignWorkflowAsync(page.Id, def.Id);

        await using var db = factory.CreateDbContext();
        var updated = await db.Pages.FindAsync(page.Id);
        Assert.Multiple(() =>
        {
            Assert.That(updated!.WorkflowState, Is.EqualTo("Draft"));
            Assert.That(updated.IsPublished, Is.False, "non-Published initial state must unpublish the page");
        });
    }

    [Test]
    public async Task AddTransition_UnknownDefinitionId_ThrowsInvalidOperation()
    {
        // Regression: AddTransitionAsync silently inserted a dangling WorkflowTransitionDef row when
        // definitionId did not exist. On SQL Server this produced an FK violation; on InMemory it was
        // silently swallowed. Now both providers get an InvalidOperationException.
        var factory = NewFactory();
        var svc = new WorkflowService(factory);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.AddTransitionAsync(9999, "Draft", "Published"));
    }

    [Test]
    public async Task AssignWorkflow_PublishedInitialState_PublishesPage()
    {
        // Regression: AssignWorkflowAsync only cleared IsPublished for non-"Published" initial states;
        // it never SET IsPublished when InitialState WAS "Published", leaving an unpublished page unpublished
        // even after assignment to a workflow that starts in the published state.
        var factory = NewFactory();
        var svc = new WorkflowService(factory);
        var def = await svc.CreateDefinitionAsync("Publish-first", "Published");

        await using var setupDb = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "draft-page", Title = "Draft",
            Kind = IdeaCms.Abstractions.PageKind.Data,
            BodyTrust = IdeaCms.Abstractions.ContentTrust.Untrusted,
            IsPublished = false, Enabled = true, CreatedUtc = DateTime.UtcNow,
        };
        setupDb.Pages.Add(page);
        await setupDb.SaveChangesAsync();

        await svc.AssignWorkflowAsync(page.Id, def.Id);

        await using var db = factory.CreateDbContext();
        var updated = await db.Pages.FindAsync(page.Id);
        Assert.Multiple(() =>
        {
            Assert.That(updated!.WorkflowState, Is.EqualTo("Published"));
            Assert.That(updated.IsPublished, Is.True, "Published initial state must publish the page");
        });
    }

    [Test]
    public async Task TransitionPage_NullWorkflowStateOnPublishedPage_DerivesFromIsPublished()
    {
        // Regression: TransitionPageAsync used page.WorkflowState ?? def.InitialState as from-state, so a
        // page with WorkflowState=null and IsPublished=true derived "Draft" (the initial state) as from-state,
        // making it impossible to transition via "Published" → "Draft" on such a page.
        var factory = NewFactory();
        var svc = new WorkflowService(factory);

        var def = await svc.CreateDefinitionAsync("Test", "Draft", isDefault: true);
        await svc.AddTransitionAsync(def.Id, "Draft", "Published");
        await svc.AddTransitionAsync(def.Id, "Published", "Draft", label: "Revert");

        await using var setupDb = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "null-state-page", Title = "Null State",
            Kind = IdeaCms.Abstractions.PageKind.Data,
            BodyTrust = IdeaCms.Abstractions.ContentTrust.Untrusted,
            WorkflowDefinitionId = def.Id,
            WorkflowState = null,   // null state but page is live — cleared by a direct SaveAsync
            IsPublished = true, Enabled = true, CreatedUtc = DateTime.UtcNow,
        };
        setupDb.Pages.Add(page);
        await setupDb.SaveChangesAsync();

        var (ok, err) = await svc.TransitionPageAsync(page.Id, "Draft", Admin());

        Assert.That(ok, Is.True, err ?? "transition must succeed when from-state is derived from IsPublished");

        await using var db = factory.CreateDbContext();
        var updated = await db.Pages.FindAsync(page.Id);
        Assert.Multiple(() =>
        {
            Assert.That(updated!.WorkflowState, Is.EqualTo("Draft"));
            Assert.That(updated.IsPublished, Is.False, "transitioning away from Published must clear IsPublished");
        });
    }

    [Test]
    public async Task AssignWorkflow_ResetsStateEvenWhenPageAlreadyHasState()
    {
        // Regression: ??= left old WorkflowState intact when reassigning a different workflow.
        var factory = NewFactory();
        var svc = new WorkflowService(factory);

        var defA = await svc.CreateDefinitionAsync("DefA", "Draft");
        var defB = await svc.CreateDefinitionAsync("DefB", "Review");

        await using var setupDb = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "reassign-page", Title = "Reassign Test",
            Kind = IdeaCms.Abstractions.PageKind.Data,
            BodyTrust = IdeaCms.Abstractions.ContentTrust.Untrusted,
            Enabled = true, CreatedUtc = DateTime.UtcNow,
            WorkflowDefinitionId = defA.Id, WorkflowState = "Published",
        };
        setupDb.Pages.Add(page);
        await setupDb.SaveChangesAsync();

        await svc.AssignWorkflowAsync(page.Id, defB.Id);

        await using var db = factory.CreateDbContext();
        var updated = await db.Pages.FindAsync(page.Id);
        Assert.That(updated!.WorkflowState, Is.EqualTo("Review"),
            "AssignWorkflow must reset state to InitialState of the new definition");
    }

    [Test]
    public async Task TransitionPage_SoftDeletedPage_FindsPage_DoesNotReturnNotFound()
    {
        // Regression: TransitionPageAsync used the default EF filter and returned "Page not found" for
        // soft-deleted pages, even though an admin may need to manage workflow state during recovery.
        // IgnoreQueryFilters() ensures the admin layer can always resolve a page by id.
        var factory = NewFactory();
        var (svc, defId, _) = await SetupEditorialWorkflowAsync(factory);

        // Insert a soft-deleted page manually so it is invisible to the default filter.
        await using var setup = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "soft-deleted", Title = "Soft Deleted",
            Kind = IdeaCms.Abstractions.PageKind.Data,
            BodyTrust = IdeaCms.Abstractions.ContentTrust.Untrusted,
            WorkflowDefinitionId = defId, WorkflowState = "Draft",
            IsPublished = false, Enabled = true,
            IsDeleted = true, DeletedUtc = DateTime.UtcNow, CreatedUtc = DateTime.UtcNow,
        };
        setup.Pages.Add(page);
        await setup.SaveChangesAsync();

        var (ok, err) = await svc.TransitionPageAsync(page.Id, "Review", Editor());

        Assert.That(err, Is.Not.EqualTo("Page not found."),
            "soft-deleted page must be found; 'Page not found' is only correct for a truly absent id");
        Assert.That(ok, Is.True, err);
    }

    [Test]
    public async Task AddTransitionAsync_DuplicateStates_IsIdempotent_NoExtraRow()
    {
        // Regression: AddTransitionAsync had no dedup check on (WorkflowDefinitionId, FromState, ToState),
        // so calling it twice with the same states created two rows. TransitionPageAsync then used
        // FirstOrDefault on the loaded Transitions collection — the result was non-deterministic (DB
        // ordering) and the second row was orphaned, confusing admin UIs that list all transitions.
        // Now the method returns the existing row without inserting a duplicate.
        var factory = NewFactory();
        var svc = new WorkflowService(factory);
        var def = await svc.CreateDefinitionAsync("Dedup", "Draft");

        var first = await svc.AddTransitionAsync(def.Id, "Draft", "Published", requiredRole: "Editor");
        var second = await svc.AddTransitionAsync(def.Id, "Draft", "Published", requiredRole: "Admin");

        // The second call must return the existing row (same Id), not a new one.
        Assert.That(second.Id, Is.EqualTo(first.Id), "duplicate AddTransition must return the existing row");
        await using var db = factory.CreateDbContext();
        var count = await db.WorkflowTransitionDefs
            .CountAsync(t => t.WorkflowDefinitionId == def.Id && t.FromState == "Draft" && t.ToState == "Published");
        Assert.That(count, Is.EqualTo(1), "exactly one transition row must exist after two identical AddTransition calls");
    }

    [Test]
    public async Task AssignWorkflow_SoftDeletedPage_Succeeds()
    {
        // Regression: AssignWorkflowAsync used the default EF filter and returned false for soft-deleted
        // pages. IgnoreQueryFilters() ensures the admin can assign a workflow to any page by id.
        var factory = NewFactory();
        var svc = new WorkflowService(factory);
        var def = await svc.CreateDefinitionAsync("Simple", "Draft");

        await using var setup = factory.CreateDbContext();
        var page = new CmsPage
        {
            Slug = "del-wf-page", Title = "Del WF",
            Kind = IdeaCms.Abstractions.PageKind.Data,
            BodyTrust = IdeaCms.Abstractions.ContentTrust.Untrusted,
            Enabled = true, IsDeleted = true, DeletedUtc = DateTime.UtcNow, CreatedUtc = DateTime.UtcNow,
        };
        setup.Pages.Add(page);
        await setup.SaveChangesAsync();

        var ok = await svc.AssignWorkflowAsync(page.Id, def.Id);

        Assert.That(ok, Is.True, "AssignWorkflowAsync must succeed for a soft-deleted page");
    }
}
