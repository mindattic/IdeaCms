using IdeaCms.Core.Portability;
using NuGet.Versioning;

namespace IdeaCms.Tests.Portability;

/// <summary>Test double for <see cref="INupkgFetcher"/> — hands back canned .nupkg bytes with no
/// network involved, so <see cref="NuGetIdeaListPackageResolver"/>'s own logic (id/version mapping,
/// caching, unzipping) is unit-testable independent of a real feed.</summary>
internal sealed class FakeNupkgFetcher : INupkgFetcher
{
    public int CallCount { get; private set; }
    private readonly Dictionary<(string Id, NuGetVersion Version), byte[]> _nupkgs = new();

    public void Add(string packageId, NuGetVersion version, byte[] nupkgBytes) =>
        _nupkgs[(packageId, version)] = nupkgBytes;

    public Task<bool> TryCopyNupkgAsync(string packageId, NuGetVersion version, Stream destination, CancellationToken ct = default)
    {
        CallCount++;
        if (!_nupkgs.TryGetValue((packageId, version), out var bytes)) return Task.FromResult(false);
        destination.Write(bytes);
        destination.Position = 0;
        return Task.FromResult(true);
    }
}
