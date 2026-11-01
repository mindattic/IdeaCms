using Microsoft.Extensions.Configuration;
using IdeaCms.Core.Portability;

namespace IdeaCms.Blazor;

/// <summary>
/// Builds the <see cref="IIdeaListPackageResolver"/> every `.idealist` entry point (boot, CLI import,
/// CLI export/compose) composes the same way: the local filesystem resolver first (fast, no network,
/// first-party dev citizens), falling back to a NuGet-backed resolver only when a feed is configured.
/// </summary>
internal static class IdeaListResolverFactory
{
    public static IIdeaListPackageResolver Build(IReadOnlyList<string> packagesDirs, string contentRootPath, IConfiguration configuration)
    {
        var dirs = packagesDirs.Count > 0 ? packagesDirs : [Path.Combine(contentRootPath, "library")];
        var fsResolver = new FileSystemIdeaListPackageResolver(dirs);

        var feedUrl = configuration["IdeaCms:NuGetFeedUrl"];
        if (string.IsNullOrWhiteSpace(feedUrl)) return fsResolver;

        // GitHub Packages requires a PAT even to read a package the org owns — the Tokens Vault bucket
        // is the existing home for a value like this (MindAttic.Authentication's ConfigAuthSecrets pattern).
        var pat = configuration["MindAttic:Vault:Tokens:github-packages-pat"];
        var cacheDir = Path.Combine(contentRootPath, "library", ".nuget-cache");
        var fetcher = new GitHubPackagesNupkgFetcher(feedUrl, pat);
        var nugetResolver = new NuGetIdeaListPackageResolver(fetcher, cacheDir);

        return new CompositeIdeaListPackageResolver([fsResolver, nugetResolver]);
    }
}
