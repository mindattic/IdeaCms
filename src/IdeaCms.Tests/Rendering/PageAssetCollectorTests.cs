using IdeaCms.Abstractions;
using IdeaCms.Core.Rendering;

namespace IdeaCms.Tests.Rendering;

/// <summary>The pure cascade/dedup algorithm, over a fake catalog + stubbed per-citizen assets.</summary>
[TestFixture]
public class PageAssetCollectorTests
{
    // Minimal IContentCatalog: Find/FindLatest mirror ContentCatalog; the collector uses only those.
    private sealed class FakeCatalog(IReadOnlyList<ContentDescriptor> all) : IContentCatalog
    {
        public IReadOnlyCollection<ContentDescriptor> All => (IReadOnlyCollection<ContentDescriptor>)all;
        public ContentDescriptor? Find(ContentKind kind, string key, int version) =>
            all.FirstOrDefault(d => d.Kind == kind && d.Key == key && d.Version == version);
        public ContentDescriptor? FindLatest(ContentKind kind, string key) =>
            all.Where(d => d.Kind == kind && d.Key == key).MaxBy(d => d.Version);
        public Type? ResolveType(ContentDescriptor descriptor) => null;
    }

    private static ContentDescriptor Comp(string key, int version = 1) => new()
    {
        Kind = ContentKind.Plugin, Key = key, Version = version, DisplayName = key, Origin = ContentOrigin.Package,
    };

    private static CitizenAssets Css(params string[] urls) => new(urls, []);

    [Test]
    public void EmitsAssets_InDocumentOrder_AcrossCitizens()
    {
        var catalog = new FakeCatalog([Comp("a"), Comp("b")]);
        var assets = new Dictionary<string, CitizenAssets> { ["a"] = Css("a.css"), ["b"] = Css("b.css") };

        var head = PageAssetCollector.Collect(
            "<Plugin.A /><Plugin.B />", catalog, d => assets[d.Key]);

        Assert.That(head.Css, Is.EqualTo(new[] { "a.css", "b.css" }));
    }

    [Test]
    public void DuplicateUrl_DedupedFirstOccurrenceWins_PositionFixed()
    {
        var catalog = new FakeCatalog([Comp("a"), Comp("b")]);
        var assets = new Dictionary<string, CitizenAssets> { ["a"] = Css("x.css", "a.css"), ["b"] = Css("x.css", "b.css") };

        var head = PageAssetCollector.Collect(
            "<Plugin.A /><Plugin.B />", catalog, d => assets[d.Key]);

        Assert.That(head.Css, Is.EqualTo(new[] { "x.css", "a.css", "b.css" }));   // x.css once, at first sight
    }

    [Test]
    public void CssAndScripts_DedupedInSeparateNamespaces()
    {
        var catalog = new FakeCatalog([Comp("a")]);
        var assets = new Dictionary<string, CitizenAssets> { ["a"] = new(["shared"], ["shared"]) };

        var head = PageAssetCollector.Collect("<Plugin.A />", catalog, d => assets[d.Key]);

        Assert.Multiple(() =>
        {
            Assert.That(head.Css, Is.EqualTo(new[] { "shared" }));
            Assert.That(head.Scripts, Is.EqualTo(new[] { "shared" }));   // same URL survives in both lists
        });
    }

    [Test]
    public void Dedup_IsOrdinalCaseSensitive()
    {
        var catalog = new FakeCatalog([Comp("a"), Comp("b")]);
        var assets = new Dictionary<string, CitizenAssets> { ["a"] = Css("A.css"), ["b"] = Css("a.css") };

        var head = PageAssetCollector.Collect(
            "<Plugin.A /><Plugin.B />", catalog, d => assets[d.Key]);

        Assert.That(head.Css, Is.EqualTo(new[] { "A.css", "a.css" }));   // distinct
    }

    [Test]
    public void PinnedVersion_ResolvesViaFind_FloatingResolvesToHighest()
    {
        var catalog = new FakeCatalog([Comp("tip", 1), Comp("tip", 2)]);
        CitizenAssets AssetsFor(ContentDescriptor d) => Css($"tip.v{d.Version}.css");

        var pinned   = PageAssetCollector.Collect("<Plugin.Tip data-version=\"1\" />", catalog, AssetsFor);
        var floating = PageAssetCollector.Collect("<Plugin.Tip />", catalog, AssetsFor);

        Assert.Multiple(() =>
        {
            Assert.That(pinned.Css,   Is.EqualTo(new[] { "tip.v1.css" }));
            Assert.That(floating.Css, Is.EqualTo(new[] { "tip.v2.css" }));
        });
    }

    [Test]
    public void TwoDistinctPinnedVersions_EmitBothAssetSets()
    {
        var catalog = new FakeCatalog([Comp("tip", 1), Comp("tip", 2)]);
        var head = PageAssetCollector.Collect(
            "<Plugin.Tip data-version=\"1\" /><Plugin.Tip data-version=\"2\" />",
            catalog, d => Css($"tip.v{d.Version}.css"));

        Assert.That(head.Css, Is.EqualTo(new[] { "tip.v1.css", "tip.v2.css" }));
    }

    [Test]
    public void MissingOrDisabledRef_IsSkipped_NoThrow()
    {
        var catalog = new FakeCatalog([]);   // nothing resolves
        var head = PageAssetCollector.Collect("<Plugin.Gone />", catalog, _ => Css("never.css"));

        Assert.Multiple(() =>
        {
            Assert.That(head.Css, Is.Empty);
            Assert.That(head.Scripts, Is.Empty);
        });
    }

    [Test]
    public void NullOrBlankBody_YieldsEmptyHead()
    {
        var catalog = new FakeCatalog([Comp("a")]);
        Assert.Multiple(() =>
        {
            Assert.That(PageAssetCollector.Collect((string?)null, catalog, _ => Css("x")).Css, Is.Empty);
            Assert.That(PageAssetCollector.Collect("   ", catalog, _ => Css("x")).Scripts, Is.Empty);
        });
    }
}
