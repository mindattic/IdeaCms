namespace IdeaCms.Core.Services;

/// <summary>
/// Where a installed package's verbatim <c>.idea</c> bytes live. The registry row keeps the returned
/// <c>BlobPath</c>; the bytes are the source of truth the (deferred) ALC loader extracts to resolve the
/// package's types. The ADR names Azure Blob as the production backing — that swaps in behind this seam
/// with no caller change. The default host impl is a local file store (dev + single-box).
/// Soft model: there is no Delete — disabling a package retains its bytes.
/// </summary>
public interface IPackageBlobStore
{
    /// <summary>Persist the package bytes and return the opaque <c>BlobPath</c> to store on the registry row.</summary>
    Task<string> SaveAsync(string category, string key, int version, ReadOnlyMemory<byte> bytes, CancellationToken ct = default);

    /// <summary>Open a previously-saved package by its <c>BlobPath</c>, or null if it is not present.</summary>
    Task<Stream?> OpenAsync(string blobPath, CancellationToken ct = default);

    Task<bool> ExistsAsync(string blobPath, CancellationToken ct = default);
}

/// <summary>
/// Local-filesystem package store rooted at <c>%APPDATA%\MindAttic\IdeaCms\packages</c> by default. Layout:
/// <c>{category}/{key}/{version}.idea</c>. Identity segments are validated upstream (category is a
/// ContentKind name, key matches the package-key grammar, version is an int), and the resolved path is
/// re-checked to sit under the root — so a crafted identity can't escape the store.
/// </summary>
public sealed class LocalFilePackageBlobStore : IPackageBlobStore
{
    private readonly string _root;

    public LocalFilePackageBlobStore(string? root = null)
    {
        _root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MindAttic", "IdeaCms", "packages"));
    }

    public static string BlobPathFor(string category, string key, int version) => $"{category}/{key}/{version}.idea";

    public async Task<string> SaveAsync(string category, string key, int version, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        var blobPath = BlobPathFor(category, key, version);
        var full = Resolve(blobPath) ?? throw new IOException($"refusing to write outside the package store: {blobPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes.ToArray(), ct);
        return blobPath;
    }

    public Task<Stream?> OpenAsync(string blobPath, CancellationToken ct = default)
    {
        var full = Resolve(blobPath);
        Stream? s = full is not null && File.Exists(full) ? File.OpenRead(full) : null;
        return Task.FromResult(s);
    }

    public Task<bool> ExistsAsync(string blobPath, CancellationToken ct = default)
    {
        var full = Resolve(blobPath);
        return Task.FromResult(full is not null && File.Exists(full));
    }

    // Map a forward-slash BlobPath under the root; return null if it would escape the root (defense in depth).
    private string? Resolve(string blobPath)
    {
        var rel = blobPath.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_root, rel));
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
