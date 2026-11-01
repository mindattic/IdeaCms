using NuGet.Versioning;

namespace IdeaCms.Core.Portability;

/// <summary>
/// Narrow seam over the real NuGet client (<c>NuGet.Protocol</c>'s <c>SourceRepository</c>/
/// <c>FindPackageByIdResource</c>) so <see cref="NuGetIdeaListPackageResolver"/>'s own logic — id/version
/// mapping, local caching, unzipping <c>content/{key}.idea</c> back out — is unit-testable against a fake
/// implementation, with no real network involved. The real implementation talks to GitHub Packages and is
/// verified by hand against a live feed rather than in the automated suite (GitHub Packages requires auth
/// even for its own service index, unlike nuget.org, which is a plausible failure mode worth isolating
/// behind exactly this kind of seam before depending on it in production).
/// </summary>
public interface INupkgFetcher
{
    /// <summary>Copies the exact-version .nupkg's bytes into <paramref name="destination"/> (positioned
    /// at 0 on return), or returns false if the feed has no such package/version.</summary>
    Task<bool> TryCopyNupkgAsync(string packageId, NuGetVersion version, Stream destination, CancellationToken ct = default);
}
