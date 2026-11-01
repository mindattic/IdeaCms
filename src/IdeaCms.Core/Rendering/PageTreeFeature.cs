using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;

namespace IdeaCms.Core.Rendering;

/// <summary>
/// Host implementation of <see cref="IPageTree"/>: returns the published, enabled, non-deleted child
/// pages of a page (by its <see cref="Entities.ContentEntityBase.Uid"/>), ordered by SortOrder then Title.
/// Resolved by a Component via <see cref="IRenderContext.TryGetFeature{T}"/>; the TableOfContents component
/// uses it to render the current page's children — or nothing when there are none. Never throws into a render.
/// </summary>
public sealed class PageTreeFeature(IDbContextFactory<CmsDbContext> factory) : IPageTree
{
    public async Task<IReadOnlyList<ChildPage>> ChildrenOfAsync(Guid pageId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var parent = await db.Pages.FirstOrDefaultAsync(p => p.Uid == pageId && !p.IsDeleted, ct);
            if (parent is null) return Array.Empty<ChildPage>();

            return await db.Pages
                .Where(p => p.ParentId == parent.Id && p.IsPublished && p.Enabled && !p.IsDeleted)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
                .Select(p => new ChildPage(p.Slug, p.Title, p.OpenInNewWindow, p.Uid))
                .ToListAsync(ct);
        }
        catch
        {
            return Array.Empty<ChildPage>();
        }
    }

    public Task<IReadOnlyList<ChildPage>> ChildrenOfSlugAsync(string slug, CancellationToken ct = default) =>
        ChildrenOfSlugAsync(Guid.Empty, slug, ct);

    /// <summary>
    /// Slug lookups are scoped to a site because a slug is unique only within one: the Pages unique index
    /// is on <c>(SiteId, Slug)</c>, so an unscoped match can return ANOTHER domain's page on a deployment
    /// that serves several sites (M.IDEA-§4.10). <see cref="Guid.Empty"/> keeps the legacy unscoped behaviour
    /// for the slug-only overload — now at least ORDERED, so which page answers is deterministic instead
    /// of down to row order.
    /// </summary>
    public async Task<IReadOnlyList<ChildPage>> ChildrenOfSlugAsync(Guid siteId, string slug, CancellationToken ct = default)
    {
        try
        {
            slug = (slug ?? "").Trim('/');
            await using var db = await factory.CreateDbContextAsync(ct);

            // An unknown site uid falls through to the unscoped lookup rather than returning nothing, so a
            // host that cannot name its site degrades to the old behaviour instead of losing its nav.
            int? scopeId = siteId == Guid.Empty
                ? null
                : await db.Sites.Where(s => s.Uid == siteId).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);

            var parent = await db.Pages
                .Where(p => p.Slug == slug && !p.IsDeleted && (scopeId == null || p.SiteId == scopeId))
                .OrderBy(p => p.SiteId).ThenBy(p => p.Id)
                .FirstOrDefaultAsync(ct);

            return parent is null ? Array.Empty<ChildPage>() : await ChildrenOfAsync(parent.Uid, ct);
        }
        catch
        {
            return Array.Empty<ChildPage>();
        }
    }

    public async Task<IReadOnlyList<ChildPageNode>> DescendantsTreeAsync(Guid pageId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var root = await db.Pages.FirstOrDefaultAsync(p => p.Uid == pageId && !p.IsDeleted, ct);
            if (root is null) return Array.Empty<ChildPageNode>();

            // Single query: load all published, enabled pages in this site, then build the tree in memory.
            var siteId = root.SiteId;
            var all = await db.Pages
                .Where(p => p.SiteId == siteId && p.IsPublished && p.Enabled && !p.IsDeleted)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
                .Select(p => new { p.Id, p.ParentId, p.Slug, p.Title, p.OpenInNewWindow })
                .ToListAsync(ct);

            var byParent = all.GroupBy(p => p.ParentId).ToDictionary(g => g.Key, g => g.ToList());

            // visited guards against parent cycles written outside the app; without it a cycle causes StackOverflow.
            var visited = new HashSet<int> { root.Id };
            IReadOnlyList<ChildPageNode> BuildNodes(int parentId)
            {
                if (!byParent.TryGetValue(parentId, out var kids)) return Array.Empty<ChildPageNode>();
                return kids
                    .Where(k => visited.Add(k.Id))
                    .Select(k => new ChildPageNode(k.Slug, k.Title, BuildNodes(k.Id), k.OpenInNewWindow))
                    .ToList();
            }

            return BuildNodes(root.Id);
        }
        catch
        {
            return Array.Empty<ChildPageNode>();
        }
    }
}
