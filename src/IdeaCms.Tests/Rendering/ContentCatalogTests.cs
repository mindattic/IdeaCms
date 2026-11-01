using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Discovery;

namespace IdeaCms.Tests.Rendering;

/// <summary>
/// Key lookup in the live catalog. Every other key comparison in the codebase (the delete guard, the
/// include-reference parser, the manifest <c>uses[]</c> grammar) is OrdinalIgnoreCase, so the catalog —
/// the one place a key is finally matched against a citizen — must be too.
/// </summary>
[TestFixture]
public class ContentCatalogTests
{
    private sealed class NullResolver : ITypeResolver
    {
        public Type? Resolve(ContentDescriptor descriptor) => typeof(ContentCatalogTests);
    }

    private static ContentDescriptor Desc(ContentKind kind, string key, int version) => new()
    {
        Kind = kind, Key = key, Version = version, DisplayName = key,
        Origin = ContentOrigin.Package, Strategy = RenderStrategy.ClrType,
    };

    private static ContentCatalog CatalogWith(params ContentDescriptor[] all)
    {
        var c = new ContentCatalog(new NullResolver());
        c.LoadSnapshot(all, []);
        return c;
    }

    [Test]
    public void Find_MatchesKeyCaseInsensitively()
    {
        // Regression: a site's DefaultThemeKey is free text in the Admin Sites panel and is stored
        // verbatim. Typed as "Cyberspace" it never matched the catalog's lowercase "cyberspace", so the
        // whole site silently fell back to the bootstrap theme with only an inbox alert to explain it.
        var catalog = CatalogWith(Desc(ContentKind.Theme, "cyberspace", 1));

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Find(ContentKind.Theme, "Cyberspace", 1), Is.Not.Null);
            Assert.That(catalog.FindLatest(ContentKind.Theme, "CYBERSPACE"), Is.Not.Null);
            Assert.That(catalog.ResolveTag(ContentKind.Theme, "Cyberspace", null).Outcome,
                Is.EqualTo(ContentResolution.Resolved));
        });
    }

    [Test]
    public void ResolveTag_DisabledIdentity_MatchesKeyCaseInsensitively()
    {
        var catalog = new ContentCatalog(new NullResolver());
        catalog.LoadSnapshot([], [(ContentKind.Plugin, "navmenu", 1)]);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.ResolveTag(ContentKind.Plugin, "NavMenu", 1).Outcome,
                Is.EqualTo(ContentResolution.Disabled));
            Assert.That(catalog.ResolveTag(ContentKind.Plugin, "NavMenu", null).Outcome,
                Is.EqualTo(ContentResolution.Disabled));
        });
    }

    [Test]
    public void FindLatest_StillPicksTheHighestVersion()
    {
        var catalog = CatalogWith(
            Desc(ContentKind.Component, "card", 1),
            Desc(ContentKind.Component, "card", 3),
            Desc(ContentKind.Component, "card", 2));

        Assert.That(catalog.FindLatest(ContentKind.Component, "Card")!.Version, Is.EqualTo(3));
    }
}
