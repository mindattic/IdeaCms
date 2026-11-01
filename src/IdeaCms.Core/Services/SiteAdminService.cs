using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Sites;

namespace IdeaCms.Core.Services;

public sealed record SiteSummary(
    int Id, string Key, string Name, string HostBindings,
    string DefaultThemeKey, int DefaultThemeVersion, string DefaultThemeMode, bool IsDefault, int PageCount);

/// <summary>
/// Site CRUD for the Admin "Sites" panel. Without this, a second domain could only be added by hand
/// in SQL — which is why <c>HostBindings</c> sat unread in the schema for so long.
/// </summary>
public interface ISiteAdminService
{
    Task<IReadOnlyList<SiteSummary>> ListAsync(CancellationToken ct = default);
    Task<(bool Ok, string? Error, int Id)> CreateAsync(string key, string name, string hostBindings,
        string defaultThemeKey, int defaultThemeVersion, string defaultThemeMode, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> UpdateAsync(int id, string name, string hostBindings,
        string defaultThemeKey, int defaultThemeVersion, string defaultThemeMode, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> MakeDefaultAsync(int id, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> DeleteAsync(int id, CancellationToken ct = default);
    /// <summary>Which site a given host would resolve to right now — the panel's "test a hostname" box.</summary>
    Task<SiteSummary?> WhichSiteAsync(string host, CancellationToken ct = default);

}

public sealed class SiteAdminService(CmsDbContext db) : ISiteAdminService
{
    public async Task<IReadOnlyList<SiteSummary>> ListAsync(CancellationToken ct = default)
    {
        var sites = await db.Sites.OrderBy(s => s.IsDefault ? 0 : 1).ThenBy(s => s.Id).ToListAsync(ct);
        var counts = await db.Pages.GroupBy(p => p.SiteId)
            .Select(g => new { SiteId = g.Key, Count = g.Count() }).ToListAsync(ct);
        return sites.Select(s => ToSummary(s, counts.FirstOrDefault(c => c.SiteId == s.Id)?.Count ?? 0)).ToList();
    }

    public async Task<(bool Ok, string? Error, int Id)> CreateAsync(string key, string name, string hostBindings,
        string defaultThemeKey, int defaultThemeVersion, string defaultThemeMode, CancellationToken ct = default)
    {
        key = (key ?? "").Trim().ToLowerInvariant();
        if (key.Length == 0) return (false, "A site key is required.", 0);
        if (await db.Sites.AnyAsync(s => s.Key == key, ct)) return (false, $"A site with key \"{key}\" already exists.", 0);

        var conflict = await FindBindingConflictAsync(hostBindings, excludeSiteId: null, ct);
        if (conflict is not null) return (false, conflict, 0);

        var site = new Site
        {
            Key = key,
            Name = string.IsNullOrWhiteSpace(name) ? key : name.Trim(),
            HostBindings = CleanBindings(hostBindings),
            DefaultThemeKey = (defaultThemeKey ?? "").Trim(),
            DefaultThemeVersion = defaultThemeVersion <= 0 ? 1 : defaultThemeVersion,
            DefaultThemeMode = NormalizeThemeMode(defaultThemeMode),
            // Never steal default from an existing site as a side effect of creating one.
            IsDefault = !await db.Sites.AnyAsync(ct),
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);
        return (true, null, site.Id);
    }

    public async Task<(bool Ok, string? Error)> UpdateAsync(int id, string name, string hostBindings,
        string defaultThemeKey, int defaultThemeVersion, string defaultThemeMode, CancellationToken ct = default)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (site is null) return (false, "Site not found.");

        var conflict = await FindBindingConflictAsync(hostBindings, excludeSiteId: id, ct);
        if (conflict is not null) return (false, conflict);

        site.Name = string.IsNullOrWhiteSpace(name) ? site.Key : name.Trim();
        site.HostBindings = CleanBindings(hostBindings);
        site.DefaultThemeKey = (defaultThemeKey ?? "").Trim();
        site.DefaultThemeVersion = defaultThemeVersion <= 0 ? 1 : defaultThemeVersion;
        site.DefaultThemeMode = NormalizeThemeMode(defaultThemeMode);
        site.ModifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> MakeDefaultAsync(int id, CancellationToken ct = default)
    {
        var sites = await db.Sites.ToListAsync(ct);
        var target = sites.FirstOrDefault(s => s.Id == id);
        if (target is null) return (false, "Site not found.");

        // Exactly one default: it is the answer for every unbound hostname, so two would make
        // resolution depend on row order.
        foreach (var s in sites) s.IsDefault = s.Id == id;
        target.ModifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(int id, CancellationToken ct = default)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (site is null) return (false, "Site not found.");
        if (site.IsDefault) return (false, "The default site cannot be deleted. Make another site the default first.");

        // Reference-guarded, per HOUSE-LAW-2: deleting a site out from under its pages would orphan
        // them onto whatever site resolves next, silently republishing them under another domain.
        var pages = await db.Pages.CountAsync(p => p.SiteId == id, ct);
        if (pages > 0) return (false, $"This site still has {pages} page(s). Move or delete them first.");

        db.Sites.Remove(site);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }


    public async Task<SiteSummary?> WhichSiteAsync(string host, CancellationToken ct = default)
    {
        var sites = await db.Sites.OrderBy(s => s.Id).ToListAsync(ct);
        var match = SiteResolver.Resolve(host, sites);
        if (match is null) return null;
        var count = await db.Pages.CountAsync(p => p.SiteId == match.Id, ct);
        return ToSummary(match, count);
    }

    /// <summary>
    /// Rejects a binding another site already claims. Two sites answering the same hostname is not a
    /// configuration anyone means, and the loser would be invisible with no error anywhere.
    /// </summary>
    private async Task<string?> FindBindingConflictAsync(string? hostBindings, int? excludeSiteId, CancellationToken ct)
    {
        var wanted = HostBinding.Split(hostBindings).ToList();
        if (wanted.Count == 0) return null;

        var others = await db.Sites.Where(s => excludeSiteId == null || s.Id != excludeSiteId).ToListAsync(ct);
        foreach (var other in others)
        {
            var taken = HostBinding.Split(other.HostBindings).ToHashSet(StringComparer.Ordinal);
            var clash = wanted.FirstOrDefault(taken.Contains);
            if (clash is not null)
                return $"\"{clash}\" is already bound to site \"{other.Key}\".";
        }
        return null;
    }

    private static string CleanBindings(string? raw) => string.Join(", ", HostBinding.Split(raw).Distinct());

    private static string NormalizeThemeMode(string? mode) => mode == "dark" ? "dark" : "light";

    private static SiteSummary ToSummary(Site s, int pageCount) => new(
        s.Id, s.Key, s.Name, s.HostBindings, s.DefaultThemeKey, s.DefaultThemeVersion, s.DefaultThemeMode, s.IsDefault, pageCount);
}
