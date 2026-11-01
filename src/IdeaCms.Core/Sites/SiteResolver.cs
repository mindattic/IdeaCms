using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;

namespace IdeaCms.Core.Sites;

/// <summary>
/// Resolves the <see cref="Site"/> a request belongs to from its host header. One deployment, many
/// domains — the seam the schema has carried since migration #1 (<c>Site.HostBindings</c>), now
/// actually read.
/// </summary>
public interface ISiteResolver
{
    /// <summary>
    /// The site bound to <paramref name="requestHost"/> (e.g. <c>www.example.com:5199</c>), or the
    /// default site when no binding matches. Never throws; returns null only when there are no sites.
    /// </summary>
    Task<Site?> ResolveAsync(string? requestHost, CmsDbContext db, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// Falls back to the default site whenever nothing matches, which is what keeps every existing
/// single-site deployment behaving exactly as it did: a lone site with empty HostBindings answers on
/// every hostname, as it always has. Multi-site is opt-in per site, by filling in its bindings.
/// <para>
/// Sites is a tiny table and this REPLACES the default-site lookup the render path already did, so
/// resolution costs no extra query. Precedence is host+port > host > wildcard > catch-all > default;
/// within one quality band the default site wins, then the lowest id, so the answer is stable rather
/// than dependent on row order.
/// </para>
/// </remarks>
public sealed class SiteResolver : ISiteResolver
{
    public async Task<Site?> ResolveAsync(string? requestHost, CmsDbContext db, CancellationToken ct = default)
    {
        var sites = await db.Sites.OrderBy(s => s.Id).ToListAsync(ct);
        return Resolve(requestHost, sites);
    }

    /// <summary>The pure decision, exposed so it can be tested without a database.</summary>
    public static Site? Resolve(string? requestHost, IReadOnlyList<Site> sites)
    {
        if (sites.Count == 0) return null;

        Site? best = null;
        var bestQuality = HostBinding.MatchQuality.None;

        foreach (var site in sites)
        {
            var quality = HostBinding.Match(site.HostBindings, requestHost);
            if (quality == HostBinding.MatchQuality.None) continue;

            if (quality > bestQuality
                || (quality == bestQuality && best is not null && Prefer(site, best)))
            {
                best = site;
                bestQuality = quality;
            }
        }

        // No binding claimed this host: the default site answers, exactly as it did before bindings
        // were read at all.
        return best ?? sites.FirstOrDefault(s => s.IsDefault) ?? sites[0];
    }

    /// <summary>Tie-break within one match quality: the default site, then the lowest id.</summary>
    private static bool Prefer(Site candidate, Site incumbent) =>
        candidate.IsDefault != incumbent.IsDefault
            ? candidate.IsDefault
            : candidate.Id < incumbent.Id;
}
