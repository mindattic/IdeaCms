using MindAttic.Ideas.Abstractions;
using IdeaCms.Packaging;

namespace IdeaCms.Core.Rendering;

/// <summary>One citizen's declared head assets, in the citizen's own cascade order.</summary>
public readonly record struct CitizenAssets(IReadOnlyList<string> Css, IReadOnlyList<string> Scripts);

/// <summary>The deduped, cascade-ordered head assets a page contributes (the collector's output).</summary>
public sealed record HeadAssets(IReadOnlyList<string> Css, IReadOnlyList<string> Scripts)
{
    public static readonly HeadAssets Empty = new([], []);
}

/// <summary>
/// Moves a package's ordered css[]/scripts[] across the <see cref="ContentDescriptor.Extra"/> seam, which
/// is <c>string→string</c> only. We join each list with <c>\n</c> on the way in and split on the way out;
/// order is preserved (the manifest lists are ordered) and blank entries are dropped. This is the no-schema
/// data path: a package's assets live in the verbatim <c>InstalledPackage.ManifestJson</c> and are surfaced
/// onto the in-memory descriptor at catalog-reload time — no new EF column.
/// </summary>
public static class ManifestAssetPacker
{
    private const string CssKey = "css";
    private const string ScriptsKey = "scripts";
    private const string UsesKey = "uses";

    public static IReadOnlyDictionary<string, string> PackExtra(IdeaManifest m) => new Dictionary<string, string>
    {
        [CssKey] = string.Join('\n', m.Css),
        [ScriptsKey] = string.Join('\n', m.Scripts),
        [UsesKey] = string.Join('\n', m.Uses),
    };

    public static CitizenAssets FromExtra(IReadOnlyDictionary<string, string>? extra)
    {
        if (extra is null) return new([], []);
        return new(Split(extra.GetValueOrDefault(CssKey)), Split(extra.GetValueOrDefault(ScriptsKey)));
    }

    /// <summary>The declared <c>uses[]</c> string ids a (compiled) citizen carries, surfaced from Extra.</summary>
    public static IReadOnlyList<string> UsesFromExtra(IReadOnlyDictionary<string, string>? extra) =>
        extra is null ? [] : Split(extra.GetValueOrDefault(UsesKey));

    private static IReadOnlyList<string> Split(string? joined) =>
        string.IsNullOrEmpty(joined)
            ? []
            : joined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Adapters that hand a descriptor's assets to <see cref="PageAssetCollector"/>.</summary>
public static class PageAssets
{
    /// <summary>
    /// Package-only adapter: a package citizen's manifest css/scripts (surfaced into Extra at reload)
    /// prefixed with the citizen's <see cref="ContentDescriptor.AssetMount"/> so the emitted &lt;head&gt;
    /// URLs resolve under the <c>/_ideas</c> route. Compiled citizens return none — use
    /// <see cref="AllAssetsOf"/> to also harvest compiled widgets via Activator.
    /// </summary>
    public static CitizenAssets PackageAssetsOf(ContentDescriptor d)
    {
        if (d.Origin != ContentOrigin.Package) return new([], []);
        var raw = ManifestAssetPacker.FromExtra(d.Extra);
        var mount = (d.AssetMount ?? "").TrimEnd('/');
        return new(Mount(raw.Css, mount), Mount(raw.Scripts, mount));
    }

    /// <summary>
    /// Unified adapter: package citizens → manifest css/scripts (mounted); compiled Plugin/Component →
    /// instantiated via <see cref="Activator"/> to read their declared
    /// <see cref="PluginBase.StylesheetUrls"/>/<see cref="PluginBase.ScriptUrls"/> (the same pattern
    /// <c>PageHost</c> uses for Theme harvest). Pass this as the <c>assetsOf</c> delegate to
    /// <see cref="PageAssetCollector.Collect"/> so compiled citizen assets are hoisted into
    /// <c>&lt;head&gt;</c> alongside package citizen assets.
    /// </summary>
    public static CitizenAssets AllAssetsOf(ContentDescriptor d, IContentCatalog catalog)
    {
        if (d.Origin == ContentOrigin.Package)
            return PackageAssetsOf(d);
        var type = catalog.ResolveType(d);
        if (type is null) return new([], []);
        try
        {
            var instance = Activator.CreateInstance(type);
            if (instance is PluginBase p) return new(p.StylesheetUrls, p.ScriptUrls);
            if (instance is ComponentBase c) return new(c.StylesheetUrls, c.ScriptUrls);
        }
        catch { /* abstract type or DI-only constructor — contribute no assets */ }
        return new([], []);
    }

    // Prefix a relative asset path with the package's mount; leave already-absolute (or unmounted) paths
    // as-is. "Absolute" is not just a leading "/": a manifest may name an off-package URL (a CDN
    // stylesheet, a protocol-relative "//host/x.css"), and mounting one produced a guaranteed 404 like
    // "/_ideas/Plugin/x/1/https://cdn.example/lib.css".
    private static IReadOnlyList<string> Mount(IReadOnlyList<string> rels, string mount)
    {
        if (mount.Length == 0) return rels;
        var outp = new List<string>(rels.Count);
        foreach (var r in rels)
            outp.Add(IsAbsolute(r) ? r : $"{mount}/{r}");
        return outp;
    }

    /// <summary>Root-relative ("/x.css"), protocol-relative ("//host/x.css"), or scheme-qualified ("https://…").</summary>
    private static bool IsAbsolute(string url) =>
        url.StartsWith('/') || Uri.IsWellFormedUriString(url, UriKind.Absolute);
}
