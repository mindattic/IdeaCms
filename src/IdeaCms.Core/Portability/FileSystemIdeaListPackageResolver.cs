using IdeaCms.Abstractions;
using IdeaCms.Packaging;

namespace IdeaCms.Core.Portability;

/// <summary>
/// Resolves a <see cref="IdeaList.Packages"/> entry by opening every <c>*.idea</c> under each configured
/// directory, in order, and reading its manifest — NEVER by filename: a third-party package does not
/// follow the first-party "IdeaCms.{Category}.{Key}.V{n}.idea" naming convention. Directories are
/// searched in the order given; the first one that contains a matching (Kind,Key,Version) wins. The
/// index is built once, lazily, and cached — a package pool this large does not change while a boot or
/// a CLI invocation is running.
/// </summary>
public sealed class FileSystemIdeaListPackageResolver(IReadOnlyList<string> directories) : IIdeaListPackageResolver
{
    private readonly Lock _gate = new();
    private Dictionary<(ContentKind Kind, string Key, int Version), string>? _index;

    public Task<Stream?> ResolveAsync(ContentKind kind, string key, int version, CancellationToken ct = default)
    {
        var index = BuildIndexOnce();
        return Task.FromResult(index.TryGetValue((kind, key.ToLowerInvariant(), version), out var path)
            ? (Stream?)File.OpenRead(path)
            : null);
    }

    private Dictionary<(ContentKind, string, int), string> BuildIndexOnce()
    {
        if (_index is not null) return _index;
        lock (_gate)
        {
            if (_index is not null) return _index;

            var index = new Dictionary<(ContentKind, string, int), string>();
            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.idea").OrderBy(f => f, StringComparer.Ordinal))
                {
                    try
                    {
                        using var archive = IdeaArchiveReader.Open(file);
                        if (!archive.TryReadManifest(out var manifest, out _) || manifest is null) continue;
                        if (!Enum.TryParse<ContentKind>(manifest.Category, ignoreCase: true, out var kind)) continue;
                        index.TryAdd((kind, manifest.Key.ToLowerInvariant(), manifest.Version), file);
                    }
                    catch { /* an unreadable .idea in the pool is skipped, not fatal to resolution */ }
                }
            }
            _index = index;
            return index;
        }
    }
}
