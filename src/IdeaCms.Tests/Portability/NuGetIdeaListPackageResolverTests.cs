using System.IO.Compression;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Portability;
using IdeaCms.Packaging;
using IdeaCms.Tests.Packaging;
using NuGet.Versioning;

namespace IdeaCms.Tests.Portability;

/// <summary>
/// <see cref="NuGetIdeaListPackageResolver"/>'s own logic — id/version mapping, local caching,
/// unzipping <c>content/{key}.idea</c> back out of a `.nupkg` — against a fake <see
/// cref="INupkgFetcher"/>, no real network involved. The real GitHub-Packages-talking fetcher
/// (<see cref="GitHubPackagesNupkgFetcher"/>) is verified by hand against a live feed instead.
/// </summary>
[TestFixture]
public class NuGetIdeaListPackageResolverTests
{
    private string _cacheDir = "";

    [SetUp]
    public void SetUp()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "nugetcache_" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true); } catch { }
    }

    private static byte[] BuildNupkg(string key, byte[] ideaBytes)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry($"content/{key}.idea");
            using var es = entry.Open();
            es.Write(ideaBytes);
        }
        return ms.ToArray();
    }

    [Test]
    public async Task ResolveAsync_MapsKindKeyVersion_ToPackageIdAndNuGetVersion()
    {
        var fetcher = new FakeNupkgFetcher();
        var ideaBytes = IdeaTestArchive.CodePackage("tooltip", 1, "Plugin").ToArray();
        fetcher.Add("IdeaCms.Plugin.tooltip", new NuGetVersion(1, 0, 0), BuildNupkg("tooltip", ideaBytes));

        var resolver = new NuGetIdeaListPackageResolver(fetcher, _cacheDir);
        await using var stream = await resolver.ResolveAsync(ContentKind.Plugin, "tooltip", 1);

        Assert.That(stream, Is.Not.Null);
        using var archive = IdeaArchiveReader.Open(stream!);
        archive.TryReadManifest(out var manifest, out _);
        Assert.That(manifest!.Key, Is.EqualTo("tooltip"));
    }

    [Test]
    public async Task ResolveAsync_MissingFromFeed_ReturnsNull()
    {
        var resolver = new NuGetIdeaListPackageResolver(new FakeNupkgFetcher(), _cacheDir);
        var stream = await resolver.ResolveAsync(ContentKind.Plugin, "ghost", 1);
        Assert.That(stream, Is.Null);
    }

    [Test]
    public async Task ResolveAsync_SecondCall_UsesLocalCache_DoesNotHitTheFetcherAgain()
    {
        var fetcher = new FakeNupkgFetcher();
        var ideaBytes = IdeaTestArchive.CodePackage("tooltip", 1, "Plugin").ToArray();
        fetcher.Add("IdeaCms.Plugin.tooltip", new NuGetVersion(1, 0, 0), BuildNupkg("tooltip", ideaBytes));
        var resolver = new NuGetIdeaListPackageResolver(fetcher, _cacheDir);

        await using (await resolver.ResolveAsync(ContentKind.Plugin, "tooltip", 1)) { }
        await using (await resolver.ResolveAsync(ContentKind.Plugin, "tooltip", 1)) { }

        Assert.That(fetcher.CallCount, Is.EqualTo(1), "the second resolve must be served from the local cache");
    }

    [Test]
    public async Task ResolveAsync_EntryMissingInsideNupkg_ReturnsNull()
    {
        var fetcher = new FakeNupkgFetcher();
        // A .nupkg that exists but doesn't carry the expected content/{key}.idea entry.
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) { zip.CreateEntry("content/wrong.idea"); }
        fetcher.Add("IdeaCms.Plugin.tooltip", new NuGetVersion(1, 0, 0), ms.ToArray());

        var resolver = new NuGetIdeaListPackageResolver(fetcher, _cacheDir);
        var stream = await resolver.ResolveAsync(ContentKind.Plugin, "tooltip", 1);

        Assert.That(stream, Is.Null);
    }
}
