using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;

namespace IdeaCms.Core.Services;

public interface IWorkflowService
{
    /// <summary>All workflow definitions.</summary>
    Task<IReadOnlyList<WorkflowDefinition>> GetDefinitionsAsync(CancellationToken ct = default);

    /// <summary>A single definition with its transitions loaded.</summary>
    Task<WorkflowDefinition?> GetDefinitionAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new workflow definition. If <paramref name="isDefault"/> is true the previous default (if
    /// any) is demoted — only one definition may be default at a time.
    /// </summary>
    Task<WorkflowDefinition> CreateDefinitionAsync(
        string name, string initialState, string description = "", bool isDefault = false, CancellationToken ct = default);

    /// <summary>Appends a transition rule to an existing workflow definition.</summary>
    Task<WorkflowTransitionDef> AddTransitionAsync(
        int definitionId, string fromState, string toState,
        string? requiredRole = null, string? label = null, CancellationToken ct = default);

    /// <summary>
    /// Transitions a page to <paramref name="toState"/> under its assigned (or default) workflow. Validates:
    /// <list type="bullet">
    ///   <item>The page exists and has a workflow assigned (or a site default exists).</item>
    ///   <item>The transition (<see cref="WorkflowTransitionDef"/>) is defined in the workflow.</item>
    ///   <item>The <paramref name="user"/> holds the required role (if any).</item>
    /// </list>
    /// On success, updates <see cref="Page.WorkflowState"/> and, when the target state is
    /// <c>"Published"</c>, also sets <see cref="Page.IsPublished"/> = true (and vice versa).
    /// </summary>
    Task<(bool Ok, string? Error)> TransitionPageAsync(
        int pageId, string toState, ClaimsPrincipal user, CancellationToken ct = default);

    /// <summary>Assigns a workflow definition to a page, setting its state to the definition's initial state.</summary>
    Task<bool> AssignWorkflowAsync(int pageId, int workflowDefinitionId, CancellationToken ct = default);
}

public sealed class WorkflowService(IDbContextFactory<CmsDbContext> dbFactory) : IWorkflowService
{
    public async Task<IReadOnlyList<WorkflowDefinition>> GetDefinitionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkflowDefinitions.AsNoTracking().Include(d => d.Transitions).ToListAsync(ct);
    }

    public async Task<WorkflowDefinition?> GetDefinitionAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkflowDefinitions.AsNoTracking().Include(d => d.Transitions).FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<WorkflowDefinition> CreateDefinitionAsync(
        string name, string initialState, string description = "", bool isDefault = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (isDefault)
        {
            // Demote ALL existing defaults (ToListAsync handles the edge case of pre-existing multiples).
            var priors = await db.WorkflowDefinitions.Where(d => d.IsDefault).ToListAsync(ct);
            foreach (var prior in priors) prior.IsDefault = false;
        }

        var def = new WorkflowDefinition
        {
            Name = name, InitialState = initialState, Description = description,
            IsDefault = isDefault, CreatedUtc = DateTime.UtcNow,
        };
        db.WorkflowDefinitions.Add(def);
        await db.SaveChangesAsync(ct);
        return def;
    }

    public async Task<WorkflowTransitionDef> AddTransitionAsync(
        int definitionId, string fromState, string toState,
        string? requiredRole = null, string? label = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.WorkflowDefinitions.AnyAsync(d => d.Id == definitionId, ct))
            throw new InvalidOperationException($"Workflow definition {definitionId} not found.");
        // Idempotent: if (definitionId, fromState, toState) already exists, return it without a duplicate row.
        var existing = await db.WorkflowTransitionDefs.FirstOrDefaultAsync(
            t => t.WorkflowDefinitionId == definitionId
                 && t.FromState == fromState
                 && t.ToState == toState, ct);
        if (existing is not null) return existing;
        var t = new WorkflowTransitionDef
        {
            WorkflowDefinitionId = definitionId,
            FromState = fromState, ToState = toState,
            RequiredRole = requiredRole, Label = label,
        };
        db.WorkflowTransitionDefs.Add(t);
        await db.SaveChangesAsync(ct);
        return t;
    }

    public async Task<(bool Ok, string? Error)> TransitionPageAsync(
        int pageId, string toState, ClaimsPrincipal user, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var page = await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null) return (false, "Page not found.");

        // Resolve the active workflow definition for this page.
        var defId = page.WorkflowDefinitionId
                    ?? await db.WorkflowDefinitions.Where(d => d.IsDefault).Select(d => (int?)d.Id).FirstOrDefaultAsync(ct);
        if (defId is null)
            return (false, "No workflow is assigned to this page and no default workflow is configured.");

        var def = await db.WorkflowDefinitions.Include(d => d.Transitions)
            .FirstOrDefaultAsync(d => d.Id == defId, ct);
        if (def is null) return (false, "Workflow definition not found.");

        // WorkflowState may be null on a published page (e.g. cleared via a direct save). Derive the
        // effective from-state from IsPublished so "Published → Draft" transitions still work.
        var fromState = page.WorkflowState
            ?? (page.IsPublished ? "Published" : def.InitialState);
        var transition = def.Transitions.FirstOrDefault(t =>
            string.Equals(t.FromState, fromState, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.ToState, toState, StringComparison.OrdinalIgnoreCase));

        if (transition is null)
            return (false, $"Transition from '{fromState}' to '{toState}' is not defined in workflow '{def.Name}'.");

        // Role gate.
        if (transition.RequiredRole is { Length: > 0 } role)
        {
            // Admins always pass.
            var cmsUserRoles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!cmsUserRoles.Contains(MaRoles.Admin) && !cmsUserRoles.Contains(role))
                return (false, $"Transition to '{toState}' requires the '{role}' role.");
        }

        page.WorkflowState = toState;
        // Published/unpublished sync: entering "Published" publishes; leaving it unpublishes.
        // Transitions between non-publishing states leave IsPublished unchanged.
        if (string.Equals(toState, "Published", StringComparison.OrdinalIgnoreCase))
            page.IsPublished = true;
        else if (string.Equals(fromState, "Published", StringComparison.OrdinalIgnoreCase))
            page.IsPublished = false;
        page.ModifiedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<bool> AssignWorkflowAsync(int pageId, int workflowDefinitionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var def = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == workflowDefinitionId, ct);
        if (def is null) return false;

        var page = await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null) return false;

        page.WorkflowDefinitionId = workflowDefinitionId;
        page.WorkflowState = def.InitialState;
        // Published/unpublished sync: derive IsPublished from InitialState in both directions so
        // a "Published" initial state also publishes the page (not just non-Published unpublishes).
        page.IsPublished = string.Equals(def.InitialState, "Published", StringComparison.OrdinalIgnoreCase);
        page.ModifiedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return true;
    }
}
