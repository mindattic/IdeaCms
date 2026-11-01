using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;

namespace IdeaCms.Core.Services;

/// <summary>A point-in-time snapshot of a <see cref="Page"/> row from the temporal history table.</summary>
public sealed record PageHistoryEntry(
    int PageId,
    string Slug, string Title, bool IsPublished, bool Enabled, PageKind Kind,
    string? ThemeKey, int? ThemeVersion,
    string? BodyHtml, string? PageCss, string? PageJs,
    ContentTrust BodyTrust,
    DateTime ValidFrom, DateTime ValidTo,
    string? AuthoredByUserId = null,
    string? WorkflowState = null,
    string? SeoTitle = null,
    string? ActivePluginsJson = null,
    bool IsRestricted = false);

/// <summary>
/// Surfaces the SQL Server temporal history of the <see cref="Page"/> table (wiki-like rollback).
/// <see cref="GetHistoryAsync"/> requires a SQL Server temporal table (throws on InMemory / SQLite).
/// <see cref="RestoreAsync"/> takes a pre-fetched <see cref="PageHistoryEntry"/> so it is DB-agnostic
/// and can be tested without a temporal provider.
/// </summary>
public interface IPageHistoryService
{
    /// <summary>All temporal states of a page, most recent first (current row is index 0).</summary>
    Task<IReadOnlyList<PageHistoryEntry>> GetHistoryAsync(int pageId, CancellationToken ct = default);

    /// <summary>
    /// Writes a historical snapshot's content fields back onto the current page row, re-stamping
    /// trust from the restoring user's claims (Author iff the user holds
    /// <c>Cms.AuthorRawMarkup</c>). Returns false if the page no longer exists.
    /// </summary>
    Task<bool> RestoreAsync(PageHistoryEntry snapshot, ClaimsPrincipal user, CancellationToken ct = default);
}

public sealed class PageHistoryService(IDbContextFactory<CmsDbContext> dbFactory) : IPageHistoryService
{
    public async Task<IReadOnlyList<PageHistoryEntry>> GetHistoryAsync(int pageId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Pages
            .TemporalAll()
            .Where(p => p.Id == pageId)
            .OrderByDescending(p => EF.Property<DateTime>(p, "PeriodStart"))
            .Select(p => new PageHistoryEntry(
                p.Id, p.Slug, p.Title, p.IsPublished, p.Enabled, p.Kind,
                p.ThemeKey, p.ThemeVersion,
                p.BodyHtml, p.PageCss, p.PageJs, p.BodyTrust,
                EF.Property<DateTime>(p, "PeriodStart"),
                EF.Property<DateTime>(p, "PeriodEnd"),
                p.AuthoredByUserId, p.WorkflowState,
                p.SeoTitle, p.ActivePluginsJson, p.IsRestricted))
            .ToListAsync(ct);
    }

    public async Task<bool> RestoreAsync(PageHistoryEntry snapshot, ClaimsPrincipal user, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var page = await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == snapshot.PageId, ct);
        if (page is null) return false;

        page.Title = snapshot.Title;
        page.Kind = snapshot.Kind;
        page.ThemeKey = snapshot.ThemeKey;
        page.ThemeVersion = snapshot.ThemeVersion;
        page.BodyHtml = snapshot.BodyHtml;
        page.PageCss = snapshot.PageCss;
        page.PageJs = snapshot.PageJs;
        page.IsPublished = snapshot.IsPublished;
        page.Enabled = snapshot.Enabled;
        page.WorkflowState = snapshot.WorkflowState;
        // Keep IsPublished in sync with the restored WorkflowState so the invariant
        // WorkflowState == "Published" ↔ IsPublished == true is maintained after restore.
        if (snapshot.WorkflowState is not null)
            page.IsPublished = string.Equals(snapshot.WorkflowState, "Published", StringComparison.OrdinalIgnoreCase);
        page.SeoTitle = snapshot.SeoTitle;
        page.ActivePluginsJson = snapshot.ActivePluginsJson;
        page.IsRestricted = snapshot.IsRestricted;

        // Re-stamp trust from the restoring user's claims (M.IDEA-LAW-5): the restore is a write.
        var (trust, authoredBy) = PageAuthoring.Stamp(user);
        page.BodyTrust = trust;
        page.AuthoredByUserId = authoredBy;
        page.AuthorTrustVersion += 1;
        page.ModifiedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return true;
    }
}
