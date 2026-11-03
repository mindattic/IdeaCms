using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Rendering;
using IdeaCms.Packaging;

namespace IdeaCms.Core.Services;

public readonly record struct LifecycleResult(bool Ok, string? Error)
{
    public static readonly LifecycleResult Success = new(true, null);
    public static LifecycleResult Fail(string error) => new(false, error);
}

/// <summary>Outcome of a delete attempt. Blocked => reference-guarded; DisabledInstead => compiled-origin soft path.</summary>
public sealed record DeleteGuardResult(bool Deleted, bool Blocked, bool DisabledInstead, IReadOnlyList<string> PinnedBy, string? Message);

/// <summary>
/// A page reference used by the (pure, DB-free) delete-guard scan. <see cref="Uses"/> carries a COMPILED
/// page's declared <c>uses[]</c> string ids (it has no BodyHtml to scan); null for data pages.
/// </summary>
public readonly record struct PageRef(
    string Slug, string? BodyHtml, string? ThemeKey, int? ThemeVersion, bool Enabled, bool IsPublished,
    IReadOnlyList<string>? Uses = null,
    string? ActivePluginsJson = null,
    bool IsDeleted = false);

/// <summary>
/// Admin lifecycle for content definitions: list, enable/disable (with live catalog reload), and
/// VERSION-SPECIFIC, reference-guarded delete. The core invariant: a page must never be left invalid —
/// you cannot delete a version that an enabled+published page pins, and you cannot delete the last
/// enabled version that a floating ("latest") reference would orphan. Compiled-origin definitions are
/// disabled rather than hard-deleted (discovery would re-add them); true removal is reserved for Package
/// origin (Phase 5).
/// </summary>
public interface IContentLifecycleService
{
    Task<IReadOnlyList<CmsContentDefinition>> ListAsync(ContentKind? kind = null, CancellationToken ct = default);
    Task<LifecycleResult> SetEnabledAsync(int id, bool enabled, CancellationToken ct = default);
    Task<DeleteGuardResult> CanDeleteAsync(ContentKind kind, string key, int version, CancellationToken ct = default);
    Task<DeleteGuardResult> DeleteAsync(ContentKind kind, string key, int version, CancellationToken ct = default);
}

public sealed class ContentLifecycleService(IDbContextFactory<CmsDbContext> dbFactory, DiscoveryService discovery)
    : IContentLifecycleService
{
    public async Task<IReadOnlyList<CmsContentDefinition>> ListAsync(ContentKind? kind = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.ContentDefinitions.AsNoTracking();
        if (kind is ContentKind k) q = q.Where(d => d.Kind == k);
        return await q.OrderBy(d => d.Kind).ThenBy(d => d.Key).ThenByDescending(d => d.Version).ToListAsync(ct);
    }

    public async Task<LifecycleResult> SetEnabledAsync(int id, bool enabled, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var def = await db.ContentDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null) return LifecycleResult.Fail("Content definition not found.");
        if (def.Enabled != enabled)
        {
            def.Enabled = enabled;
            await db.SaveChangesAsync(ct);
            await discovery.ReloadCatalogAsync(ct);   // live-apply: the catalog reflects it immediately
        }
        return LifecycleResult.Success;
    }

    public async Task<DeleteGuardResult> CanDeleteAsync(ContentKind kind, string key, int version, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var def = await db.ContentDefinitions
            .FirstOrDefaultAsync(d => d.Kind == kind && d.Key == key && d.Version == version, ct);
        if (def is null)
            return new DeleteGuardResult(false, false, false, Array.Empty<string>(), "Content definition not found.");

        // IgnoreQueryFilters: we need IsDeleted so we can skip soft-deleted pages in FindBlockingPages
        // (soft-deleted pages are not live references even if their flags were not cleared on delete).
        var pageRows = await db.Pages.IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && p.Enabled && p.IsPublished)
            .Select(p => new { p.Slug, p.SiteId, p.BodyHtml, p.ThemeKey, p.ThemeVersion, p.Enabled, p.IsPublished, p.Kind, p.ComponentTypeName, p.ActivePluginsJson, p.IsDeleted })
            .ToListAsync(ct);
        var siteDefaults = await LoadSiteDefaultsAsync(db, ct);

        // A COMPILED (Code) page references Components/Controls through its package manifest uses[], NOT its
        // BodyHtml — so the guard must see those too, or a referenced citizen could be deleted out from under
        // it. Map entryType -> uses[] from enabled Page packages.
        var usesByType = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var pkg in await db.InstalledPackages.Where(p => p.Enabled && p.Category == "Page").ToListAsync(ct))
        {
            try
            {
                var m = ManifestReader.Read(pkg.ManifestJson);
                if (!string.IsNullOrEmpty(m.EntryType)) usesByType[m.EntryType!] = m.Uses;
            }
            catch (JsonException) { /* one corrupt manifest only drops that page's uses[] from the guard */ }
        }

        // A page with no theme / plugin selection of its own renders its SITE's defaults (PageHost), so the
        // guard must see those inherited references too — otherwise the last enabled version of a site's
        // default theme or chrome plugin could be deleted out from under every page that inherits it.
        var pages = pageRows.Select(p =>
        {
            var sd = p.SiteId is int sid ? siteDefaults.GetValueOrDefault(sid) : default;
            var inheritsTheme = p.ThemeKey is null && !string.IsNullOrWhiteSpace(sd.ThemeKey);
            return new PageRef(
                p.Slug, p.BodyHtml,
                inheritsTheme ? sd.ThemeKey : p.ThemeKey,
                inheritsTheme ? sd.ThemeVersion : p.ThemeVersion,
                p.Enabled, p.IsPublished,
                p.Kind == PageKind.Code && p.ComponentTypeName is not null
                    && usesByType.TryGetValue(p.ComponentTypeName, out var u) ? u : null,
                string.IsNullOrWhiteSpace(p.ActivePluginsJson) ? sd.PluginsJson : p.ActivePluginsJson,
                p.IsDeleted);
        }).ToList();

        // Versions of this key that would REMAIN enabled+live after deleting `version`.
        var otherEnabled = await db.ContentDefinitions
            .Where(d => d.Kind == kind && d.Key == key && d.Version != version && d.Enabled && d.IsActive && !d.IsShadowed)
            .Select(d => d.Version)
            .ToListAsync(ct);

        var blocking = FindBlockingPages(kind, key, version, pages, otherEnabled);
        return blocking.Count > 0
            ? new DeleteGuardResult(false, true, false, blocking, $"Blocked: {blocking.Count} page(s) still use this version.")
            : new DeleteGuardResult(false, false, false, Array.Empty<string>(), null);
    }

    public async Task<DeleteGuardResult> DeleteAsync(ContentKind kind, string key, int version, CancellationToken ct = default)
    {
        // Guard and delete share one context to eliminate the TOCTOU window that arises when
        // CanDeleteAsync runs in its own context (disposed before the delete context opens).
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var def = await db.ContentDefinitions
            .FirstOrDefaultAsync(d => d.Kind == kind && d.Key == key && d.Version == version, ct);
        if (def is null)
            return new DeleteGuardResult(false, false, false, Array.Empty<string>(), "Content definition not found.");

        var pageRows = await db.Pages.IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && p.Enabled && p.IsPublished)
            .Select(p => new { p.Slug, p.SiteId, p.BodyHtml, p.ThemeKey, p.ThemeVersion, p.Enabled, p.IsPublished, p.Kind, p.ComponentTypeName, p.ActivePluginsJson, p.IsDeleted })
            .ToListAsync(ct);
        var siteDefaults = await LoadSiteDefaultsAsync(db, ct);

        var usesByType = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var pkg in await db.InstalledPackages.Where(p => p.Enabled && p.Category == "Page").ToListAsync(ct))
        {
            try
            {
                var m = ManifestReader.Read(pkg.ManifestJson);
                if (!string.IsNullOrEmpty(m.EntryType)) usesByType[m.EntryType!] = m.Uses;
            }
            catch (JsonException) { }
        }

        // A page with no theme / plugin selection of its own renders its SITE's defaults (PageHost), so the
        // guard must see those inherited references too — otherwise the last enabled version of a site's
        // default theme or chrome plugin could be deleted out from under every page that inherits it.
        var pages = pageRows.Select(p =>
        {
            var sd = p.SiteId is int sid ? siteDefaults.GetValueOrDefault(sid) : default;
            var inheritsTheme = p.ThemeKey is null && !string.IsNullOrWhiteSpace(sd.ThemeKey);
            return new PageRef(
                p.Slug, p.BodyHtml,
                inheritsTheme ? sd.ThemeKey : p.ThemeKey,
                inheritsTheme ? sd.ThemeVersion : p.ThemeVersion,
                p.Enabled, p.IsPublished,
                p.Kind == PageKind.Code && p.ComponentTypeName is not null
                    && usesByType.TryGetValue(p.ComponentTypeName, out var u) ? u : null,
                string.IsNullOrWhiteSpace(p.ActivePluginsJson) ? sd.PluginsJson : p.ActivePluginsJson,
                p.IsDeleted);
        }).ToList();

        var otherEnabled = await db.ContentDefinitions
            .Where(d => d.Kind == kind && d.Key == key && d.Version != version && d.Enabled && d.IsActive && !d.IsShadowed)
            .Select(d => d.Version)
            .ToListAsync(ct);

        var blocking = FindBlockingPages(kind, key, version, pages, otherEnabled);
        if (blocking.Count > 0)
            return new DeleteGuardResult(false, true, false, blocking, $"Blocked: {blocking.Count} page(s) still use this version.");

        if (def.Origin == ContentOrigin.Compiled)
        {
            if (!def.Enabled)
                return new DeleteGuardResult(false, false, true, Array.Empty<string>(), "Already disabled; no change made.");
            def.Enabled = false;
            await db.SaveChangesAsync(ct);
            await discovery.ReloadCatalogAsync(ct);
            return new DeleteGuardResult(false, false, true, Array.Empty<string>(),
                "Compiled content can't be removed (discovery would re-add it); it was disabled instead.");
        }

        db.ContentDefinitions.Remove(def);
        await db.SaveChangesAsync(ct);
        await discovery.ReloadCatalogAsync(ct);
        return new DeleteGuardResult(true, false, false, Array.Empty<string>(), null);
    }

    /// <summary>Each site's default theme (key + version) and default plugin selection JSON, by site id.</summary>
    private static async Task<Dictionary<int, (string? ThemeKey, int? ThemeVersion, string? PluginsJson)>> LoadSiteDefaultsAsync(
        CmsDbContext db, CancellationToken ct)
    {
        var sites = await db.Sites.AsNoTracking()
            .Select(s => new { s.Id, s.DefaultThemeKey, s.DefaultThemeVersion }).ToListAsync(ct);
        var plugins = await db.Settings.AsNoTracking()
            .Where(s => s.Scope == "Site" && s.Key == "plugins.default" && s.ScopeId != null)
            .Select(s => new { s.ScopeId, s.Value }).ToListAsync(ct);
        var map = new Dictionary<int, (string?, int?, string?)>();
        foreach (var s in sites)
            map[s.Id] = (s.DefaultThemeKey, s.DefaultThemeVersion,
                plugins.FirstOrDefault(p => p.ScopeId == s.Id)?.Value);
        return map;
    }

    /// <summary>
    /// PURE delete-guard: the enabled+published pages that would be left invalid by deleting
    /// (kind,key,version). A pin on this exact version always blocks. A floating ("latest") reference
    /// blocks only when NO other enabled version would remain (otherwise it harmlessly floats to that one).
    /// </summary>
    public static IReadOnlyList<string> FindBlockingPages(
        ContentKind kind, string key, int version,
        IReadOnlyList<PageRef> pages, IReadOnlyList<int> otherEnabledVersions)
    {
        var floatingOrphans = otherEnabledVersions.Count == 0; // deleting `version` leaves nothing enabled
        var blocking = new List<string>();

        foreach (var p in pages)
        {
            if (p.IsDeleted || !p.Enabled || !p.IsPublished) continue;

            bool blocks;
            if (kind == ContentKind.Theme)
            {
                var keyMatch = string.Equals(p.ThemeKey, key, StringComparison.OrdinalIgnoreCase);
                var pinned = keyMatch && p.ThemeVersion == version;
                var floating = keyMatch && p.ThemeVersion is null && floatingOrphans;
                blocks = pinned || floating;
            }
            else // Plugin / Component: scan BodyHtml, compiled uses[], AND the per-page plugin selection
            {
                var activePlugins = kind == ContentKind.Plugin && p.ActivePluginsJson is { Length: > 0 } apj
                    ? TryDeserializePlugins(apj) : null;
                var pinned = IncludeReferenceParser.BodyPinsVersion(p.BodyHtml, kind, key, version)
                             || IncludeReferenceParser.UsesPinsVersion(p.Uses, kind, key, version)
                             // Versioned plugin refs in ActivePluginsJson (e.g. "Plugin.navmenu@1") must also block.
                             || IncludeReferenceParser.UsesPinsVersion(activePlugins, kind, key, version);
                // Parse the selection through the SAME uses[] grammar the pin check above uses, rather than
                // string-comparing against "Plugin.<key>": that ad-hoc compare missed every ref the grammar
                // accepts but the literal does not (a padded entry, an explicit "@latest" float), letting the
                // last enabled version be deleted out from under a page that still selects it.
                var activePluginFloating = IncludeReferenceParser.UsesFloatsKey(activePlugins, kind, key);
                var floating = floatingOrphans
                               && (IncludeReferenceParser.BodyFloatsKey(p.BodyHtml, kind, key)
                                   || IncludeReferenceParser.UsesFloatsKey(p.Uses, kind, key)
                                   || activePluginFloating);
                blocks = pinned || floating;
            }

            if (blocks) blocking.Add(p.Slug);
        }

        return blocking;
    }

    private static IReadOnlyList<string> TryDeserializePlugins(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }
}
