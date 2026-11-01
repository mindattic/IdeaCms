using System.Reflection;
using System.Runtime.Loader;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Discovery;

namespace IdeaCms.Tests.Packaging;

/// <summary>
/// The ALC unification linchpin: a per-package collectible context defers host/framework names to Default
/// (so casts to host base types succeed) while loading private deps itself. Pure policy + real load behavior.
/// Never calls Unload (matches the soft, effective-on-restart model).
/// </summary>
[TestFixture]
public class CmsPackageLoadContextTests
{
    // ---- pure deferral policy ----

    [Test]
    public void ShouldDefer_HostAndFrameworkNames_True()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AlcDeferralPolicy.ShouldDefer("MindAttic.Ideas.Abstractions", _ => false), Is.True);
            Assert.That(AlcDeferralPolicy.ShouldDefer("IdeaCms.Core", _ => false), Is.True);
            Assert.That(AlcDeferralPolicy.ShouldDefer("System.Text.Json", _ => false), Is.True);
            Assert.That(AlcDeferralPolicy.ShouldDefer("Microsoft.EntityFrameworkCore", _ => false), Is.True);  // bare name
            Assert.That(AlcDeferralPolicy.ShouldDefer(null, _ => false), Is.True);
        });
    }

    [Test]
    public void ShouldDefer_PrivateName_FalseUnlessAlreadyLoadedInDefault()
    {
        Assert.That(AlcDeferralPolicy.ShouldDefer("Markdig", _ => false), Is.False);
        Assert.That(AlcDeferralPolicy.ShouldDefer("Markdig", n => n == "Markdig"), Is.True);   // dup in Default -> defer
    }

    // ---- real load behavior ----

    private static string PrivateDllPath()
    {
        // AngleSharp ships beside the test (transitive via Core) and is NOT a host-deferred name.
        var path = Path.Combine(AppContext.BaseDirectory, "AngleSharp.dll");
        if (!File.Exists(path)) Assert.Ignore("AngleSharp.dll not present beside the test assembly.");
        return path;
    }

    [Test]
    public void DeferredHostName_ResolvesToTheDefaultContextAssembly()
    {
        var ctx = new CmsPackageLoadContext(PrivateDllPath());
        var hostAbstractions = typeof(SharedContracts).Assembly;

        var resolved = ctx.LoadFromAssemblyName(new AssemblyName(hostAbstractions.GetName().Name!));

        Assert.That(resolved, Is.SameAs(hostAbstractions), "a deferred name must unify with the host's single identity");
    }

    [Test]
    public void PrivateAssembly_LoadsIntoThisContext_NotDefault()
    {
        var ctx = new CmsPackageLoadContext(PrivateDllPath());

        var asm = ctx.LoadFromAssemblyPath(PrivateDllPath());

        Assert.Multiple(() =>
        {
            Assert.That(asm.GetName().Name, Is.EqualTo("AngleSharp"));
            Assert.That(AssemblyLoadContext.GetLoadContext(asm), Is.SameAs(ctx), "a private dep loads into the package context");
            Assert.That(AssemblyLoadContext.GetLoadContext(asm), Is.Not.SameAs(AssemblyLoadContext.Default));
        });
    }

    // ---- flat bin/ probing (an extracted .idea has no NuGet-shaped deps.json) ----

    /// <summary>
    /// Pick a real assembly beside the test that is (a) not a host-deferred name and (b) not already loaded
    /// in Default — the only shape where the probe, rather than deferral, decides the outcome.
    /// </summary>
    private static string UnloadedPrivateDll()
    {
        foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(dll);
            if (AlcDeferralPolicy.ShouldDefer(name, IsLoadedInDefault)) continue;
            return dll;
        }
        Assert.Ignore("No unloaded private assembly available beside the test assembly.");
        return null!;

        static bool IsLoadedInDefault(string simpleName) =>
            AssemblyLoadContext.Default.Assemblies.Any(a =>
                string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public void PrivateDependency_ResolvesFromFlatBinDir_WithoutDepsJson()
    {
        // An extracted .idea is a FLAT bin/: the entry DLL plus its non-host dependencies, no deps.json.
        // AssemblyDependencyResolver cannot resolve that layout, so the context must probe its own directory.
        var privateDll = UnloadedPrivateDll();
        var binDir = Path.Combine(Path.GetTempPath(), "ma-idea-alc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(binDir);
        try
        {
            var entryPath = Path.Combine(binDir, "Entry.dll");
            File.Copy(typeof(CmsPackageLoadContextTests).Assembly.Location, entryPath);
            var depPath = Path.Combine(binDir, Path.GetFileName(privateDll));
            File.Copy(privateDll, depPath);
            Assert.That(Directory.EnumerateFiles(binDir, "*.deps.json"), Is.Empty, "guard: the layout under test has no deps.json");

            var ctx = new CmsPackageLoadContext(entryPath);

            var asm = ctx.LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(depPath)));

            Assert.Multiple(() =>
            {
                Assert.That(asm, Is.Not.Null);
                Assert.That(asm.Location, Is.EqualTo(depPath).IgnoreCase, "must load the copy inside the package's own bin/");
                Assert.That(AssemblyLoadContext.GetLoadContext(asm), Is.SameAs(ctx));
            });
        }
        finally { try { Directory.Delete(binDir, recursive: true); } catch { } }
    }

    [Test]
    public void ProbeIsScopedToThePackageBinDir_NotTheHostDirectory()
    {
        // The probe must only see the package's OWN bin/. An assembly that exists beside the host (here,
        // beside the test) but was never packed into the .idea must not resolve — otherwise a package would
        // silently bind to whatever the host happens to ship.
        var privateDll = UnloadedPrivateDll();
        var binDir = Path.Combine(Path.GetTempPath(), "ma-idea-alc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(binDir);
        try
        {
            var entryPath = Path.Combine(binDir, "Entry.dll");
            File.Copy(typeof(CmsPackageLoadContextTests).Assembly.Location, entryPath);
            // NOTE: privateDll is deliberately NOT copied into binDir.
            var ctx = new CmsPackageLoadContext(entryPath);

            // Load() finds nothing in bin/ and returns null, so the runtime falls back to Default. The
            // assembly may still resolve there — what must NOT happen is it loading into the package context.
            var asm = ctx.LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(privateDll)));

            Assert.That(AssemblyLoadContext.GetLoadContext(asm), Is.Not.SameAs(ctx),
                "an assembly absent from the package's bin/ must never load into the package context");
        }
        finally { try { Directory.Delete(binDir, recursive: true); } catch { } }
    }
}
