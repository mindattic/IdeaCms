namespace IdeaCms.Core.Portability;

/// <summary>
/// Narrow seam over GitHub's own REST API (not <c>NuGet.Protocol</c>'s search resource, which is
/// unreliable against GitHub Packages — the same reason <see cref="GitHubPackagesNupkgFetcher"/> only
/// ever does exact id+version lookups) so <see cref="Services.PackageCatalogService"/>'s own logic —
/// id parsing, installed cross-reference — is unit-testable against a fake, with no real network
/// involved. The real implementation is verified by hand against a live feed rather than in the
/// automated suite, same as <see cref="GitHubPackagesNupkgFetcher"/>.
/// </summary>
public interface IPackageCatalogFetcher
{
    /// <summary>False when no org/PAT is configured — callers must not call the API then.</summary>
    bool IsConfigured { get; }

    /// <summary>Every NuGet package id (<c>IdeaCms.{Category}.{Key}</c>) published under the feed's org.</summary>
    Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken ct = default);

    /// <summary>Every version string (e.g. <c>"1.0.0"</c>) published for one package id.</summary>
    Task<IReadOnlyList<string>> ListVersionsAsync(string packageId, CancellationToken ct = default);
}
