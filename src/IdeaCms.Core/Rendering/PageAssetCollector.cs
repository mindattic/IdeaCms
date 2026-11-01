using IdeaCms.Abstractions;

namespace IdeaCms.Core.Rendering;

/// <summary>
/// Collects the head assets (css/scripts) a page contributes from the citizens it includes, deduped and in
/// cascade order, for hoisted &lt;head&gt; injection (so two components that use the same stylesheet inject
/// it once). PURE: it resolves include references against the catalog EXACTLY as the renderer does (so the
/// head never disagrees with the body about which version is on the page), and reads each citizen's assets
/// through an injected <paramref name="assetsOf"/> delegate — the only seam, so the package path (live this
/// slot) and the compiled-instance path (attended TODO) compose without changing this code.
/// </summary>
public static class PageAssetCollector
{
    public static HeadAssets Collect(string? pageBodyHtml, IContentCatalog catalog, Func<ContentDescriptor, CitizenAssets> assetsOf)
        => Collect(IncludeReferenceParser.Parse(pageBodyHtml), catalog, assetsOf);

    /// <summary>
    /// Collect from an explicit reference list — the seam a COMPILED page uses: its references come from the
    /// manifest <c>uses[]</c> (it has no BodyHtml to scan), parsed to the same (Kind,Key,Version) tuples this
    /// method already consumes. Same resolution + dedup + cascade order as the data-page path.
    /// </summary>
    public static HeadAssets Collect(
        IReadOnlyList<(ContentKind Kind, string Key, int? Version)> refs,
        IContentCatalog catalog, Func<ContentDescriptor, CitizenAssets> assetsOf)
    {
        if (refs.Count == 0) return HeadAssets.Empty;

        var css = new List<string>();
        var scripts = new List<string>();
        var seenCss = new HashSet<string>(StringComparer.Ordinal);
        var seenScripts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (kind, key, version) in refs)
        {
            // Mirror ContentCatalog.ResolveTag exactly: a pinned version resolves only to that version;
            // no fallback to latest (a missing pinned version contributes no head assets, matching the
            // MissingContent placeholder the renderer shows in the body).
            var desc = version is int v ? catalog.Find(kind, key, v) : catalog.FindLatest(kind, key);
            if (desc is null) continue;   // missing/disabled — contributes nothing; the renderer alerts, not us

            var assets = assetsOf(desc);
            foreach (var url in assets.Css)
                if (seenCss.Add(url)) css.Add(url);             // first-occurrence-wins, position fixed at first sight
            foreach (var url in assets.Scripts)
                if (seenScripts.Add(url)) scripts.Add(url);
        }

        return new HeadAssets(css, scripts);
    }
}
