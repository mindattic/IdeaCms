using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;

namespace IdeaCms.Core.Services;

/// <summary>A read-only summary of an installed .idea package, for the admin packages browser.</summary>
public sealed record PackageSummary(
    int Id,
    string Category,
    string Key,
    int Version,
    string DisplayName,
    bool Enabled,
    bool IsActiveVersion,
    DateTime InstalledUtc,
    string BlobPath,
    string Sha256);

public interface IPackageRegistryService
{
    Task<IReadOnlyList<PackageSummary>> ListAsync(CancellationToken ct = default);
}

public sealed class PackageRegistryService(IDbContextFactory<CmsDbContext> factory) : IPackageRegistryService
{
    public async Task<IReadOnlyList<PackageSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.InstalledPackages.AsNoTracking()
            .OrderBy(p => p.Category).ThenBy(p => p.Key).ThenByDescending(p => p.Version)
            .Select(p => new PackageSummary(
                p.Id, p.Category, p.Key, p.Version, p.DisplayName,
                p.Enabled, p.IsActiveVersion, p.InstalledUtc,
                p.BlobPath, p.Sha256))
            .ToListAsync(ct);
    }
}
