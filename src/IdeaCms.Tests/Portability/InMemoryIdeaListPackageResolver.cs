using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Portability;

namespace IdeaCms.Tests.Portability;

/// <summary>Test double for <see cref="IIdeaListPackageResolver"/> — an in-memory pool of pre-registered
/// <c>.idea</c> bytes, keyed exactly like the real filesystem resolver: by (Kind, Key, Version), never
/// by filename.</summary>
internal sealed class InMemoryIdeaListPackageResolver : IIdeaListPackageResolver
{
    private readonly Dictionary<(ContentKind Kind, string Key, int Version), byte[]> _packages = new();

    public void Add(ContentKind kind, string key, int version, byte[] bytes) =>
        _packages[(kind, key.ToLowerInvariant(), version)] = bytes;

    public Task<Stream?> ResolveAsync(ContentKind kind, string key, int version, CancellationToken ct = default) =>
        Task.FromResult(_packages.TryGetValue((kind, key.ToLowerInvariant(), version), out var bytes)
            ? (Stream?)new MemoryStream(bytes)
            : null);
}
