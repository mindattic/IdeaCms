using IdeaCms.Packaging;

namespace IdeaCms.Core.Discovery;

/// <summary>
/// Extracts an installed code package's <c>bin/</c> to disk so the ALC loader can load it, and owns the
/// extracted-path convention shared by install (which extracts) and <see cref="AlcAwareTypeResolver"/>
/// (which loads from the same place). Extracted files persist across restarts; nothing here deletes them.
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
