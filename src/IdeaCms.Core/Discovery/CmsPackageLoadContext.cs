using System.Reflection;
using System.Runtime.Loader;
using IdeaCms.Packaging;

namespace IdeaCms.Core.Discovery;

/// <summary>
/// The pure deferral decision behind <see cref="CmsPackageLoadContext"/> — the ALC unification linchpin
/// (ADR Appendix E). A name is DEFERRED to the host's Default context (so the package's base types unify by
/// reference identity with the host's, and casts succeed) when it is a host/framework assembly OR is already
/// loaded in Default; otherwise it is a private dependency the package loads itself. Pure → unit-testable.
/// </summary>
public static class AlcDeferralPolicy
{
    public static bool ShouldDefer(string? simpleName, Func<string, bool> isLoadedInDefault)
    {
        if (string.IsNullOrEmpty(simpleName)) return true;            // nameless -> let Default decide
        return ManifestValidator.IsHostAssemblyName(simpleName) || isLoadedInDefault(simpleName);
    }
}

/// <summary>
/// A per-package collectible <see cref="AssemblyLoadContext"/> that loads a runtime <c>.idea</c>'s private
/// assemblies while DEFERRING host/framework names to the Default context (see <see cref="AlcDeferralPolicy"/>).
/// This is the load primitive only — it is NOT wired as the live <c>ITypeResolver</c> yet, and true
/// <see cref="AssemblyLoadContext.Unload"/> is deliberately never called (uninstall stays soft-disable +
/// effective-on-restart, so a loaded type can't pin a half-torn-down context). Blob extraction and the
/// ALC-aware resolver that consumes this are the attended Phase-5/B follow-up.
/// </summary>
public sealed class CmsPackageLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _binDir;

    public CmsPackageLoadContext(string entryAssemblyPath)
        : base(name: "ma-idea:" + Path.GetFileNameWithoutExtension(entryAssemblyPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _binDir = Path.GetDirectoryName(Path.GetFullPath(entryAssemblyPath))!;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Defer host/framework + already-loaded names to Default (return null) so they resolve to ONE identity.
        if (AlcDeferralPolicy.ShouldDefer(assemblyName.Name, IsLoadedInDefault))
            return null;

        // Otherwise this is a private package dependency — load it into THIS context.
        // AssemblyDependencyResolver only succeeds when a NuGet-shaped .deps.json sits beside the entry
        // assembly. An extracted .idea is a FLAT bin/ (the packer copies every non-host dependency next to
        // the entry DLL, no deps.json, no package layout), so the directory probe below is the path that
        // actually resolves a package's third-party libraries (e.g. Markdig for Component.FromMd).
        var path = _resolver.ResolveAssemblyToPath(assemblyName) ?? ProbeBinDir(assemblyName.Name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <summary>Resolve <c>&lt;simpleName&gt;.dll</c> from the package's own extracted bin/ directory.</summary>
    private string? ProbeBinDir(string? simpleName)
    {
        if (string.IsNullOrEmpty(simpleName)) return null;
        var candidate = Path.Combine(_binDir, simpleName + ".dll");
        // Guard traversal: a hostile simple name must not escape the package's own bin/.
        var full = Path.GetFullPath(candidate);
        if (!full.StartsWith(_binDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(full) ? full : null;
    }

    private static bool IsLoadedInDefault(string simpleName) =>
        Default.Assemblies.Any(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
}
