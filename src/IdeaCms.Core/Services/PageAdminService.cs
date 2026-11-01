using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;

namespace IdeaCms.Core.Services;

public sealed record PageSummary(
    int Id, string Slug, string Title, PageKind Kind, bool IsPublished, bool Enabled, ContentTrust BodyTrust,
    int? ParentId, int SortOrder);

public sealed class PageEditModel
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ThemeKey { get; set; }
    public int? ThemeVersion { get; set; }
    public PageKind Kind { get; set; } = PageKind.Data;
    public string? BodyHtml { get; set; }
    public string? PageCss { get; set; }
    public string? PageJs { get; set; }
    public bool IsPublished { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Parent page in the nav tree; null = top level.</summary>
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }
    /// <summary>Overrides the browser <c>&lt;title&gt;</c> tag (SEO). Null = use Title.</summary>
    public string? SeoTitle { get; set; }
    /// <summary>Content for <c>&lt;meta name="description"&gt;</c>.</summary>
    public string? SeoDescription { get; set; }

    // ---- Access control ----
    /// <summary>False (default) = public. True = viewers must match at least one role or user entry.</summary>
    public bool IsRestricted { get; set; }
    /// <summary>Role names (auth or CMS) allowed to view when restricted.</summary>
    public List<string> AllowedRoles { get; set; } = [];
    /// <summary>Individual user IDs (ma:uid Guid strings) allowed to view when restricted.</summary>
    public List<string> AllowedUserIds { get; set; } = [];

    // ---- Plugins ----
    /// <summary>Plugin ref strings active for this page (e.g. "Plugin.tooltip", "Plugin.navmenu@1").</summary>
    public List<string> ActivePlugins { get; set; } = [];
    /// <summary>
    /// True = this page has NO selection of its own and inherits the site's default plugins
    /// (ActivePluginsJson null). False = <see cref="ActivePlugins"/> is the page's own list — which may be
    /// EMPTY, meaning "no plugins at all" (no header, breadcrumbs or footer).
    /// </summary>
    public bool InheritPlugins { get; set; } = true;
    /// <summary>The site's default plugins (read-only context for the editor; what an inheriting page renders).</summary>
    public List<string> SiteDefaultPlugins { get; set; } = [];

    /// <summary>When true, navigation links to this page open in a new browser tab/window.</summary>
    public bool OpenInNewWindow { get; set; }

    // ---- Workflow ----
    /// <summary>Assigned workflow definition; null = site default or no workflow.</summary>
    public int? WorkflowDefinitionId { get; set; }
    /// <summary>Current workflow state name; null = no workflow / governed by IsPublished only.</summary>
    public string? WorkflowState { get; set; }
}

public sealed record PageSaveResult(bool Ok, int Id, ContentTrust StampedTrust, string? Error);

/// <summary>
/// Admin Page CRUD by (SiteId, Slug). The security-critical write is <see cref="SaveAsync"/>: it stamps
/// BodyTrust from the author's principal via <see cref="PageAuthoring"/> (Author iff the
/// Cms.AuthorRawMarkup claim is present, else Untrusted) so inline JS only runs for trusted authors.
/// Slug uniqueness is pre-checked (friendly error) with a DbUpdateException backstop. Delete is SOFT
/// (IsDeleted) — rows are never hard-deleted, so authored history and references never dangle.
/// </summary>
public interface IPageAdminService
{
    Task<IReadOnlyList<PageSummary>> ListAsync(CancellationToken ct = default);
    Task<PageEditModel?> GetAsync(int id, CancellationToken ct = default);
    /// <summary>A blank model for a NEW page, carrying the site defaults it will inherit (it lands in the default site).</summary>
    Task<PageEditModel> NewAsync(CancellationToken ct = default);
    Task<PageSaveResult> SaveAsync(PageEditModel model, ClaimsPrincipal author, CancellationToken ct = default);
    Task<bool> SetPublishedAsync(int id, bool published, CancellationToken ct = default);
    Task<bool> SetEnabledAsync(int id, bool enabled, CancellationToken ct = default);
    Task<bool> SoftDeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Re-home a page in the nav tree: set its parent (null = top level) and sort order. Rejects a move
    /// that would make a page its own ancestor (cycle guard), so the tree can never become malformed.
    /// </summary>
    Task<bool> MoveAsync(int id, int? parentId, int sortOrder, CancellationToken ct = default);

    /// <summary>
    /// Atomically swap the SortOrder of two pages in a single transaction.
    /// Both pages must exist; returns false if either is not found.
    /// </summary>
    Task<bool> SwapSortOrderAsync(int idA, int sortA, int idB, int sortB, CancellationToken ct = default);
}

public sealed class PageAdminService(IDbContextFactory<CmsDbContext> dbFactory) : IPageAdminService
{
    public async Task<IReadOnlyList<PageSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Pages.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Slug)
            .Select(p => new PageSummary(p.Id, p.Slug, p.Title, p.Kind, p.IsPublished, p.Enabled, p.BodyTrust,
                p.ParentId, p.SortOrder))
            .ToListAsync(ct);
    }

    public async Task<PageEditModel?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.Pages.AsNoTracking().IgnoreQueryFilters()
            .Include(x => x.MetaTags).Include(x => x.RoleAccess).Include(x => x.UserAccess)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;
        var siteDefault = p.SiteId is int sid
            ? await db.Settings.AsNoTracking()
                .Where(s => s.Scope == "Site" && s.ScopeId == sid && s.Key == "plugins.default")
                .Select(s => s.Value).FirstOrDefaultAsync(ct)
            : null;
        return new PageEditModel
        {
            SiteDefaultPlugins   = DeserializePlugins(siteDefault),
            Id = p.Id, Slug = p.Slug, Title = p.Title, ThemeKey = p.ThemeKey, ThemeVersion = p.ThemeVersion,
            Kind = p.Kind, BodyHtml = p.BodyHtml, PageCss = p.PageCss, PageJs = p.PageJs,
            IsPublished = p.IsPublished, Enabled = p.Enabled, OpenInNewWindow = p.OpenInNewWindow,
            ParentId = p.ParentId, SortOrder = p.SortOrder,
            SeoTitle       = p.SeoTitle,
            SeoDescription = p.MetaTags.FirstOrDefault(t => t.Name == "seo.description")?.Content,
            IsRestricted   = p.IsRestricted,
            AllowedRoles   = p.RoleAccess.Select(r => r.RoleName).ToList(),
            AllowedUserIds = p.UserAccess.Select(u => u.UserId).ToList(),
            WorkflowDefinitionId = p.WorkflowDefinitionId,
            WorkflowState        = p.WorkflowState,
            ActivePlugins        = DeserializePlugins(p.ActivePluginsJson),
            InheritPlugins       = string.IsNullOrWhiteSpace(p.ActivePluginsJson),
        };
    }

    public async Task<PageEditModel> NewAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Same site rule as SaveAsync for a new page: the default site, else the first one.
        var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.IsDefault, ct)
                   ?? await db.Sites.AsNoTracking().FirstOrDefaultAsync(ct);
        var siteDefault = site is null ? null : await db.Settings.AsNoTracking()
            .Where(s => s.Scope == "Site" && s.ScopeId == site.Id && s.Key == "plugins.default")
            .Select(s => s.Value).FirstOrDefaultAsync(ct);
        return new PageEditModel
        {
            Kind = PageKind.Data, Enabled = true, InheritPlugins = true,
            SiteDefaultPlugins = DeserializePlugins(siteDefault),
        };
    }

    public async Task<PageSaveResult> SaveAsync(PageEditModel model, ClaimsPrincipal author, CancellationToken ct = default)
    {
        var slug = (model.Slug ?? "").Trim().Trim('/').ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // An EXISTING page keeps its OWN site; only a new page lands in the default one. Deriving the site
        // from IsDefault for every save pointed the (SiteId, Slug) uniqueness check — and the
        // DbUpdateException discriminator below — at the wrong site whenever the edited page belonged to
        // another one: a real collision in the page's own site slipped past the friendly pre-check and came
        // back as "Save failed — please try again.", while an unrelated slug in the default site produced a
        // spurious "already exists".
        var found = model.Id == 0
            ? null
            : await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == model.Id, ct);
        if (model.Id != 0 && found is null)
            return new PageSaveResult(false, model.Id, default, "Page not found.");

        int? siteId;
        if (found is not null)
        {
            siteId = found.SiteId;
        }
        else
        {
            var site = await db.Sites.FirstOrDefaultAsync(s => s.IsDefault, ct) ?? await db.Sites.FirstOrDefaultAsync(ct);
            siteId = site?.Id;
        }

        // Slug uniqueness pre-check (friendly error before the DB unique index fires).
        // IgnoreQueryFilters: the unique index covers ALL rows including soft-deleted, so the pre-check must too.
        if (await db.Pages.IgnoreQueryFilters().AnyAsync(p => p.SiteId == siteId && p.Slug == slug && p.Id != model.Id, ct))
            return new PageSaveResult(false, model.Id, default, $"A page with slug '{(slug.Length == 0 ? "(home)" : slug)}' already exists.");

        var (trust, authoredBy) = PageAuthoring.Stamp(author);
        var now = DateTime.UtcNow;

        Page page;
        if (found is null)
        {
            page = new Page { SiteId = siteId, CreatedUtc = now };
            db.Pages.Add(page);
        }
        else
        {
            // Auto-301: record the old slug before overwriting it, so the router can redirect stale links.
            // The empty-string guard is intentionally absent: renaming the home page (slug="") must also
            // produce a history entry so the old "/" can redirect to the new slug.
            if (!string.Equals(found.Slug, slug, StringComparison.OrdinalIgnoreCase))
            {
                var oldSlug = found.Slug.ToLowerInvariant();
                var alreadyRecorded = await db.PageSlugHistory.AnyAsync(
                    h => h.PageId == found.Id && h.OldSlug == oldSlug, ct);
                if (!alreadyRecorded)
                    db.PageSlugHistory.Add(new PageSlugHistory
                    {
                        PageId = found.Id, OldSlug = oldSlug,
                        IsVanity = false, AddedByUserId = null,
                        CreatedUtc = now,
                    });
            }

            page = found;
        }

        page.Slug = slug;
        page.Title = model.Title;
        page.SeoTitle = string.IsNullOrWhiteSpace(model.SeoTitle) ? null : model.SeoTitle.Trim();
        page.ThemeKey = string.IsNullOrWhiteSpace(model.ThemeKey) ? null : model.ThemeKey.Trim();
        page.ThemeVersion = model.ThemeVersion;
        page.Kind = model.Kind;
        page.BodyHtml = model.BodyHtml;
        // M.IDEA-§4.7: Untrusted PageCss is normalized at save time -- same-selector shorthand/longhand
        // conflicts within the author's OWN text are collapsed (CssConflictMerger never reads or
        // rewrites Theme/Component/Global CSS, only this one field). Author-trusted PageCss is stored
        // exactly as submitted, preserving the M.IDEA-LAW-5 byte-for-byte verbatim guarantee.
        page.PageCss = trust == ContentTrust.Author ? model.PageCss : CssConflictMerger.Normalize(model.PageCss);
        page.PageJs = model.PageJs;
        page.IsPublished = model.IsPublished;
        page.Enabled = model.Enabled;
        page.OpenInNewWindow = model.OpenInNewWindow;
        page.IsRestricted = model.IsRestricted;
        // Cycle guard: walk up from the proposed parent; if we reach model.Id the move would form a
        // cycle. Mirrors the same walk in MoveAsync. Existing pages only (model.Id == 0 = new page).
        var safeParentId = model.ParentId == model.Id ? null : model.ParentId;
        if (safeParentId.HasValue && model.Id != 0)
        {
            var cursorId = safeParentId;
            for (var hops = 0; cursorId is int cid && hops < 256; hops++)
            {
                if (cid == model.Id) { safeParentId = null; break; }
                cursorId = await db.Pages.IgnoreQueryFilters()
                    .Where(p => p.Id == cid).Select(p => p.ParentId).FirstOrDefaultAsync(ct);
            }
        }
        page.ParentId = safeParentId;
        page.SortOrder = model.SortOrder;
        page.BodyTrust = trust;                 // write-time trust stamp
        page.AuthoredByUserId = authoredBy;
        page.AuthorTrustVersion += 1;           // epoch bump
        page.ActivePluginsJson = model.InheritPlugins ? null : SerializePlugins(model.ActivePlugins);
        page.WorkflowDefinitionId = model.WorkflowDefinitionId;
        page.WorkflowState = model.WorkflowState;
        // Sync IsPublished from WorkflowState so the invariant WorkflowState=="Published" ↔ IsPublished
        // is maintained even when the UI model submits contradictory values (e.g. WorkflowState="Draft"
        // with IsPublished=true).  Null WorkflowState means "no workflow" and model.IsPublished governs.
        if (model.WorkflowState is not null)
            page.IsPublished = string.Equals(model.WorkflowState, "Published", StringComparison.OrdinalIgnoreCase);
        page.ModifiedUtc = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Discriminate: was this a slug collision or a concurrent slug-history duplicate insert?
            await using var check = await dbFactory.CreateDbContextAsync(ct);
            var slugConflict = await check.Pages.IgnoreQueryFilters()
                .AnyAsync(p => p.SiteId == siteId && p.Slug == slug && p.Id != model.Id, ct);
            return new PageSaveResult(false, page.Id, trust, slugConflict
                ? $"A page with slug '{(slug.Length == 0 ? "(home)" : slug)}' already exists."
                : "Save failed — please try again.");
        }

        // Upsert meta tags — RemoveRange (not ExecuteDeleteAsync) keeps InMemory provider compatible.
        var existingTags = await db.PageMetaTags.Where(t => t.PageId == page.Id).ToListAsync(ct);
        db.PageMetaTags.RemoveRange(existingTags);
        if (!string.IsNullOrWhiteSpace(model.SeoDescription))
            db.PageMetaTags.Add(new PageMetaTag { PageId = page.Id, Name = "seo.description", Content = model.SeoDescription.Trim() });

        // Upsert role access.
        var existingRoles = await db.PageRoleAccess.Where(r => r.PageId == page.Id).ToListAsync(ct);
        db.PageRoleAccess.RemoveRange(existingRoles);
        foreach (var role in (model.AllowedRoles ?? []).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct())
            db.PageRoleAccess.Add(new PageRoleAccess { PageId = page.Id, RoleName = role });

        // Upsert user access.
        var existingUsers = await db.PageUserAccess.Where(u => u.PageId == page.Id).ToListAsync(ct);
        db.PageUserAccess.RemoveRange(existingUsers);
        foreach (var uid in (model.AllowedUserIds ?? []).Select(u => u.Trim()).Where(u => u.Length > 0).Distinct())
            db.PageUserAccess.Add(new PageUserAccess { PageId = page.Id, UserId = uid });

        if (db.ChangeTracker.HasChanges())
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // meta-tag / access-list save failed; the page itself was already committed.
                // If the page is restricted but the ACL rows failed to persist, no user would be able to
                // view it — roll back IsRestricted so the page stays accessible rather than silently locked.
                if (page.IsRestricted)
                {
                    try
                    {
                        await using var fix = await dbFactory.CreateDbContextAsync(ct);
                        var row = await fix.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == page.Id, ct);
                        if (row is not null) { row.IsRestricted = false; await fix.SaveChangesAsync(ct); }
                    }
                    catch { /* best effort */ }
                }
                // Page content was saved but meta/ACL were not — return partial success so the caller
                // can surface a warning rather than silently losing SEO and access-control edits.
                return new PageSaveResult(true, page.Id, trust,
                    "SEO and access-control settings could not be saved — please save the page again.");
            }
        }

        return new PageSaveResult(true, page.Id, trust, null);
    }

    public async Task<bool> SetPublishedAsync(int id, bool published, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var page = await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return false;
        page.IsPublished = published;
        // Keep WorkflowState in sync: publishing forces state = "Published"; unpublishing clears it only
        // if it was "Published" (so a "Review"-state page unpublished stays "Review" for the workflow).
        if (published)
            page.WorkflowState = "Published";
        else if (string.Equals(page.WorkflowState, "Published", StringComparison.OrdinalIgnoreCase))
            page.WorkflowState = null;
        page.ModifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
    public async Task<bool> SetEnabledAsync(int id, bool enabled, CancellationToken ct = default) => await FlagAsync(id, ct, p => p.Enabled = enabled);

    public async Task<bool> SoftDeleteAsync(int id, CancellationToken ct = default) =>
        await FlagAsync(id, ct, p => { p.IsDeleted = true; p.DeletedUtc = DateTime.UtcNow; });

    public async Task<bool> MoveAsync(int id, int? parentId, int sortOrder, CancellationToken ct = default)
    {
        if (parentId == id) return false;   // a page cannot be its own parent
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var page = await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return false;

        // Cycle guard: walk up from the proposed parent — if we reach `id`, the move would make the page
        // its own ancestor. (Bounded walk so a pre-existing bad row can never spin forever.)
        var cursorId = parentId;
        for (var hops = 0; cursorId is int cid && hops < 256; hops++)
        {
            if (cid == id) return false;
            cursorId = await db.Pages.IgnoreQueryFilters().Where(p => p.Id == cid).Select(p => p.ParentId).FirstOrDefaultAsync(ct);
        }

        page.ParentId = parentId;
        page.SortOrder = sortOrder;
        page.ModifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SwapSortOrderAsync(int idA, int sortA, int idB, int sortB, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pageA = await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == idA, ct);
        var pageB = await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == idB, ct);
        if (pageA is null || pageB is null) return false;

        var now = DateTime.UtcNow;
        pageA.SortOrder = sortA;
        pageA.ModifiedUtc = now;
        pageB.SortOrder = sortB;
        pageB.ModifiedUtc = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // An explicit empty list serializes as "[]" — "no plugins" — distinct from null ("inherit the site's").
    private static string SerializePlugins(List<string> plugins) => JsonSerializer.Serialize(plugins);

    /// <summary>
    /// The plugin refs a page actually renders: its own list when it has one (even an empty one), else the
    /// site's default chrome. Null <paramref name="pageJson"/> is the only "inherit".
    /// </summary>
    public static List<string> EffectivePlugins(string? pageJson, string? siteDefaultJson) =>
        string.IsNullOrWhiteSpace(pageJson) ? DeserializePlugins(siteDefaultJson) : DeserializePlugins(pageJson);

    public static List<string> DeserializePlugins(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private async Task<bool> FlagAsync(int id, CancellationToken ct, Action<Page> mutate)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var page = await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return false;
        mutate(page);
        page.ModifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
