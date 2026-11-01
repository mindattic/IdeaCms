using IdeaCms.Abstractions;
using IdeaCms.Core.Rendering;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests.Rendering;

/// <summary>
/// The declarative dependency model: a COMPILED page declares the citizens it uses via [Uses] -> manifest
/// uses[], which (1) parses to the same (Kind,Key,Version) tuples the include grammar yields, (2) drives
/// head-asset hoisting through the SAME collector as a data page, and (3) feeds the delete reference-guard.
/// </summary>
[TestFixture]
public class UsesDeclarationTests
{
    // ---- uses[] grammar: "<Kind>.<key>[@<version>]" ----

    [Test]
    public void TryParseUse_BareKey_FloatsToLatest()
    {
        Assert.That(IncludeReferenceParser.TryParseUse("Plugin.tooltip", out var k, out var key, out var v), Is.True);
        Assert.That((k, key, v), Is.EqualTo((ContentKind.Plugin, "tooltip", (int?)null)));
    }

    [Test]
    public void TryParseUse_PinnedVersion_AndCaseInsensitiveKind()
    {
        Assert.That(IncludeReferenceParser.TryParseUse("theme.cyberspace@4", out var k, out var key, out var v), Is.True);
        Assert.That((k, key, v), Is.EqualTo((ContentKind.Theme, "cyberspace", (int?)4)));
    }

    [Test]
    public void TryParseUse_RejectsMalformed()
    {
        Assert.That(IncludeReferenceParser.TryParseUse("nope", out _, out _, out _), Is.False);       // no kind separator
        Assert.That(IncludeReferenceParser.TryParseUse("Bogus.x", out _, out _, out _), Is.False);    // kind not a ContentKind
        Assert.That(IncludeReferenceParser.TryParseUse("", out _, out _, out _), Is.False);
    }

    // ---- head-asset hoisting from uses[] (the compiled-page path) ----

    private sealed class FakeCatalog(IReadOnlyList<ContentDescriptor> all) : IContentCatalog
    {
        public IReadOnlyCollection<ContentDescriptor> All => (IReadOnlyCollection<ContentDescriptor>)all;
        public ContentDescriptor? Find(ContentKind kind, string key, int version) =>
            all.FirstOrDefault(d => d.Kind == kind && d.Key == key && d.Version == version);
        public ContentDescriptor? FindLatest(ContentKind kind, string key) =>
            all.Where(d => d.Kind == kind && d.Key == key).MaxBy(d => d.Version);
        public Type? ResolveType(ContentDescriptor descriptor) => null;
    }

    [Test]
    public void Collect_FromUses_HoistsReferencedCitizenAssets()
    {
        var tooltip = new ContentDescriptor
        {
            Kind = ContentKind.Plugin, Key = "tooltip", Version = 1, DisplayName = "Tooltip",
            Origin = ContentOrigin.Package,
        };
        var catalog = new FakeCatalog([tooltip]);
        var refs = IncludeReferenceParser.ParseUses(new[] { "Plugin.tooltip" });

        var head = PageAssetCollector.Collect(refs, catalog, _ => new CitizenAssets(["tooltip.css"], ["tooltip.js"]));

        Assert.Multiple(() =>
        {
            Assert.That(head.Css, Is.EqualTo(new[] { "tooltip.css" }));
            Assert.That(head.Scripts, Is.EqualTo(new[] { "tooltip.js" }));
        });
    }

    // ---- delete reference-guard sees a compiled page's uses[] ----

    [Test]
    public void DeleteGuard_BlocksDeletingAComponentACompiledPagePins()
    {
        var codePage = new PageRef(
            Slug: "hello-world", BodyHtml: null, ThemeKey: "cyberspace", ThemeVersion: 1,
            Enabled: true, IsPublished: true, Uses: new[] { "Plugin.tooltip@1" });

        // Another enabled version of tooltip remains, so a pin (not a float-orphan) is what must block.
        var blocking = ContentLifecycleService.FindBlockingPages(
            ContentKind.Plugin, "tooltip", 1, new[] { codePage }, otherEnabledVersions: new[] { 2 });

        Assert.That(blocking, Is.EqualTo(new[] { "hello-world" }));
    }

    [Test]
    public void DeleteGuard_FloatingUse_BlocksOnlyWhenNoOtherVersionRemains()
    {
        var codePage = new PageRef(
            Slug: "hello-world", BodyHtml: null, ThemeKey: null, ThemeVersion: null,
            Enabled: true, IsPublished: true, Uses: new[] { "Plugin.tooltip" }); // floats to latest

        var orphaned = ContentLifecycleService.FindBlockingPages(
            ContentKind.Plugin, "tooltip", 1, new[] { codePage }, otherEnabledVersions: Array.Empty<int>());
        var safe = ContentLifecycleService.FindBlockingPages(
            ContentKind.Plugin, "tooltip", 1, new[] { codePage }, otherEnabledVersions: new[] { 2 });

        Assert.Multiple(() =>
        {
            Assert.That(orphaned, Is.EqualTo(new[] { "hello-world" })); // deleting the sole version orphans the float
            Assert.That(safe, Is.Empty);                                // v2 remains -> the float harmlessly moves
        });
    }
}
