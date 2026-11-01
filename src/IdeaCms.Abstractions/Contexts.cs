using Microsoft.AspNetCore.Components;

namespace IdeaCms.Abstractions;

// ============================================================================================
//  RENDER CONTEXT — the single object handed to every citizen at render time.
//  It is an INTERFACE delivered as a [CascadingParameter], NOT a sealed class as a [Parameter].
//  This is the most load-bearing forward-compat choice in the foundation: an interface grows
//  append-only via DEFAULT methods AND unifies cleanly across the Phase-5 collectible-ALC boundary
//  (a sealed class passed by Parameter cannot). GROW ONLY by adding members WITH default
//  implementations; never remove/rename/reorder.
// ============================================================================================

/// <summary>Everything a Page/Component/Theme is handed at render time.</summary>
public interface IRenderContext
{
    /// <summary>Unique id for this placement/render instance.</summary>
    Guid InstanceId { get; }
    ContentMode Mode { get; }
    CmsRenderMode RenderMode { get; }
    IPageContext Page { get; }
    ISiteContext Site { get; }
    /// <summary>Scoped DI for this circuit/request.</summary>
    IServiceProvider Services { get; }
    /// <summary>Raw settings JSON for this placement, if any.</summary>
    string? RawSettingsJson { get; }
    /// <summary>Deserialize settings using the host's pinned serializer options.</summary>
    T GetSettings<T>() where T : class, new();

    /// <summary>
    /// Additive-forever escape hatch: resolve an optional host feature without ever changing this
    /// interface. Default implementation pulls from <see cref="Services"/>.
    /// </summary>
    bool TryGetFeature<T>(out T? feature) where T : class
    {
        feature = Services.GetService(typeof(T)) as T;
        return feature is not null;
    }

    /// <summary>
    /// The stored instance settings (a JSON object of setting name to value) for one slot on this page —
    /// <c>"theme"</c>, <c>"page"</c>, or <c>"plugin:{key}"</c> (M.IDEA-§4.5). Null when the instance has none;
    /// the host binds these onto the citizen's [Parameter]s, so citizens rarely call this directly.
    /// </summary>
    string? GetInstanceSettingsJson(string slot) => null;
}

/// <summary>The page being rendered.</summary>
public interface IPageContext
{
    Guid PageId { get; }
    string Slug { get; }
    string Title { get; }
    /// <summary>Overrides the browser &lt;title&gt; tag. Null = fall back to <see cref="Title"/>.</summary>
    string? SeoTitle { get; }
    string? ThemeKey { get; }
    /// <summary>The page's pinned theme version, if it overrides the site default.</summary>
    int? ThemeVersion { get; }
    /// <summary>Free-form author HTML/CSS/JS for this page (cascade tier 3) — Data pages.</summary>
    IInlineMarkup Inline { get; }
    IReadOnlyDictionary<string, string?> Meta { get; }
}

/// <summary>The site/tenant the page belongs to. Multi-site is a nullable seam from day one.</summary>
public interface ISiteContext
{
    Guid SiteId { get; }
    string Key { get; }
    string Host { get; }
    string DefaultThemeKey { get; }
    /// <summary>Read a setting through the Host -> Site -> Page override chain.</summary>
    string? GetSetting(string key);
}

/// <summary>
/// The free-form author content for a Data page: cascade tier 3. <see cref="Trusted"/> is the
/// render-time projection of the page's stored, write-time trust stamp.
/// </summary>
public interface IInlineMarkup
{
    string? Html { get; }
    string? Css { get; }
    string? Js { get; }
    /// <summary>True =&gt; host emits raw (intentional admin JS); false =&gt; sanitized.</summary>
    bool Trusted { get; }
}

/// <summary>
/// Host-provided render seam (resolved via <see cref="IRenderContext.TryGetFeature{T}"/>): turns a
/// string-id include reference — e.g. <c>"IdeaCms.Plugin.Tooltip.V1"</c> (omit the version or
/// use <c>.Latest</c> to float) — into a render fragment, using the SAME catalog resolution, Missing/
/// Disabled degradation, and Admin-Inbox alerting as a data page's <c>&lt;IdeaCms.…/&gt;</c>
/// include tag. This is what lets a COMPILED Page/Theme compose other citizens BY STRING ID with no
/// compile-time package reference — the <see cref="CmsInclude"/> primitive delegates to it. The host impl
/// MUST never throw into a render (degrade to a placeholder). APPEND-ONLY interface (default methods only).
/// </summary>
public interface IIncludeRenderer
{
    RenderFragment Render(
        IRenderContext context, string reference,
        IReadOnlyDictionary<string, object?>? attributes = null, RenderFragment? childContent = null);
}

/// <summary>
/// Host-provided seam (resolved via <see cref="IRenderContext.TryGetFeature{T}"/>): per-instance
/// metadata store for components that need to persist data beyond a single render — e.g. the
/// FromMd component stores its source file path and cached Markdown here. Keyed by
/// (pageUid, componentKey, slotName) so multiple instances on the same page are isolated.
/// The host impl MUST never throw into a render. APPEND-ONLY interface (default methods only).
/// </summary>
public interface IComponentMetadataStore
{
    /// <summary>Returns the stored metadata JSON for this slot, or null if none exists yet.</summary>
    Task<string?> GetAsync(Guid pageUid, string componentKey, string slotName = "main", CancellationToken ct = default);

    /// <summary>Upserts metadata JSON for this slot.</summary>
    Task SaveAsync(Guid pageUid, string componentKey, string slotName, string metadataJson, CancellationToken ct = default);

    /// <summary>
    /// Metadata for MANY pages at once, keyed by page uid; pages with no row for this slot are absent.
    /// A component that renders a list of other pages (an index, a card grid) needs one lookup, not one per
    /// row. The default implementation loops <see cref="GetAsync"/> so every existing host keeps working;
    /// a host SHOULD override it with a single query. MUST never throw into a render.
    /// </summary>
    async Task<IReadOnlyDictionary<Guid, string>> GetManyAsync(
        IEnumerable<Guid> pageUids, string componentKey, string slotName = "main", CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var uid in pageUids.Distinct())
        {
            var json = await GetAsync(uid, componentKey, slotName, ct);
            if (json is not null) result[uid] = json;
        }
        return result;
    }
}

/// <summary>
/// One child page in the site tree (for nav / the TableOfContents widget).
/// <paramref name="PageId"/> is the child's stable uid, so a component listing children can look their
/// metadata up through <see cref="IComponentMetadataStore"/>. Defaulted, and last, to stay additive.
/// </summary>
public sealed record ChildPage(string Slug, string Title, bool OpenInNewWindow = false, Guid PageId = default);

/// <summary>One node in a page sub-tree — carries its own children for recursive rendering.</summary>
public sealed record ChildPageNode(string Slug, string Title, IReadOnlyList<ChildPageNode> Children, bool OpenInNewWindow = false);

/// <summary>
/// Host-provided render seam (resolved via <see cref="IRenderContext.TryGetFeature{T}"/>): the published,
/// enabled child pages of a page, ordered by sort order. Lets a Component (e.g. TableOfContents) render the
/// CURRENT page's children with NO compile-time host reference — it asks the context for this feature and,
/// if present, lists <see cref="ChildPage"/>s; a Component renders nothing when a page has no children. The
/// host impl MUST never throw into a render. APPEND-ONLY interface (default methods only).
/// </summary>
public interface IPageTree
{
    Task<IReadOnlyList<ChildPage>> ChildrenOfAsync(Guid pageId, CancellationToken ct = default);

    /// <summary>
    /// The published, enabled children of the page at <paramref name="slug"/> — for a component that lists
    /// SOME OTHER page's children, e.g. a home page showing the projects index. Slug rather than uid because
    /// that is what an author can type into a tag attribute.
    /// The default implementation returns empty, so a host that has not implemented it degrades to an empty
    /// list rather than breaking; the CMS host overrides it. MUST never throw into a render.
    /// </summary>
    Task<IReadOnlyList<ChildPage>> ChildrenOfSlugAsync(string slug, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ChildPage>>(Array.Empty<ChildPage>());

    /// <summary>
    /// The same lookup, scoped to one site. PREFER THIS OVERLOAD: a slug is unique only within a site
    /// (the CMS keys pages on <c>(SiteId, Slug)</c>), so the slug-only form above can answer with ANOTHER
    /// domain's page when one deployment serves several sites. Pass
    /// <see cref="ISiteContext.SiteId"/> from the render context — the site uid every citizen already has.
    /// <see cref="Guid.Empty"/> means "site unknown" and falls back to the unscoped lookup.
    /// The default implementation delegates to the slug-only overload so an existing host keeps working
    /// unchanged; the CMS host overrides it. MUST never throw into a render.
    /// </summary>
    Task<IReadOnlyList<ChildPage>> ChildrenOfSlugAsync(Guid siteId, string slug, CancellationToken ct = default)
        => ChildrenOfSlugAsync(slug, ct);

    /// <summary>
    /// Returns the full sub-tree rooted at <paramref name="pageId"/> (children, grandchildren, …)
    /// as nested <see cref="ChildPageNode"/> records. Empty when the page has no descendants.
    /// Default implementation falls back to one level via <see cref="ChildrenOfAsync"/>.
    /// </summary>
    async Task<IReadOnlyList<ChildPageNode>> DescendantsTreeAsync(Guid pageId, CancellationToken ct = default)
    {
        var flat = await ChildrenOfAsync(pageId, ct);
        return flat.Select(c => new ChildPageNode(c.Slug, c.Title, Array.Empty<ChildPageNode>(), c.OpenInNewWindow)).ToList();
    }
}

/// <summary>
/// What a public demo deployment of this CMS looks like right now — everything a Component may show to
/// anyone. Deliberately carries NO credentials: those are only ever returned by the host's
/// <see cref="IDemoAccess.RevealPath"/> endpoint after a human check, never through a render.
/// </summary>
/// <param name="Url">The demo's address.</param>
/// <param name="Ready">False while the demo is resetting (its hourly wipe).</param>
/// <param name="ResetsAtUtc">When the current login stops working, if known.</param>
/// <param name="TurnstileSiteKey">The public Cloudflare Turnstile site key, or null when the reveal is off.</param>
public sealed record DemoInfo(string Url, bool Ready, DateTimeOffset? ResetsAtUtc, string? TurnstileSiteKey);

/// <summary>
/// Host-provided render seam (resolved via <see cref="IRenderContext.TryGetFeature{T}"/>): the public demo
/// of this CMS, when the host has one. A Component renders nothing when the feature is absent or
/// <see cref="GetInfoAsync"/> returns null. MUST never throw into a render. APPEND-ONLY interface.
/// </summary>
public interface IDemoAccess
{
    /// <summary>The demo's public state, or null when this host has no demo configured.</summary>
    Task<DemoInfo?> GetInfoAsync(CancellationToken ct = default);

    /// <summary>The POST endpoint a browser calls with a Turnstile token to receive the current login.</summary>
    string RevealPath => "/_demo/reveal";
}
