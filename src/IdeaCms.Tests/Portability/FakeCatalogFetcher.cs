using IdeaCms.Core.Portability;

namespace IdeaCms.Tests.Portability;

/// <summary>Test double for <see cref="IPackageCatalogFetcher"/> — hands back canned package ids/versions
/// with no network involved, so <see cref="Core.Services.PackageCatalogService"/>'s own logic (id
/// parsing, installed cross-reference) is unit-testable independent of a real feed.</summary>
internal sealed class FakeCatalogFetcher : IPackageCatalogFetcher
{
    public bool IsConfigured { get; set; } = true;
    public Exception? ThrowOnListPackageIds { get; set; }

    private readonly Dictionary<string, List<string>> _versionsByPackageId = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string packageId, params string[] versions) => _versionsByPackageId[packageId] = [.. versions];

    public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken ct = default)
    {
        if (ThrowOnListPackageIds is { } ex) throw ex;
        return Task.FromResult<IReadOnlyList<string>>(_versionsByPackageId.Keys.ToList());
    }

    public Task<IReadOnlyList<string>> ListVersionsAsync(string packageId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(
            _versionsByPackageId.TryGetValue(packageId, out var v) ? v : []);
}
