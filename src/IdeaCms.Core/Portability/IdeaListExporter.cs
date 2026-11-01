using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Rendering;
using MindAttic.Media;

namespace IdeaCms.Core.Portability;

/// <summary>How an export should gather and (unless <c>zip</c> is null — a dry run) write an idealist.</summary>
public sealed record IdeaListExportOptions
{
    /// <summary>False for a packages-only, content-free idealist (<c>--compose-idealist</c> with no
    /// <c>--from-site</c>): no site/settings/pages/media are gathered at all.</summary>
    public bool IncludeSiteContent { get; init; } = true;

    public string? SlugPrefix { get; init; }
    public bool IncludeMedia { get; init; } = true;

    /// <summary>Explicit "Kind.key@version" entries to union into Packages[] alongside whatever
    /// auto-discovery finds from the exported pages' bodies/theme/active plugins.</summary>
    public IReadOnlyCollection<string> ExtraPackages { get; init; } = [];
}

/// <summary>What an export gathered (and, unless it was a dry run, wrote).</summary>
public sealed record IdeaListExportResult(
    bool Ok, string? Error, IdeaList List,
    int MediaUploaded, int MediaFailed,
    IReadOnlyCollection<string> UnresolvedReferences);

/// <summary>
/// Gathers an <see cref="IdeaList"/> from this environment — the work behind the
/// <c>--export-idealist</c> and <c>--compose-idealist --from-site</c> CLI verbs, which are argument
/// parsing and console reporting over it (mirroring the existing importer/CLI split). Auto-discovers
/// <see cref="IdeaList.Packages"/> and each page's <see cref="IdeaListPage.Uses"/> by walking every
/// exported page's body/theme/active-plugins for citizen references and pinning each to its currently
/// active+enabled version — a reference that resolves to nothing installed is a warning, never a
/// hard failure; only import/boot enforces those.
/// <para>
/// <paramref name="zip"/> in <see cref="ExportAsync"/> is nullable: null means a dry run — nothing is
/// streamed or written, only counted, matching what <c>--dry-run</c> always did.
/// </para>
/// </summary>
public sealed class IdeaListExporter(CmsDbContext db, IMediaStore media)
{
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<IdeaListExportResult> ExportAsync(
        ZipArchive? zip, int? siteId, IdeaListExportOptions options, IdeaListImporter.Log log, CancellationToken ct = default)
    {
        var dryRun = zip is null;
        var list = new IdeaList { ExportedUtc = DateTime.UtcNow, ExportedFrom = Environment.MachineName };
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (options.IncludeSiteContent)
        {
            var site = siteId is { } id ? await db.Sites.FindAsync([id], ct) : null;
            list.Site = site is null ? null : new IdeaListSite
            {
                Key = site.Key,
                Name = site.Name,
                HostBindings = site.HostBindings,
                DefaultThemeKey = site.DefaultThemeKey,
                DefaultThemeVersion = site.DefaultThemeVersion,
                DefaultThemeMode = site.DefaultThemeMode,
                SettingsJson = site.SettingsJson,
            };

            // Host- and Site-scope settings only: a Page-scope entry keys on a local page id, and a page
            // already carries everything it needs on its own row.
            list.Settings = await db.Settings
                .Where(s => s.Scope == "Host" || (s.Scope == "Site" && s.ScopeId == siteId))
                .OrderBy(s => s.Scope).ThenBy(s => s.Key)
                .Select(s => new IdeaListSetting { Scope = s.Scope, Key = s.Key, Value = s.Value })
                .ToListAsync(ct);

            // Only this site's pages travel — otherwise a multi-domain deployment would export every
            // domain's content into a list that claims to be one site's.
            var pageQuery = db.Pages.Include(p => p.MetaTags).Where(p => p.SiteId == siteId);
            if (!string.IsNullOrWhiteSpace(options.SlugPrefix))
                pageQuery = pageQuery.Where(p => p.Slug.StartsWith(options.SlugPrefix));

            var pages = await pageQuery.OrderBy(p => p.Slug).ToListAsync(ct);
            var pageIds = pages.Select(p => p.Id).ToHashSet();
            var uidById = pages.ToDictionary(p => p.Id, p => p.Uid);

            var roleAccess = await db.PageRoleAccess.Where(r => pageIds.Contains(r.PageId)).ToListAsync(ct);
            var slugHistory = await db.PageSlugHistory.Where(h => pageIds.Contains(h.PageId)).ToListAsync(ct);
            var instanceSlots = await db.WidgetPlacementSettings.Where(s => pageIds.Contains(s.PageId)).ToListAsync(ct);

            foreach (var p in pages)
            {
                var lp = new IdeaListPage
                {
                    Uid = p.Uid,
                    Slug = p.Slug,
                    Title = p.Title,
                    SeoTitle = p.SeoTitle,
                    // A parent outside the exported set cannot be expressed, so the child imports as a root.
                    ParentUid = p.ParentId is { } pid && uidById.TryGetValue(pid, out var pu) ? pu : null,
                    Kind = p.Kind.ToString(),
                    BodyHtml = p.BodyHtml,
                    PageCss = p.PageCss,
                    PageJs = p.PageJs,
                    BodyTrust = p.BodyTrust.ToString(),
                    ThemeKey = p.ThemeKey,
                    ThemeVersion = p.ThemeVersion,
                    ActivePluginsJson = p.ActivePluginsJson,
                    ComponentTypeName = p.ComponentTypeName,
                    AssemblyName = p.AssemblyName,
                    SettingsJson = p.SettingsJson,
                    IsPublished = p.IsPublished,
                    Enabled = p.Enabled,
                    IsRestricted = p.IsRestricted,
                    OpenInNewWindow = p.OpenInNewWindow,
                    SortOrder = p.SortOrder,
                    WorkflowState = p.WorkflowState,
                    MetaTags = p.MetaTags.ToDictionary(t => t.Name, t => t.Content),
                    RoleAccess = roleAccess.Where(r => r.PageId == p.Id).Select(r => r.RoleName).ToList(),
                    SlugHistory = slugHistory.Where(h => h.PageId == p.Id)
                        .Select(h => new IdeaListSlugAlias { OldSlug = h.OldSlug, IsVanity = h.IsVanity })
                        .ToList(),
                    InstanceSettings = instanceSlots.Where(s => s.PageId == p.Id)
                        .OrderBy(s => s.SlotName, StringComparer.Ordinal)
                        .Select(s => new IdeaListInstanceSettings { Slot = s.SlotName, WidgetRef = s.WidgetRef, SettingsJson = s.SettingsJson })
                        .ToList(),
                };
                lp.Uses = await DiscoverUsesAsync(lp.BodyHtml, lp.ThemeKey, lp.ThemeVersion, lp.ActivePluginsJson, unresolved, ct);
                list.Pages.Add(lp);
            }

            var pageUids = pages.Select(p => p.Uid).ToHashSet();
            list.ComponentMetadata = (await db.ComponentMetadata.ToListAsync(ct))
                .Where(m => pageUids.Contains(m.PageUid))
                .Select(m => new IdeaListComponentMetadata
                {
                    PageUid = m.PageUid, ComponentKey = m.ComponentKey, SlotName = m.SlotName, MetadataJson = m.MetadataJson,
                })
                .ToList();
        }

        // Packages[] = every page's auto-discovered Uses[] (deduped, order preserved) unioned with
        // whatever was named explicitly (e.g. --package on --compose-idealist).
        var packages = new List<string>();
        foreach (var lp in list.Pages)
            foreach (var u in lp.Uses)
                if (!packages.Contains(u, StringComparer.OrdinalIgnoreCase)) packages.Add(u);
        foreach (var extra in options.ExtraPackages)
            if (!packages.Contains(extra, StringComparer.OrdinalIgnoreCase)) packages.Add(extra);
        list.Packages = packages;

        foreach (var w in unresolved)
            log($"  ! {w}: referenced but not installed/enabled — omitted from Packages[]", true);

        // ---- media -----------------------------------------------------------------------------
        int mediaUploaded = 0, mediaFailed = 0;
        if (options.IncludeSiteContent && options.IncludeMedia)
        {
            var items = await media.ListAsync();
            foreach (var item in items)
            {
                var entryName = IdeaList.MediaFolder + item.Uid.ToString("D") + ExtensionFor(item.FileName, item.ContentType);
                if (dryRun) { log($"  [DRY] would embed media {item.FileName} ({Describe(item.SizeBytes)})"); mediaUploaded++; continue; }

                try
                {
                    var fetched = await media.GetAsync(item.Uid, ct);
                    if (fetched is null)
                    {
                        log($"  ! {item.FileName}: no payload in the store, skipped", true);
                        mediaFailed++;
                        continue;
                    }
                    await using var content = fetched.Value.Content;
                    var entry = zip!.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await content.CopyToAsync(entryStream, ct);

                    list.Media.Add(new IdeaListMedia
                    {
                        SourceUid = item.Uid, FileName = item.FileName, ContentType = item.ContentType,
                        Folder = item.Folder, MediaType = item.MediaType, SizeBytes = item.SizeBytes,
                        Sha256 = item.Sha256, Width = item.Width, Height = item.Height, Notes = item.Notes,
                        EntryName = entryName,
                    });
                    mediaUploaded++;
                }
                catch (Exception ex)
                {
                    log($"  ! {item.FileName}: {ex.Message}", true);
                    mediaFailed++;
                }
            }
        }

        // The manifest is written LAST so it only ever lists media that actually made it into the archive.
        if (!dryRun)
        {
            var manifest = zip!.CreateEntry(IdeaList.ManifestEntryName, CompressionLevel.Optimal);
            await using var ms = manifest.Open();
            await JsonSerializer.SerializeAsync(ms, list, JsonOpts, ct);
        }

        return new IdeaListExportResult(
            Ok: mediaFailed == 0, Error: null, List: list,
            MediaUploaded: mediaUploaded, MediaFailed: mediaFailed,
            UnresolvedReferences: unresolved);
    }

    private async Task<List<string>> DiscoverUsesAsync(
        string? bodyHtml, string? themeKey, int? themeVersion, string? activePluginsJson,
        HashSet<string> unresolved, CancellationToken ct)
    {
        var uses = new List<string>();

        foreach (var (kind, key, version) in IncludeReferenceParser.Parse(bodyHtml))
            await AddPinnedAsync(kind, key, version, uses, unresolved, ct);

        if (!string.IsNullOrWhiteSpace(themeKey))
            await AddPinnedAsync(ContentKind.Theme, themeKey, themeVersion, uses, unresolved, ct);

        if (!string.IsNullOrWhiteSpace(activePluginsJson))
        {
            List<string>? plugins = null;
            try { plugins = JsonSerializer.Deserialize<List<string>>(activePluginsJson); } catch (JsonException) { /* malformed — ignore */ }
            foreach (var p in plugins ?? [])
                if (IncludeReferenceParser.TryParseUse(p, out var pk, out var pkey, out var pver))
                    await AddPinnedAsync(pk, pkey, pver, uses, unresolved, ct);
        }

        return uses;
    }

    private async Task AddPinnedAsync(
        ContentKind kind, string key, int? version, List<string> uses, HashSet<string> unresolved, CancellationToken ct)
    {
        int? pinned;
        if (version is { } v)
        {
            // Already pinned by the author (an explicit data-version, an "@n" in ActivePluginsJson, or
            // ThemeVersion) — still verify it, rather than trusting a page's own metadata: a page can
            // name a theme/plugin/component version that was never actually installed here.
            var exists = await db.ContentDefinitions.AnyAsync(
                c => c.Kind == kind && c.Key == key && c.Enabled && c.IsActive && c.Version == v, ct);
            pinned = exists ? v : null;
        }
        else
        {
            pinned = await db.ContentDefinitions
                .Where(c => c.Kind == kind && c.Key == key && c.Enabled && c.IsActive)
                .Select(c => (int?)c.Version)
                .FirstOrDefaultAsync(ct);
        }

        if (pinned is null) { unresolved.Add($"{kind}.{key}"); return; }

        var reference = $"{kind}.{key}@{pinned}";
        if (!uses.Contains(reference, StringComparer.OrdinalIgnoreCase)) uses.Add(reference);
    }

    private static string ExtensionFor(string fileName, string contentType)
    {
        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(ext)) return ext;
        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "video/mp4" => ".mp4",
            "application/pdf" => ".pdf",
            _ => ".bin",
        };
    }

    public static string Describe(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };
}
