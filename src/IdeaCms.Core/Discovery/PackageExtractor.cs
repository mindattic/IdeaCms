using IdeaCms.Packaging;

namespace IdeaCms.Core.Discovery;

/// <summary>
/// Extracts an installed code package's <c>bin/</c> to disk so the ALC loader can load it, and owns the
/// extracted-path convention shared by install (which extracts) and <see cref="AlcAwareTypeResolver"/>
/// (which loads from the same place). Extracted files persist across restarts; the only deletions are its own staging/moved-aside folders.
/// </summary>
public interface IPackageExtractor
{
    /// <summary>Extract the package's bin/ and wwwroot/ to its conventional dir; returns that dir.</summary>
    string Extract(IdeaArchiveReader archive, string category, string key, int version);

    /// <summary>The expected entry-assembly path for a package (whether or not it is extracted yet).</summary>
    string EntryDllPath(string category, string key, int version, string assemblyName);

    /// <summary>True once the package's entry assembly is present on disk.</summary>
    bool IsExtracted(string category, string key, int version, string assemblyName);

    /// <summary>
    /// Resolve a request-relative asset path to the physical file under the package's extracted
    /// <c>wwwroot/</c>, or null if it is absent or would escape that root (serves the <c>/_ideas</c> route).
    /// </summary>
    string? ResolveAsset(string category, string key, int version, string relativePath);

    /// <summary>
    /// Extract into a temporary staging folder WITHOUT touching the live extraction. The caller then
    /// <see cref="IStagedExtraction.Swap"/>s it in, commits its DB row, and calls
    /// <see cref="IStagedExtraction.Complete"/>; disposing without Complete undoes the swap. Used by the
    /// install path so a same-version overwrite whose live files are locked fails BEFORE the DB commit.
    /// The default stages nothing and extracts in place on Swap (for extractors with no disk to protect).
    /// </summary>
    IStagedExtraction Stage(IdeaArchiveReader archive, string category, string key, int version) =>
        new InPlaceStagedExtraction(() => Extract(archive, category, key, version));
}

/// <summary>A staged package extraction: Swap it in, then Complete after the DB commit; Dispose rolls back.</summary>
public interface IStagedExtraction : IDisposable
{
    /// <summary>Put the staged files live, moving any current extraction aside. Throws (leaving the live
    /// files as they were) when the current extraction can't be moved, e.g. a loaded, locked DLL.</summary>
    void Swap();

    /// <summary>Keep the swapped-in files and discard the moved-aside previous ones.</summary>
    void Complete();
}

internal sealed class InPlaceStagedExtraction(Action extract) : IStagedExtraction
{
    public void Swap() => extract();
    public void Complete() { }
    public void Dispose() { }
}

/// <summary>Local-filesystem extractor rooted at <c>%APPDATA%\MindAttic\IdeaCms\extracted</c> by default.</summary>
public sealed class PackageExtractor : IPackageExtractor
{
    private readonly string _root;

    public PackageExtractor(string? root = null)
    {
        _root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MindAttic", "IdeaCms", "extracted"));
    }

    public string DirFor(string category, string key, int version) =>
        Path.Combine(_root, category, key, version.ToString());

    public string WwwrootDirFor(string category, string key, int version) =>
        Path.Combine(DirFor(category, key, version), "wwwroot");

    public string EntryDllPath(string category, string key, int version, string assemblyName) =>
        Path.Combine(DirFor(category, key, version), assemblyName + ".dll");

    public bool IsExtracted(string category, string key, int version, string assemblyName) =>
        File.Exists(EntryDllPath(category, key, version, assemblyName));

    public string Extract(IdeaArchiveReader archive, string category, string key, int version)
    {
        var dir = DirFor(category, key, version);
        Directory.CreateDirectory(dir);
        archive.ExtractTo(dir, "bin/");                                   // -> dir/<assemblies>
        archive.ExtractTo(WwwrootDirFor(category, key, version), "wwwroot/");   // -> dir/wwwroot/<assets>
        return dir;
    }

    public IStagedExtraction Stage(IdeaArchiveReader archive, string category, string key, int version)
    {
        var dir = DirFor(category, key, version);
        var staged = dir + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staged);
            archive.ExtractTo(staged, "bin/");
            archive.ExtractTo(Path.Combine(staged, "wwwroot"), "wwwroot/");
        }
        catch
        {
            TryDelete(staged);
            throw;
        }
        return new DirectorySwap(dir, staged);
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Swap = rename the live dir aside, then rename the staged dir into place (same volume, so both are
    /// atomic renames). Renaming a dir whose DLL is loaded fails on Windows, which is exactly the
    /// "locked extracted DLL" case: it throws before anything live has changed.
    /// </summary>
    private sealed class DirectorySwap(string live, string staged) : IStagedExtraction
    {
        private string? _backup;
        private bool _swapped, _completed;

        public void Swap()
        {
            if (_swapped) return;
            if (Directory.Exists(live))
            {
                var backup = live + ".old-" + Guid.NewGuid().ToString("N");
                Directory.Move(live, backup);              // throws if anything inside is locked
                _backup = backup;
            }
            try
            {
                Directory.Move(staged, live);
            }
            catch
            {
                if (_backup is not null) { Directory.Move(_backup, live); _backup = null; }
                throw;
            }
            _swapped = true;
        }

        public void Complete()
        {
            _completed = true;
            if (_backup is not null) TryDelete(_backup);
        }

        public void Dispose()
        {
            if (_completed) return;
            if (_swapped)
            {
                // Roll back: put the previous extraction (if any) back where it was.
                try
                {
                    var discard = live + ".discard-" + Guid.NewGuid().ToString("N");
                    Directory.Move(live, discard);
                    if (_backup is not null) Directory.Move(_backup, live);
                    TryDelete(discard);
                }
                catch { /* best effort -- the DB was not changed, so its row still names this version */ }
            }
            TryDelete(staged);
        }
    }

    public string? ResolveAsset(string category, string key, int version, string relativePath)
    {
        var wwwroot = Path.GetFullPath(WwwrootDirFor(category, key, version));
        var rootWithSep = wwwroot.EndsWith(Path.DirectorySeparatorChar) ? wwwroot : wwwroot + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(wwwroot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)) return null;   // escapes wwwroot
        return File.Exists(target) ? target : null;
    }
}

/// <summary>No-op extractor (for hosts/tests that don't run the ALC loader). Never writes disk.</summary>
public sealed class NullPackageExtractor : IPackageExtractor
{
    public string Extract(IdeaArchiveReader archive, string category, string key, int version) => "";
    public string EntryDllPath(string category, string key, int version, string assemblyName) => "";
    public bool IsExtracted(string category, string key, int version, string assemblyName) => false;
    public string? ResolveAsset(string category, string key, int version, string relativePath) => null;
}
