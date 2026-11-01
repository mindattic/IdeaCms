using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Rendering;
using IdeaCms.Packaging;

namespace IdeaCms.Tests.Rendering;

/// <summary>The string-dict round-trip that carries a package's ordered css/scripts across the Extra seam.</summary>
[TestFixture]
public class ManifestAssetPackerTests
{
    private static IdeaManifest Manifest(IReadOnlyList<string> css, IReadOnlyList<string> scripts) =>
        new() { ManifestVersion = 1, Category = "Plugin", Kind = "code", Key = "ui.tooltip", Version = 1,
                DisplayName = "Tooltip", Sdk = 1, EntryType = "X", Css = css, Scripts = scripts };

    [Test]
    public void PackExtra_PreservesCssOrder()
    {
        var extra = ManifestAssetPacker.PackExtra(Manifest(["a.css", "b.css"], []));
        Assert.That(extra["css"], Is.EqualTo("a.css\nb.css"));
    }

    [Test]
    public void PackExtra_PreservesScriptsOrder_IndependentlyOfCss()
    {
        var extra = ManifestAssetPacker.PackExtra(Manifest(["a.css"], ["one.js", "two.js"]));
        Assert.That(extra["scripts"], Is.EqualTo("one.js\ntwo.js"));
    }

    [Test]
    public void FromExtra_RoundTrips_OrderPreserved()
    {
        var m = Manifest(["a.css", "b.css", "c.css"], ["one.js", "two.js"]);
        var assets = ManifestAssetPacker.FromExtra(ManifestAssetPacker.PackExtra(m));
        Assert.Multiple(() =>
        {
            Assert.That(assets.Css, Is.EqualTo(new[] { "a.css", "b.css", "c.css" }));
            Assert.That(assets.Scripts, Is.EqualTo(new[] { "one.js", "two.js" }));
        });
    }

    [Test]
    public void FromExtra_Null_YieldsEmpty()
    {
        var assets = ManifestAssetPacker.FromExtra(null);
        Assert.Multiple(() =>
        {
            Assert.That(assets.Css, Is.Empty);
            Assert.That(assets.Scripts, Is.Empty);
        });
    }

    [Test]
    public void FromExtra_DropsBlankAndWhitespaceLines()
    {
        var assets = ManifestAssetPacker.FromExtra(new Dictionary<string, string> { ["css"] = "a.css\n\n  \nb.css" });
        Assert.That(assets.Css, Is.EqualTo(new[] { "a.css", "b.css" }));
    }

    [Test]
    public void EmptyManifest_RoundTripsToEmptyLists()
    {
        var assets = ManifestAssetPacker.FromExtra(ManifestAssetPacker.PackExtra(Manifest([], [])));
        Assert.Multiple(() =>
        {
            Assert.That(assets.Css, Is.Empty);
            Assert.That(assets.Scripts, Is.Empty);
        });
    }

    [Test]
    public void PackageAssetsOf_CompiledOrigin_ReturnsEmpty()
    {
        var d = new ContentDescriptor
        {
            Kind = ContentKind.Plugin, Key = "ui.tooltip", Version = 1, DisplayName = "Tooltip",
            Origin = ContentOrigin.Compiled,
            Extra = new Dictionary<string, string> { ["css"] = "should.css" },   // ignored for compiled
        };
        Assert.That(PageAssets.PackageAssetsOf(d).Css, Is.Empty);
    }

    [Test]
    public void PackageAssetsOf_PackageOrigin_ReturnsParsedLists()
    {
        var d = new ContentDescriptor
        {
            Kind = ContentKind.Plugin, Key = "ui.tooltip", Version = 1, DisplayName = "Tooltip",
            Origin = ContentOrigin.Package,
            Extra = ManifestAssetPacker.PackExtra(Manifest(["x.css"], ["x.js"])),
        };
        var assets = PageAssets.PackageAssetsOf(d);
        Assert.Multiple(() =>
        {
            Assert.That(assets.Css, Is.EqualTo(new[] { "x.css" }));
            Assert.That(assets.Scripts, Is.EqualTo(new[] { "x.js" }));
        });
    }

    [Test]
    public void PackageAssetsOf_PrefixesRelativePathsWithAssetMount()
    {
        var d = new ContentDescriptor
        {
            Kind = ContentKind.Plugin, Key = "ui.tooltip", Version = 1, DisplayName = "Tooltip",
            Origin = ContentOrigin.Package, AssetMount = "/_ideas/Plugin/ui.tooltip/1",
            Extra = ManifestAssetPacker.PackExtra(Manifest(["css/x.css", "/abs/already.css"], ["js/x.js"])),
        };
        var assets = PageAssets.PackageAssetsOf(d);
        Assert.Multiple(() =>
        {
            Assert.That(assets.Css, Is.EqualTo(new[] { "/_ideas/Plugin/ui.tooltip/1/css/x.css", "/abs/already.css" }));
            Assert.That(assets.Scripts, Is.EqualTo(new[] { "/_ideas/Plugin/ui.tooltip/1/js/x.js" }));
        });
    }
}
