using System.IO.Compression;
using IdeaCms.Abstractions;
using NuGet.Versioning;

namespace IdeaCms.Core.Portability;

/// <summary>
/// Resolves a <see cref="IdeaList.Packages"/> entry from a NuGet feed — each `.idea` citizen
/// distributes as its own package, id <c>IdeaCms.{Category}.{Key}</c>, whole-number citizen
/// version <c>n</c> as NuGet version <c>{n}.0.0</c>. The downloaded `.nupkg` (a plain zip) is cached
/// locally by exact id+version — deterministic and collision-free since version is always
/// <c>{n}.0.0</c> — and <c>content/{key}.idea</c> is pulled back out of it as a plain <see
/// cref="ZipArchive"/> entry; no NuGet signing APIs are used anywhere here (a `.idea`'s own content
/// signature, see <c>PackageSigner</c>, is verified independently at install time).
/// </summary>
public sealed class NuGetIdeaListPackageResolver(INupkgFetcher fetcher, string cacheDir) : IIdeaListPackageResolver
{
    public async Task<Stream?> ResolveAsync(ContentKind kind, string key, int version, CancellationToken ct = default)
    {
        var packageId = $"IdeaCms.{kind}.{key}";
        var nugetVersion = new NuGetVersion(version, 0, 0);
        var cachePath = Path.Combine(cacheDir, $"{packageId}.{nugetVersion}.nupkg");

        if (!File.Exists(cachePath))
        {
            Directory.CreateDirectory(cacheDir);
            var tmpPath = cachePath + ".tmp";
            bool found;
            await using (var tmp = File.Create(tmpPath))
                found = await fetcher.TryCopyNupkgAsync(packageId, nugetVersion, tmp, ct);

            if (!found)
            {
                File.Delete(tmpPath);
                return null;
            }
            File.Move(tmpPath, cachePath, overwrite: true);
        }

        using var nupkg = new ZipArchive(File.OpenRead(cachePath), ZipArchiveMode.Read);
        var contentEntryName = $"content/{key}.idea";
        var entry = nupkg.Entries.FirstOrDefault(e => string.Equals(e.FullName, contentEntryName, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;

        var ms = new MemoryStream();
        await using (var es = entry.Open())
            await es.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }
}
