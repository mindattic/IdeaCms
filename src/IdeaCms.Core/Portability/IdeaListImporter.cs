using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Rendering;
using IdeaCms.Core.Services;
using MindAttic.Media;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Core.Portability;

/// <summary>How an import should behave. The defaults are what `--import-content` (now `--import-idealist`) always did.</summary>
public sealed record IdeaListImportOptions
{
    /// <summary>Report what would happen and write nothing.</summary>
    public bool DryRun { get; init; }

    /// <summary>Force every page to <see cref="ContentTrust.Untrusted"/> — for a list that is not yours.</summary>
    public bool ForceUntrusted { get; init; }

    /// <summary>Soft-delete pages on the target site that the idealist does not mention.</summary>
    public bool Prune { get; init; }

    /// <summary>Site key to import into, overriding the one the idealist names. Null uses the idealist's.</summary>
    public string? IntoSiteKey { get; init; }
}

/// <summary>What an import did. Counts are reported even for a dry run.</summary>
public sealed record IdeaListImportResult(
    bool Ok, string? Error,
    int PackagesInstalled,
    int PagesCreated, int PagesUpdated, int PagesPruned,
    int MediaUploaded, int MediaReused, int MediaFailed,
    int ComponentMetadata, int MediaReferencesRemapped)
{
    public static IdeaListImportResult Failed(string error) => new(false, error, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Thrown when a <see cref="IdeaList.Packages"/> entry cannot be resolved/installed, or a page's <see
/// cref="IdeaListPage.Uses"/> cannot be satisfied once Packages[] finishes installing. Carries every
/// failing reason, not just the first, so an operator sees the whole picture in one run. Nothing is
/// written when this is thrown.
/// </summary>
public sealed class IdeaListImportException(string message, IReadOnlyList<string> reasons) : Exception(message)
{
    public IReadOnlyList<string> Reasons { get; } = reasons;
}

/// <summary>
/// Applies an <see cref="IdeaList"/> to this environment — the work behind the `--import-idealist` CLI
/// verb and boot-time provisioning (<c>BootProvisioning</c>), which are argument parsing / console
/// reporting over it.
/// <para>
/// Packages install first, in listed order (§0), then every page's declared <c>Uses[]</c> is validated
/// against the catalog (§1) before anything else is written — a curated instance should render
/// correctly on first boot, not degrade to a placeholder. Everything from §2 on is the unchanged
/// content-import algorithm (M.IDEA-§4.9) over <see cref="IdeaList"/> types.
/// </para>
/// <para>
/// Re-runnable by construction. Pages reconcile on <c>Uid</c> first and <c>(SiteId, Slug)</c> second, both
/// WITHIN the target site; the slug fallback is what lets an idealist land on a database seeded
/// independently, where <c>frontpage</c> already exists under a different uid.
/// </para>
/// <para>
/// Media is uploaded through <see cref="IMediaStore"/>, which mints the uid, so imported items get new
/// ones and every <c>/_media/{uid}</c> reference is rewritten through an old→new map. Identical bytes
/// (matched by SHA-256) are adopted rather than re-uploaded, so a second import moves no payloads.
/// </para>
/// </summary>
public sealed class IdeaListImporter(
    CmsDbContext db, IMediaStore media,
    IPackageInstallService packages, IIdeaListPackageResolver packageResolver)
{
    /// <summary>Progress lines. The CLI prints them; boot provisioning logs them.</summary>
    public delegate void Log(string message, bool isError = false);

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Read an idealist manifest out of an open archive, or explain why it cannot be read.</summary>
    public static async Task<(IdeaList? List, string? Error)> ReadManifestAsync(ZipArchive zip)
    {
        var entry = zip.GetEntry(IdeaList.ManifestEntryName);
        if (entry is null)
            return (null, $"Not an idealist: no {IdeaList.ManifestEntryName} inside the archive.");

        IdeaList? list;
        try
        {
            await using var ms = entry.Open();
            list = await JsonSerializer.DeserializeAsync<IdeaList>(ms, JsonOpts);
        }
        catch (JsonException ex) { return (null, $"idealist manifest could not be read: {ex.Message}"); }

        if (list is null) return (null, "idealist manifest could not be read.");
        if (list.FormatVersion > IdeaList.CurrentFormatVersion)
            return (null, $"idealist format v{list.FormatVersion} is newer than this host understands (v{IdeaList.CurrentFormatVersion}).");
        return (list, null);
    }

    public async Task<IdeaListImportResult> ImportAsync(
        ZipArchive zip, IdeaList list, IdeaListImportOptions options, Log log, CancellationToken ct = default)
    {
        var dryRun = options.DryRun;

        // ---- 0. Packages, in listed order. Each install stays individually idempotent (PackageInstallService's
        // existing NoOp/allowOverride semantics), so a partial prefix from a previous failed run is safe to
        // re-apply. A failure here throws IMMEDIATELY — no further packages, no pages, this run. ----
        var installed = 0;
        foreach (var entry in list.Packages)
        {
            if (!IncludeReferenceParser.TryParseUse(entry, out var kind, out var key, out var version) || version is null)
                throw new IdeaListImportException(
                    $"Packages[] entry '{entry}' is not a valid pinned reference (expected 'Kind.key@version').", [entry]);

            if (dryRun) { log($"  [DRY] would install {entry}"); installed++; continue; }

            var bytes = await packageResolver.ResolveAsync(kind, key, version.Value, ct);
            if (bytes is null)
                throw new IdeaListImportException(
                    $"Packages[] entry '{entry}' could not be resolved by the configured package resolver.",
                    [entry]);

            await using (bytes)
            {
                try { await packages.InstallAsync(bytes, allowOverride: false, ct); installed++; log($"  + {entry}"); }
                catch (InstallException ex)
                {
                    throw new IdeaListImportException($"installing '{entry}' failed: {ex.Message}", [entry]);
                }
            }
        }
        if (list.Packages.Count > 0) log($"packages: {installed} installed/verified.");

        // ---- 1. Pre-flight: EVERY page's Uses[] against the catalog, collected in full, BEFORE any
        // media/site/settings/page/component-metadata row is touched. There is no DB transaction wrapping
        // this (neither EF InMemory nor the prior content-bundle importer used one), so "no partial apply"
        // is enforced by ordering the destructive work behind this pure read-only pre-flight, not by
        // inventing transactional machinery this codebase didn't already have. ----
        await ValidateUsesAsync(list, dryRun, ct);

        // ---- 2. media, and the uid map every later rewrite depends on -------------------------
        var uidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var existingByHash = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in await media.ListAsync())
            if (!string.IsNullOrEmpty(m.Sha256))
                existingByHash.TryAdd(m.Sha256, m.Uid);

        int uploaded = 0, reused = 0, mediaFailures = 0;
        foreach (var lm in list.Media)
        {
            if (!string.IsNullOrEmpty(lm.Sha256) && existingByHash.TryGetValue(lm.Sha256, out var already))
            {
                if (already != lm.SourceUid) uidMap[lm.SourceUid.ToString("D")] = already.ToString("D");
                reused++;
                continue;
            }

            var entry = zip.GetEntry(lm.EntryName);
            if (entry is null)
            {
                log($"  ! {lm.FileName}: payload {lm.EntryName} missing from the archive", true);
                mediaFailures++;
                continue;
            }

            if (dryRun)
            {
                log($"  [DRY] would upload {lm.FileName} ({Describe(lm.SizeBytes)})");
                uploaded++;
                continue;
            }

            try
            {
                await using var payload = entry.Open();
                var item = await media.UploadAsync(payload, lm.FileName, lm.ContentType,
                    folder: lm.Folder, mediaType: lm.MediaType,
                    width: lm.Width, height: lm.Height, notes: lm.Notes);
                if (item.Uid != lm.SourceUid) uidMap[lm.SourceUid.ToString("D")] = item.Uid.ToString("D");
                if (!string.IsNullOrEmpty(item.Sha256)) existingByHash.TryAdd(item.Sha256, item.Uid);
                uploaded++;
            }
            catch (Exception ex)
            {
                log($"  ! {lm.FileName}: {ex.Message}", true);
                mediaFailures++;
            }
        }
        log($"media: {uploaded} uploaded, {reused} already present, {mediaFailures} failed.");

        // ---- 3. site -------------------------------------------------------------------------
        Site? site;
        if (list.Site is { } ls)
        {
            // Match on the site KEY, and CREATE when it is absent rather than falling back to the
            // default site. A deployment can host several domains (M.IDEA-§4.10), so quietly redirecting
            // another site's pages onto the default one would republish them under the wrong domain.
            var targetKey = (options.IntoSiteKey ?? ls.Key).Trim().ToLowerInvariant();
            site = await db.Sites.FirstOrDefaultAsync(s => s.Key == targetKey, ct);
            if (site is null)
            {
                var anySites = await db.Sites.AnyAsync(ct);
                log($"no site keyed \"{targetKey}\" here — creating it"
                    + (anySites ? " (it will NOT become the default)." : " as the default site."));
                site = new Site { Key = targetKey, IsDefault = !anySites, CreatedUtc = DateTime.UtcNow };
                if (!dryRun) db.Sites.Add(site);
            }
            site.Name = ls.Name;
            site.HostBindings = ls.HostBindings;
            site.DefaultThemeKey = ls.DefaultThemeKey;
            site.DefaultThemeVersion = ls.DefaultThemeVersion;
            site.DefaultThemeMode = ls.DefaultThemeMode;
            site.SettingsJson = ls.SettingsJson;
            site.ModifiedUtc = DateTime.UtcNow;
            if (!dryRun) await db.SaveChangesAsync(ct);
        }
        else
        {
            site = await db.Sites.OrderBy(s => s.IsDefault ? 0 : 1).ThenBy(s => s.Id).FirstOrDefaultAsync(ct);
        }
        var siteId = site?.Id;

        // ---- 4. settings (values can carry /_media urls, so they are rewritten too) -----------
        foreach (var s in list.Settings)
        {
            var scopeId = s.Scope == "Site" ? siteId : null;
            var row = await db.Settings.FirstOrDefaultAsync(
                x => x.Scope == s.Scope && x.ScopeId == scopeId && x.Key == s.Key, ct);
            var value = Remap(s.Value, uidMap);
            if (row is null)
            {
                if (!dryRun) db.Settings.Add(new SettingEntry { Scope = s.Scope, ScopeId = scopeId, Key = s.Key, Value = value });
            }
            else row.Value = value;
        }
        if (!dryRun) await db.SaveChangesAsync(ct);

        // ---- 5. pages ------------------------------------------------------------------------
        var authorTrusted = list.Pages.Count(p => string.Equals(p.BodyTrust, "Author", StringComparison.OrdinalIgnoreCase));
        if (authorTrusted > 0)
        {
            log(options.ForceUntrusted
                ? $"--untrusted: {authorTrusted} Author-trust page(s) will be imported as Untrusted (bodies get sanitized; component tags will NOT render)."
                : $"{authorTrusted} page(s) import with Author trust — their HTML/JS is written VERBATIM and rendered unsanitized. "
                  + "Run with --untrusted if this idealist did not come from you.");
        }

        int created = 0, updated = 0;
        var touched = new Dictionary<Guid, CmsPage>();

        foreach (var lp in list.Pages)
        {
            // BOTH lookups are scoped to the target site. Uid is portable and therefore GLOBAL, so an
            // unscoped uid match would adopt ANOTHER site's page and re-point its SiteId: importing an
            // idealist with --into-site, on a deployment that already holds those pages under a different
            // site, would MOVE them rather than copy them (M.IDEA-§4.9).
            var page = await db.Pages.IgnoreQueryFilters().Include(p => p.MetaTags)
                           .FirstOrDefaultAsync(p => p.Uid == lp.Uid && p.SiteId == siteId, ct)
                       ?? await db.Pages.IgnoreQueryFilters().Include(p => p.MetaTags)
                           .FirstOrDefaultAsync(p => p.SiteId == siteId && p.Slug == lp.Slug, ct);

            var isNew = page is null;
            if (page is null)
            {
                // A uid is unique across the deployment, so a page landing in a DIFFERENT site than one
                // that already holds this uid needs its own — a copy of the idealist's page, not a move.
                var uidTaken = await db.Pages.IgnoreQueryFilters().AnyAsync(p => p.Uid == lp.Uid, ct);
                page = new CmsPage { Uid = uidTaken ? Guid.NewGuid() : lp.Uid, CreatedUtc = DateTime.UtcNow };
                if (!dryRun) db.Pages.Add(page);
                created++;
            }
            else updated++;

            page.SiteId = siteId;
            page.Slug = lp.Slug;
            page.Title = lp.Title;
            page.SeoTitle = lp.SeoTitle;
            page.Kind = Enum.TryParse<PageKind>(lp.Kind, ignoreCase: true, out var k) ? k : PageKind.Data;
            page.BodyHtml = Remap(lp.BodyHtml, uidMap);
            page.PageCss = Remap(lp.PageCss, uidMap);
            page.PageJs = Remap(lp.PageJs, uidMap);
            page.BodyTrust = !options.ForceUntrusted
                             && string.Equals(lp.BodyTrust, "Author", StringComparison.OrdinalIgnoreCase)
                ? ContentTrust.Author
                : ContentTrust.Untrusted;
            if (page.AuthorTrustVersion == 0) page.AuthorTrustVersion = 1;
            page.ThemeKey = lp.ThemeKey;
            page.ThemeVersion = lp.ThemeVersion;
            page.ActivePluginsJson = lp.ActivePluginsJson;
            page.ComponentTypeName = lp.ComponentTypeName;
            page.AssemblyName = lp.AssemblyName;
            page.SettingsJson = lp.SettingsJson;
            page.IsPublished = lp.IsPublished;
            page.Enabled = lp.Enabled;
            page.IsRestricted = lp.IsRestricted;
            page.OpenInNewWindow = lp.OpenInNewWindow;
            page.SortOrder = lp.SortOrder;
            page.WorkflowState = lp.WorkflowState;
            page.ModifiedUtc = DateTime.UtcNow;
            // An idealist is an authoritative statement about the page, so a row that was soft-deleted
            // here comes back rather than staying invisible under a live slug.
            page.IsDeleted = false;
            page.DeletedUtc = null;

            if (!dryRun)
            {
                await db.SaveChangesAsync(ct);

                // Meta tags: the idealist is the whole truth for a page, so replace rather than merge.
                var existingTags = await db.PageMetaTags.Where(t => t.PageId == page.Id).ToListAsync(ct);
                db.PageMetaTags.RemoveRange(existingTags);
                foreach (var (name, content) in lp.MetaTags)
                    db.PageMetaTags.Add(new PageMetaTag { PageId = page.Id, Name = name, Content = content });

                var existingRoles = await db.PageRoleAccess.Where(r => r.PageId == page.Id).ToListAsync(ct);
                db.PageRoleAccess.RemoveRange(existingRoles);
                foreach (var role in lp.RoleAccess.Distinct(StringComparer.OrdinalIgnoreCase))
                    db.PageRoleAccess.Add(new PageRoleAccess { PageId = page.Id, RoleName = role });

                foreach (var alias in lp.SlugHistory)
                {
                    var already = await db.PageSlugHistory
                        .AnyAsync(h => h.PageId == page.Id && h.OldSlug == alias.OldSlug, ct);
                    if (!already)
                        db.PageSlugHistory.Add(new PageSlugHistory
                        {
                            PageId = page.Id, OldSlug = alias.OldSlug, IsVanity = alias.IsVanity,
                            CreatedUtc = DateTime.UtcNow,
                        });
                }

                // Instance settings (M.IDEA-§4.5): upsert each slot, snapshotting a changed row into its history
                // exactly as IWidgetInstanceSettingsService would, so an import is rollback-able too.
                if (lp.InstanceSettings is { } listedSlots)
                {
                    var keep = listedSlots.Select(s => s.Slot).ToHashSet(StringComparer.Ordinal);
                    var stale = await db.WidgetPlacementSettings
                        .Where(s => s.PageId == page.Id).ToListAsync(ct);
                    db.WidgetPlacementSettings.RemoveRange(stale.Where(s => !keep.Contains(s.SlotName)));
                }
                foreach (var slot in (lp.InstanceSettings ?? []).Where(s => s.Slot.Length > 0))
                {
                    var row = await db.WidgetPlacementSettings
                        .FirstOrDefaultAsync(s => s.PageId == page.Id && s.SlotName == slot.Slot, ct);
                    var now = DateTime.UtcNow;
                    if (row is null)
                    {
                        db.WidgetPlacementSettings.Add(new WidgetPlacementSettings
                        {
                            PageId = page.Id, SlotName = slot.Slot, WidgetRef = slot.WidgetRef,
                            SettingsJson = slot.SettingsJson, SettingsVersion = 1, CreatedUtc = now, ModifiedUtc = now,
                        });
                    }
                    else if (row.SettingsJson != slot.SettingsJson || row.WidgetRef != slot.WidgetRef)
                    {
                        db.WidgetPlacementSettingsHistory.Add(new WidgetPlacementSettingsHistory
                        {
                            PlacementSettingsId = row.Id, WidgetRef = row.WidgetRef, SettingsJson = row.SettingsJson,
                            SettingsVersion = row.SettingsVersion, SavedUtc = now,
                        });
                        row.WidgetRef = slot.WidgetRef;
                        row.SettingsJson = slot.SettingsJson;
                        row.SettingsVersion++;
                        row.ModifiedUtc = now;
                    }
                }
                await db.SaveChangesAsync(ct);
            }

            touched[lp.Uid] = page;
            log($"  {(isNew ? "+" : "~")} /{lp.Slug}");
        }

        // ---- 6. parents, once every page has an id ------------------------------------------
        if (!dryRun)
        {
            foreach (var lp in list.Pages)
            {
                if (lp.ParentUid is not { } parentUid) continue;
                if (!touched.TryGetValue(lp.Uid, out var child)) continue;
                // Scoped to the site for the same reason the page lookup is: a parent is a page.
                var parent = touched.TryGetValue(parentUid, out var p)
                    ? p
                    : await db.Pages.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.Uid == parentUid && x.SiteId == siteId, ct);
                child.ParentId = parent?.Id;
            }
            await db.SaveChangesAsync(ct);
        }

        // ---- 7. per-component metadata -------------------------------------------------------
        int meta = 0;
        foreach (var lcm in list.ComponentMetadata)
        {
            // The idealist's page uid may have been adopted onto a row that already had a different one;
            // follow the page we actually wrote.
            var pageUid = touched.TryGetValue(lcm.PageUid, out var pg) ? pg.Uid : lcm.PageUid;
            if (dryRun) { meta++; continue; }

            var row = await db.ComponentMetadata.FirstOrDefaultAsync(
                x => x.PageUid == pageUid && x.ComponentKey == lcm.ComponentKey && x.SlotName == lcm.SlotName, ct);
            if (row is null)
            {
                db.ComponentMetadata.Add(new ComponentMetadata
                {
                    PageUid = pageUid, ComponentKey = lcm.ComponentKey, SlotName = lcm.SlotName,
                    MetadataJson = Remap(lcm.MetadataJson, uidMap) ?? "{}",
                    CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
                });
            }
            else
            {
                row.MetadataJson = Remap(lcm.MetadataJson, uidMap) ?? "{}";
                row.ModifiedUtc = DateTime.UtcNow;
            }
            meta++;
        }
        if (!dryRun) await db.SaveChangesAsync(ct);

        // ---- 8. optional prune ---------------------------------------------------------------
        int pruned = 0;
        if (options.Prune)
        {
            var keep = touched.Values.Select(p => p.Id).ToHashSet();
            var strays = await db.Pages.Where(p => p.SiteId == siteId && !keep.Contains(p.Id)).ToListAsync(ct);
            foreach (var stray in strays)
            {
                log($"  - /{stray.Slug} (not in idealist)");
                if (!dryRun) { stray.IsDeleted = true; stray.DeletedUtc = DateTime.UtcNow; }
                pruned++;
            }
            if (!dryRun) await db.SaveChangesAsync(ct);
        }

        log($"pages: {created} created, {updated} updated"
            + (options.Prune ? $", {pruned} soft-deleted" : "")
            + $"; {meta} component metadata row(s); {uidMap.Count} media reference(s) remapped.");

        return new IdeaListImportResult(
            Ok: mediaFailures == 0, Error: null,
            PackagesInstalled: installed,
            PagesCreated: created, PagesUpdated: updated, PagesPruned: pruned,
            MediaUploaded: uploaded, MediaReused: reused, MediaFailed: mediaFailures,
            ComponentMetadata: meta, MediaReferencesRemapped: uidMap.Count);
    }

    /// <summary>
    /// Every page's Uses[] must resolve against the catalog once Packages[] has finished installing (or,
    /// for a dry run's preview, against Packages[] itself — a real run would have installed it first).
    /// Collects every unmet entry across every page before throwing, so an operator sees the whole
    /// picture in one run rather than one error at a time.
    /// </summary>
    private async Task ValidateUsesAsync(IdeaList list, bool dryRun, CancellationToken ct)
    {
        var declaredInThisList = IncludeReferenceParser.ParseUses(list.Packages);
        var reasons = new List<string>();

        foreach (var page in list.Pages)
        {
            foreach (var u in page.Uses)
            {
                if (!IncludeReferenceParser.TryParseUse(u, out var kind, out var key, out var version))
                {
                    reasons.Add($"/{page.Slug}: Uses[] entry '{u}' is unparseable (expected 'Kind.key@version')");
                    continue;
                }

                var satisfiedByCatalog = await db.ContentDefinitions.AnyAsync(c =>
                    c.Kind == kind && c.Key == key && c.Enabled && c.IsActive
                    && (version == null || c.Version == version), ct);

                var satisfiedByPlan = dryRun && declaredInThisList.Any(p =>
                    p.Kind == kind && p.Key == key && (version == null || p.Version == version));

                if (!satisfiedByCatalog && !satisfiedByPlan)
                    reasons.Add($"/{page.Slug}: Uses[] entry '{u}' is not installed/enabled");
            }
        }

        if (reasons.Count > 0)
            throw new IdeaListImportException($"{reasons.Count} page Uses[] entrie(s) unmet — nothing was written.", reasons);
    }

    private static readonly System.Text.RegularExpressions.Regex UidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Rewrites every media uid the import remapped. Uids are hyphenated GUIDs, so a plain substring
    /// swap is unambiguous and catches all three shapes at once: <c>/_media/{uid}</c>,
    /// <c>&lt;Component.MediaImage uid="{uid}"&gt;</c>, and a uid inside a component's metadata JSON.
    /// </summary>
    internal static string? Remap(string? text, IReadOnlyDictionary<string, string> uidMap)
    {
        if (string.IsNullOrEmpty(text) || uidMap.Count == 0) return text;
        // ONE pass over every uid in the text: replacing map entries one after another chained them, so
        // when one item's NEW uid was another item's OLD uid (an adopted-by-hash local item whose uid is
        // also a source uid in this idealist) its references were rewritten twice, onto the wrong media.
        return UidPattern.Replace(text, m =>
            uidMap.TryGetValue(m.Value, out var mapped) || uidMap.TryGetValue(m.Value.ToLowerInvariant(), out mapped)
                ? mapped : m.Value);
    }

    internal static string Describe(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024):F1} MB",
    };
}
