using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Rendering;
using IdeaCms.Packaging;

namespace IdeaCms.Core.Discovery;

/// <summary>
/// Runs every <see cref="ICmsContentSource"/> at boot, upserts <see cref="CmsContentDefinition"/> rows
/// by (Kind,Key,Version,Origin), flips disappeared rows to inactive (degrade to placeholder, never
/// delete), resolves collisions by Priority (compiled wins; loser kept visible as shadowed), then
/// loads the active+enabled winners into the in-memory <see cref="ContentCatalog"/>.
/// </summary>
public sealed class DiscoveryService(
    IDbContextFactory<CmsDbContext> dbFactory,
    IEnumerable<ICmsContentSource> sources,
    ContentCatalog catalog) : IDisposable
{
    // Serializes concurrent ReloadCatalogAsync calls so IsShadowed writes and LoadSnapshot
    // never interleave across two parallel admin enable/disable operations.
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    public void Dispose() => _reloadLock.Dispose();

    public async Task RunAsync(CancellationToken ct = default)
    {
        var discovered = sources
            .SelectMany(s => s.Discover().Select(d => d with { Origin = s.Origin, Priority = s.Priority }))
            .ToList();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.ContentDefinitions.ToListAsync(ct);
        var byIdentity = existing.ToDictionary(e => (e.Kind, e.Key, e.Version, e.Origin));
        var now = DateTime.UtcNow;
        var seen = new HashSet<(ContentKind, string, int, ContentOrigin)>();

        foreach (var d in discovered)
        {
            var id = (d.Kind, d.Key, d.Version, d.Origin);
            seen.Add(id);
            if (byIdentity.TryGetValue(id, out var row))
            {
                row.DisplayName = d.DisplayName; row.Category = d.Category; row.Strategy = d.Strategy;
                row.RenderMode = d.RenderMode; row.Scope = d.Scope; row.ClrTypeName = d.ClrTypeName;
                row.AssemblyName = d.AssemblyName; row.AssetMount = d.AssetMount; row.Priority = d.Priority;
                row.RawBundleJson = null; row.IsActive = true; row.DiscoveredUtc = now;
            }
            else
            {
                db.ContentDefinitions.Add(new CmsContentDefinition
                {
                    Kind = d.Kind, Key = d.Key, Version = d.Version, Origin = d.Origin,
                    DisplayName = d.DisplayName, Category = d.Category, Strategy = d.Strategy,
                    RenderMode = d.RenderMode, Scope = d.Scope, ClrTypeName = d.ClrTypeName,
                    AssemblyName = d.AssemblyName, AssetMount = d.AssetMount, Priority = d.Priority,
                    IsActive = true, Enabled = true, DiscoveredUtc = now,
                });
            }
        }

        // Disappeared rows -> inactive (kept for history + stable placeholders), never deleted. SCOPED to
        // the origins THESE sources own (Compiled): Package rows are managed by PackageInstallService, so
        // discovery must never deactivate an installed package — otherwise every restart (which runs only the
        // compiled sources) would flip installed .idea packages inactive and drop them from the catalog.
        var ownedOrigins = sources.Select(s => s.Origin).ToHashSet();
        foreach (var row in existing.Where(e => ownedOrigins.Contains(e.Origin)
                     && !seen.Contains((e.Kind, e.Key, e.Version, e.Origin))))
            row.IsActive = false;

        await db.SaveChangesAsync(ct);

        await ReloadCatalogAsync(ct);
    }

    /// <summary>
    /// The cheap live-apply tail: recompute shadowing and reload the in-memory catalog (enabled winners
    /// + the disabled-identity snapshot) WITHOUT re-running any source.Discover(). Called at the end of
    /// <see cref="RunAsync"/> and after an admin enable/disable so the live catalog reflects the change.
    /// </summary>
    public async Task ReloadCatalogAsync(CancellationToken ct = default)
    {
        await _reloadLock.WaitAsync(ct);
        try
        {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Collision resolution: within a (Kind,Key,Version) the highest Priority active row wins; the
        // rest are shadowed (a Package may only win over Compiled when AllowOverride was confirmed).
        var all = await db.ContentDefinitions.Where(x => x.IsActive).ToListAsync(ct);
        foreach (var grp in all.GroupBy(x => (x.Kind, x.Key, x.Version)))
        {
            // Enabled rows always beat disabled rows of the same identity — an admin who explicitly
            // disabled a high-priority Compiled citizen should not have it shadow an enabled Package row.
            var ordered = grp.OrderByDescending(x => x.Enabled ? 1 : 0)
                             .ThenByDescending(x => x.Priority).ToList();
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].IsShadowed = i != 0;
        }
        await db.SaveChangesAsync(ct);

        // No-schema asset data path: a package's ordered css[]/scripts[] live in its verbatim manifest;
        // surface them onto the in-memory descriptor's Extra bag so PageAssetCollector can hoist them into
        // <head>. One corrupt manifest degrades only that citizen's head assets to empty — never aborts the
        // reload. (Compiled citizens carry no manifest; their Extra stays null exactly as before.)
        var manifestByIdentity = new Dictionary<(string Category, string Key, int Version), IdeaManifest>();
        foreach (var pkg in await db.InstalledPackages.Where(p => p.Enabled).ToListAsync(ct))
        {
            try { manifestByIdentity[(pkg.Category, pkg.Key, pkg.Version)] = ManifestReader.Read(pkg.ManifestJson); }
            catch (JsonException) { /* leave this identity unmapped -> Extra null -> empty head assets */ }
        }

        catalog.LoadSnapshot(
            all.Where(x => !x.IsShadowed && x.Enabled).Select(x => ToDescriptor(x, LookupManifest(x, manifestByIdentity))),
            all.Where(x => !x.IsShadowed && !x.Enabled).Select(x => (x.Kind, x.Key, x.Version)));
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private static IdeaManifest? LookupManifest(
        CmsContentDefinition x, IReadOnlyDictionary<(string, string, int), IdeaManifest> map) =>
        x.Origin == ContentOrigin.Package && map.TryGetValue((x.Category, x.Key, x.Version), out var m) ? m : null;

    private static ContentDescriptor ToDescriptor(CmsContentDefinition x, IdeaManifest? manifest) => new()
    {
        Kind = x.Kind, Key = x.Key, Version = x.Version, DisplayName = x.DisplayName,
        Category = x.Category, Origin = x.Origin, Priority = x.Priority, Strategy = x.Strategy,
        RenderMode = x.RenderMode, Scope = x.Scope, ClrTypeName = x.ClrTypeName,
        AssemblyName = x.AssemblyName, AssetMount = x.AssetMount, AllowOverride = x.AllowOverride,
        Extra = x.Origin == ContentOrigin.Package && manifest is not null ? ManifestAssetPacker.PackExtra(manifest) : null,
    };
}
