using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Core.Portability;

/// <summary>
/// Tries each resolver in order, first match wins — normally the local filesystem resolver first (fast,
/// no network, first-party dev citizens) then a NuGet-backed resolver as fallback. A new class rather
/// than an ordered-list change to <see cref="IIdeaListPackageResolver"/> itself, since that interface
/// already has four call sites depending on its single-resolver shape.
/// </summary>
public sealed class CompositeIdeaListPackageResolver(IReadOnlyList<IIdeaListPackageResolver> resolvers) : IIdeaListPackageResolver
{
    public async Task<Stream?> ResolveAsync(ContentKind kind, string key, int version, CancellationToken ct = default)
    {
        foreach (var resolver in resolvers)
            if (await resolver.ResolveAsync(kind, key, version, ct) is { } stream)
                return stream;
        return null;
    }
}
