using System.Collections.Concurrent;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests.Packaging;

internal sealed class InMemoryPackageBlobStore : IPackageBlobStore
{
    public readonly ConcurrentDictionary<string, byte[]> Saved = new();

    public Task<string> SaveAsync(string category, string key, int version, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        var path = LocalFilePackageBlobStore.BlobPathFor(category, key, version);
        Saved[path] = bytes.ToArray();
        return Task.FromResult(path);
    }

    public Task<Stream?> OpenAsync(string blobPath, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(Saved.TryGetValue(blobPath, out var b) ? new MemoryStream(b) : null);

    public Task<bool> ExistsAsync(string blobPath, CancellationToken ct = default) =>
        Task.FromResult(Saved.ContainsKey(blobPath));
}
