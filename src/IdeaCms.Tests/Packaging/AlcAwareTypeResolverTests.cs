using System.Runtime.Loader;
using IdeaCms.Abstractions;
using IdeaCms.Core.Discovery;

namespace IdeaCms.Tests.Packaging;

/// <summary>
/// The live Phase-5 resolver: package citizens load through a CmsPackageLoadContext from their extracted
/// entry assembly; everything else delegates to the default resolver (so compiled content is unchanged);
/// an un-extracted package resolves to null (placeholder).
/// </summary>
[TestFixture]
public class AlcAwareTypeResolverTests
{
    private string _root = "";

    [SetUp]
    public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "ma-alc-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        // A loaded (never-unloaded, by design) assembly locks its file on Windows; best-effort cleanup.
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private AlcAwareTypeResolver NewResolver(out PackageExtractor extractor)
    {
        extractor = new PackageExtractor(_root);
        return new AlcAwareTypeResolver(new DefaultTypeResolver(), extractor);
    }

    [Test]
    public void CompiledDescriptor_DelegatesToDefaultResolver()
    {
        var resolver = NewResolver(out _);
        var d = new ContentDescriptor
        {
            Kind = ContentKind.Plugin, Key = "x", Version = 1, DisplayName = "X", Origin = ContentOrigin.Compiled,
            ClrTypeName = typeof(PackageExtractor).FullName, AssemblyName = typeof(PackageExtractor).Assembly.GetName().Name!,
        };
        Assert.That(resolver.Resolve(d), Is.SameAs(typeof(PackageExtractor)));
    }

    [Test]
    public void PackageDescriptor_NotExtracted_ResolvesNull()
    {
        var resolver = NewResolver(out _);
        var d = new ContentDescriptor
        {
            Kind = ContentKind.Plugin, Key = "ghost", Version = 1, DisplayName = "Ghost", Origin = ContentOrigin.Package,
            ClrTypeName = "Some.Type", AssemblyName = "Ghost",
        };
        Assert.That(resolver.Resolve(d), Is.Null);
    }

    [Test]
    public void PackageDescriptor_Extracted_LoadsTypeThroughPackageContext_AndCaches()
    {
        // A real, non-host assembly with minimal (System-only) deps so a lone-file load resolves cleanly.
        // IdeaCms.Packaging is NOT in the defer list, so it loads INTO the package context (proving
        // isolation) rather than unifying back to Default the way a host assembly would.
        var asmType = typeof(IdeaCms.Packaging.IdeaManifest);
        var asmName = asmType.Assembly.GetName().Name!;
        var src = asmType.Assembly.Location;

        var resolver = NewResolver(out var extractor);
        var dir = Path.GetDirectoryName(extractor.EntryDllPath("Plugin", "pkg", 1, asmName))!;
        Directory.CreateDirectory(dir);
        File.Copy(src, Path.Combine(dir, asmName + ".dll"));

        var d = new ContentDescriptor
        {
            Kind = ContentKind.Plugin, Key = "pkg", Version = 1, DisplayName = "Pkg", Origin = ContentOrigin.Package,
            Category = "Plugin", ClrTypeName = asmType.FullName, AssemblyName = asmName,
        };

        var t1 = resolver.Resolve(d);
        var t2 = resolver.Resolve(d);

        Assert.Multiple(() =>
        {
            Assert.That(t1, Is.Not.Null, "the package type should load from its extracted assembly");
            Assert.That(t1!.FullName, Is.EqualTo(asmType.FullName));
            Assert.That(t1, Is.Not.SameAs(asmType), "it is a SEPARATE identity loaded into the package context");
            Assert.That(AssemblyLoadContext.GetLoadContext(t1.Assembly), Is.InstanceOf<CmsPackageLoadContext>(),
                "loaded through the per-package context, not Default");
            Assert.That(t2, Is.SameAs(t1), "resolution is cached");
        });
    }
}
