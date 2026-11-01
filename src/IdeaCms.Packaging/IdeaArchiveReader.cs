using System.IO.Compression;
using System.Text.Json;

namespace IdeaCms.Packaging;

/// <summary>
/// Read-only inspection of a <c>.idea</c> archive WITHOUT extracting it. Lists entries and reads the
/// manifest in memory so the host can validate a package before any file ever touches disk (and before
/// any assembly is ever loaded — that is Phase 5/B). Every entry path is screened by
/// <see cref="IsSafeEntryPath"/> so a malicious archive can't smuggle a zip-slip path into a later
/// extraction step.
/// </summary>
public sealed class IdeaArchiveReader : IDisposable
{
    private readonly ZipArchive _zip;
    private readonly bool _ownsStream;
    private readonly Stream? _stream;

    private IdeaArchiveReader(ZipArchive zip, Stream? stream, bool ownsStream)
    {
        _zip = zip;
        _stream = stream;
        _ownsStream = ownsStream;
    }

    /// <summary>Open a <c>.idea</c> from a stream. The reader does NOT take ownership of the stream.</summary>
    public static IdeaArchiveReader Open(Stream stream) =>
        new(new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true), null, ownsStream: false);

    /// <summary>Open a <c>.idea</c> from a file path. The reader owns the underlying file handle.</summary>
    public static IdeaArchiveReader Open(string path)
    {
        var fs = File.OpenRead(path);
        return new IdeaArchiveReader(new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false), fs, ownsStream: true);
    }

    /// <summary>The raw <c>idea.json</c> text, or null if the archive has no manifest entry.</summary>
    public string? ReadManifestJson()
    {
        var entry = _zip.GetEntry("idea.json");
        if (entry is null) return null;
        using var r = new StreamReader(entry.Open());
        return r.ReadToEnd();
    }

    /// <summary>
    /// Read and parse the manifest. Returns false (with an <paramref name="error"/>) if the manifest is
    /// missing or malformed — never throws for a bad package.
    /// </summary>
    public bool TryReadManifest(out IdeaManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;
        var json = ReadManifestJson();
        if (json is null)
        {
            error = "package has no idea.json manifest at its root.";
            return false;
        }
        try
        {
            manifest = ManifestReader.Read(json);
            return true;
        }
        catch (Exception ex)
        {
            error = "idea.json is malformed: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// The optional <c>data/page.json</c> seed (a Page package's routing recipe), or null if absent or
    /// malformed — never throws, so a bad seed degrades to "no seed applied" rather than failing the install.
    /// </summary>
    public PageSeed? ReadPageSeed()
    {
        var entry = _zip.GetEntry("data/page.json");
        if (entry is null) return null;
        try
        {
            using var r = new StreamReader(entry.Open());
            return JsonSerializer.Deserialize<PageSeed>(r.ReadToEnd(), IdeaManifest.ManifestJson);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The raw <c>idea.sig.json</c> text, or null if the archive is unsigned.</summary>
    public string? ReadSignatureJson()
    {
        var entry = _zip.GetEntry(PackageSigner.SignatureEntryName);
        if (entry is null) return null;
        using var r = new StreamReader(entry.Open());
        return r.ReadToEnd();
    }

    /// <summary>
    /// Path + lowercase-hex SHA-256 of every non-directory entry's decompressed bytes, ordinally sorted
    /// by path, excluding <paramref name="excludeEntryName"/> (normally <see cref="PackageSigner.SignatureEntryName"/>,
    /// so the signature never has to sign itself). This is the canonical manifest <see cref="PackageSigner"/>
    /// signs and verifies — both sides MUST go through this same method so they canonicalize identically.
    /// </summary>
    public IReadOnlyList<(string Path, string Sha256Hex)> HashAllEntries(string? excludeEntryName = null)
    {
        var result = new List<(string, string)>();
        foreach (var e in _zip.Entries)
        {
            if (e.FullName.EndsWith('/')) continue;   // directory entry, no content to hash
            if (excludeEntryName is not null && string.Equals(e.FullName, excludeEntryName, StringComparison.Ordinal)) continue;

            using var s = e.Open();
            result.Add((e.FullName, Sha256Hasher.OfStream(s)));
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return result;
    }

    /// <summary>Entry paths under <c>bin/</c> (the bundled assemblies), relative to that prefix.</summary>
    public IReadOnlyList<string> BinEntries() => EntriesUnder("bin/");

    /// <summary>Entry paths under <c>wwwroot/</c> (the static assets), relative to that prefix.</summary>
    public IReadOnlyList<string> WwwrootEntries() => EntriesUnder("wwwroot/");

    /// <summary>Entry paths under <c>data/</c> (the optional install seed), relative to that prefix.</summary>
    public IReadOnlyList<string> DataEntries() => EntriesUnder("data/");

    private IReadOnlyList<string> EntriesUnder(string prefix) =>
        _zip.Entries
            .Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && e.FullName.Length > prefix.Length
                        && !e.FullName.EndsWith('/'))
            .Select(e => e.FullName[prefix.Length..])
            .ToList();

    /// <summary>
    /// True only for an archive entry path that is safe to extract: a forward-slash relative path with
    /// no rooted/drive prefix and no <c>..</c> segment (zip-slip guard).
    /// </summary>
    public static bool IsSafeEntryPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.StartsWith('/') || name.StartsWith('\\')) return false;        // rooted
        if (name.Length >= 2 && name[1] == ':') return false;                   // drive-qualified (C:/...)
        var segs = name.Replace('\\', '/').Split('/');
        return !segs.Contains("..");                                            // no parent-dir escape
    }

    /// <summary>
    /// Extract every entry under <paramref name="prefix"/> to <paramref name="destDir"/>, preserving the
    /// relative path below the prefix. Each entry is screened by <see cref="IsSafeEntryPath"/> AND the
    /// resolved target is re-checked to sit under <paramref name="destDir"/> (double zip-slip guard);
    /// unsafe entries are skipped. Returns the relative paths actually written.
    /// </summary>
    public IReadOnlyList<string> ExtractTo(string destDir, string prefix = "bin/")
    {
        var destFull = Path.GetFullPath(destDir);
        var destWithSep = destFull.EndsWith(Path.DirectorySeparatorChar) ? destFull : destFull + Path.DirectorySeparatorChar;
        var written = new List<string>();

        foreach (var e in _zip.Entries)
        {
            if (!e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || e.FullName.Length <= prefix.Length || e.FullName.EndsWith('/')) continue;

            var rel = e.FullName[prefix.Length..];
            if (!IsSafeEntryPath(rel)) continue;

            var target = Path.GetFullPath(Path.Combine(destFull, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destWithSep, StringComparison.OrdinalIgnoreCase)) continue;   // escapes dest -> skip

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: true);
            written.Add(rel);
        }
        return written;
    }

    public void Dispose()
    {
        _zip.Dispose();
        if (_ownsStream) _stream?.Dispose();
    }
}
