using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Portability;

namespace IdeaCms.Core.Services;

/// <summary>One version of one citizen published on the feed, for the admin catalog browser.</summary>
public sealed record CatalogEntry(ContentKind Kind, string Key, int Version, string PackageId, bool Installed);

/// <summary>
/// <paramref name="FeedConfigured"/> false means no org/PAT is set up (nothing to show, not an error).
/// <paramref name="Error"/> set means the feed call itself failed (a transient GitHub API hiccup) —
/// the caller should show it, not treat <see cref="Entries"/> as authoritative.
/// </summary>
public sealed record CatalogListResult(bool FeedConfigured, IReadOnlyList<CatalogEntry> Entries, string? Error)
{
    public static readonly CatalogListResult NotConfigured = new(false, [], null);
}

public interface IPackageCatalogService
{
    Task<CatalogListResult> ListAvailableAsync(CancellationToken ct = default);
}

/// <summary>
/// Lists what's published on the NuGet feed as an alternative to hunting down and uploading `.idea`
/// files by hand — the read side of the admin Catalog tab (install itself just reuses the existing
/// <see cref="IIdeaListPackageResolver"/>/<see cref="IPackageInstallService"/> pair, unchanged).
/// </summary>
public sealed class PackageCatalogService(IPackageCatalogFetcher fetcher, IDbContextFactory<CmsDbContext> factory)
    : IPackageCatalogService
{
    private const string IdPrefix = "IdeaCms.";

    public async Task<CatalogListResult> ListAvailableAsync(CancellationToken ct = default)
    {
        if (!fetcher.IsConfigured) return CatalogListResult.NotConfigured;

        List<(ContentKind Kind, string Key, int Version, string PackageId)> parsed;
        try
        {
            parsed = [];
            foreach (var packageId in await fetcher.ListPackageIdsAsync(ct))
            {
                if (!TryParsePackageId(packageId, out var kind, out var key)) continue;
                foreach (var versionText in await fetcher.ListVersionsAsync(packageId, ct))
                    if (int.TryParse(versionText.Split('.')[0], out var version))
                        parsed.Add((kind, key, version, packageId));
            }
        }
        catch (Exception ex)
        {
            return new CatalogListResult(true, [], ex.Message);
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var installed = await db.InstalledPackages.AsNoTracking()
            .Select(p => new { p.Category, p.Key, p.Version })
            .ToListAsync(ct);
        var installedSet = installed
            .Select(p => (Category: p.Category, Key: p.Key, p.Version))
            .ToHashSet();

        var entries = parsed
            .Select(p => new CatalogEntry(p.Kind, p.Key, p.Version, p.PackageId,
                installedSet.Contains((p.Kind.ToString(), p.Key, p.Version))))
            .OrderBy(e => e.Kind).ThenBy(e => e.Key).ThenByDescending(e => e.Version)
            .ToList();

        return new CatalogListResult(true, entries, null);
    }

    /// <summary>
    /// Reverse of the id <see cref="NuGetIdeaListPackageResolver"/> builds
    /// (<c>$"IdeaCms.{kind}.{key}"</c>) and <c>IdeaCms.Sdk</c>'s <c>ma-idea nupkg</c>
    /// publishes under. Strip the fixed prefix, split the remainder at the FIRST '.' (the kind), keep
    /// the rest as the key — a key may itself contain dots, same as
    /// <c>IncludeReferenceParser.TryParseTag</c>. An id that doesn't parse is skipped, not an error:
    /// the feed could in principle host something unrelated to this app.
    /// </summary>
    internal static bool TryParsePackageId(string packageId, out ContentKind kind, out string key)
    {
        kind = default; key = "";
        if (!packageId.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        var rest = packageId[IdPrefix.Length..];
        var dot = rest.IndexOf('.');
        if (dot <= 0 || dot >= rest.Length - 1) return false;
        if (!Enum.TryParse(rest[..dot], ignoreCase: true, out kind)) return false;

        key = rest[(dot + 1)..];
        return key.Length > 0;
    }
}
