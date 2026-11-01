using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;

namespace IdeaCms.Core.Services;

/// <summary>Result of a slug-history lookup: the page's current slug and the redirect kind.</summary>
public sealed record SlugRedirectResult(string TargetSlug, int StatusCode = 301);

/// <summary>
/// Manages <see cref="PageSlugHistory"/> — the record of old and vanity slugs that should redirect to a
/// page's current slug. Written automatically on rename, and manually for vanity URLs.
/// </summary>
public interface ISlugRedirectService
{
    /// <summary>
    /// Returns a redirect target if <paramref name="slug"/> appears in the slug history for the given site.
    /// Returns null if the slug is not in history (a real 404 should follow). The target slug is the page's
    /// CURRENT active slug; status 301 = permanent (rename), 302 = temporary (not currently used but reserved).
    /// </summary>
    Task<SlugRedirectResult?> CheckRedirectAsync(int? siteId, string slug, CancellationToken ct = default);

    /// <summary>
    /// Manually adds a vanity URL (alias) for an existing page. Idempotent — a duplicate add returns true
    /// without error. Returns false if the page does not exist.
    /// </summary>
    Task<bool> AddVanityRedirectAsync(int pageId, string vanitySlug, string? userId = null, CancellationToken ct = default);

    /// <summary>All slug history entries for a page, newest first.</summary>
    Task<IReadOnlyList<PageSlugHistory>> GetHistoryAsync(int pageId, CancellationToken ct = default);
}

public sealed class SlugRedirectService(IDbContextFactory<CmsDbContext> dbFactory) : ISlugRedirectService
{
    public async Task<SlugRedirectResult?> CheckRedirectAsync(int? siteId, string slug, CancellationToken ct = default)
    {
        // Normalize before the DB compare: OldSlug values are always stored lowercase, so a mixed-case
        // incoming path (e.g. /About instead of /about) would otherwise miss the redirect row.
        slug = slug.Trim('/').ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Find a history record whose page still exists, is published, enabled, and belongs to the same site.
        // The page may have been renamed multiple times; we always redirect to its CURRENT slug.
        var match = await db.PageSlugHistory
            .Join(db.Pages,
                h => h.PageId,
                p => p.Id,
                (h, p) => new { h, p })
            .Where(x => x.h.OldSlug == slug
                     && x.p.SiteId == siteId
                     && x.p.IsPublished
                     && x.p.Enabled
                     && !x.p.IsDeleted)
            .Select(x => new { x.p.Slug })
            .FirstOrDefaultAsync(ct);

        if (match is null) return null;
        if (string.Equals(match.Slug, slug, StringComparison.OrdinalIgnoreCase)) return null; // same slug, no redirect needed
        return new SlugRedirectResult(match.Slug.ToLowerInvariant(), 301);
    }

    public async Task<bool> AddVanityRedirectAsync(int pageId, string vanitySlug, string? userId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var normalizedSlug = vanitySlug.Trim('/').ToLowerInvariant();

        var currentSlug = await db.Pages.IgnoreQueryFilters().Where(p => p.Id == pageId).Select(p => (string?)p.Slug).FirstOrDefaultAsync(ct);
        if (currentSlug is null) return false;

        // Adding a vanity alias that matches the page's own current slug is a no-op — the slug is live
        // already and adding a history entry for it creates a confusing orphaned redirect row.
        if (string.Equals(currentSlug, normalizedSlug, StringComparison.OrdinalIgnoreCase)) return true;

        // Idempotent: do nothing if this (pageId, oldSlug) already exists.
        if (await db.PageSlugHistory.AnyAsync(h => h.PageId == pageId && h.OldSlug == normalizedSlug, ct))
            return true;

        db.PageSlugHistory.Add(new PageSlugHistory
        {
            PageId = pageId, OldSlug = normalizedSlug,
            IsVanity = true, AddedByUserId = userId, CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<PageSlugHistory>> GetHistoryAsync(int pageId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PageSlugHistory
            .Where(h => h.PageId == pageId)
            .OrderByDescending(h => h.CreatedUtc)
            .ToListAsync(ct);
    }
}
