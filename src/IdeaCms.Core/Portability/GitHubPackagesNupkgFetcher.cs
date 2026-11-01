using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace IdeaCms.Core.Portability;

/// <summary>
/// The real <see cref="INupkgFetcher"/> — talks to a NuGet v3 feed (normally GitHub Packages,
/// <c>https://nuget.pkg.github.com/{owner}/index.json</c>) via <c>NuGet.Protocol</c>'s
/// <c>FindPackageByIdResource.CopyNupkgToStreamAsync</c>, the same exact-version download primitive
/// `dotnet restore` itself uses. NOT exercised by the automated test suite — verify this by hand against
/// a real feed before depending on it in production; GitHub Packages requires authentication even for
/// its own service index (unlike nuget.org), which `Repository.Factory.GetCoreV3` may not surface a
/// clean failure for if that turns out to be a problem.
/// </summary>
public sealed class GitHubPackagesNupkgFetcher(string feedUrl, string? patToken) : INupkgFetcher
{
    public async Task<bool> TryCopyNupkgAsync(string packageId, NuGetVersion version, Stream destination, CancellationToken ct = default)
    {
        var source = new PackageSource(feedUrl);
        if (!string.IsNullOrEmpty(patToken))
            source.Credentials = new PackageSourceCredential(
                feedUrl, username: "PAT", passwordText: patToken, isPasswordClearText: true, validAuthenticationTypesText: null);

        var repository = Repository.Factory.GetCoreV3(source);
        var findResource = await repository.GetResourceAsync<FindPackageByIdResource>(ct);
        using var cache = new SourceCacheContext { NoCache = true, DirectDownload = true };

        return await findResource.CopyNupkgToStreamAsync(
            packageId, version, destination, cache, NullLogger.Instance, ct);
    }
}
