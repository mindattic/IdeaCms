using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using BlazorComponentBase = Microsoft.AspNetCore.Components.ComponentBase;  // alias: MindAttic's ComponentBase wins the bare name (M.IDEA-LAW-1)

namespace MindAttic.Ideas.Abstractions;

// ============================================================================================
//  THE INHERITANCE ROOTS — IdeaBase is the shared root every content type derives from; the four
//  kind bases give each kind its shape. The kind is determined by which base you inherit. Inheriting
//  is how a type becomes content; the .idea package carries it. GROW ONLY by adding NON-ABSTRACT
//  members; never add an abstract member (it would break every existing subclass + shipped package).
// ============================================================================================

/// <summary>The shared root. Everything an .idea contains derives (transitively) from this.</summary>
public abstract class IdeaBase : BlazorComponentBase
{
    /// <summary>The render context for this placement (provided by the host via a cascade).</summary>
    [CascadingParameter] protected IRenderContext Context { get; set; } = default!;
    protected bool EditMode => Context?.Mode == ContentMode.Edit;

    // ── URL safety ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the URL unchanged if it is safe to render in an href/src/action attribute.
    /// Returns "#" for javascript:, data:, vbscript: and any other scheme that could execute
    /// client-side code. Relative URLs (/path, #anchor, ./rel), http:, https:, mailto:, tel:
    /// all pass through. Call on every untrusted URL attribute before rendering.
    /// </summary>
    protected static string SafeUrl(string? url) => IsUnsafeUrl(url) ? "#" : (url ?? "#");

    /// <summary>True if the URL could execute script or embed attacker-controlled data.</summary>
    protected static bool IsUnsafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var t = url.TrimStart();
        return t.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase);
    }

    // ── Instance settings → client script (M.IDEA-§4.5) ─────────────────────────────────────────────

    /// <summary>
    /// This instance's current setting values (its simple-typed [Parameter]s) as a camelCase JSON object.
    /// </summary>
    protected string CurrentSettingsJson()
    {
        var values = new Dictionary<string, object?>();
        foreach (var p in GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite) continue;
            if (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<ParameterAttribute>(p) is not { CaptureUnmatchedValues: false }) continue;
            if (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<SettingAttribute>(p) is { Hidden: true }) continue;
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (!(t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal))) continue;
            var v = p.GetValue(this);
            values[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = t.IsEnum && v is not null ? v.ToString() : v;
        }
        return System.Text.Json.JsonSerializer.Serialize(values);
    }

    /// <summary>
    /// Emits <c>&lt;data hidden data-ma-settings="{name}" value="{json}"&gt;</c> so this citizen's client
    /// script can read its instance settings live (<c>document.querySelector('[data-ma-settings="…"]')</c>).
    /// An attribute rather than a &lt;script&gt; on purpose: Blazor re-renders it on in-circuit navigation,
    /// whereas an inserted script never re-runs.
    /// </summary>
    protected void AddSettingsData(RenderTreeBuilder builder, int sequence, string name)
    {
        builder.OpenElement(sequence, "data");
        builder.AddAttribute(sequence + 1, "hidden", true);
        builder.AddAttribute(sequence + 2, "data-ma-settings", name);
        builder.AddAttribute(sequence + 3, "value", CurrentSettingsJson());
        builder.CloseElement();
    }
}

// ---- Page ----

/// <summary>Base for a compiled (Code) Page. e.g. <c>IdeaCms.Page.LegionFrontpage.V2</c>.</summary>
public abstract class PageBase : IdeaBase { }

/// <summary>Base for a compiled Page with strongly-typed settings.</summary>
public abstract class PageBase<TSettings> : PageBase where TSettings : class, new()
{
    protected TSettings Settings { get; private set; } = new();
    protected override void OnParametersSet() => Settings = Context?.GetSettings<TSettings>() ?? new TSettings();
}

// ---- Theme ----

/// <summary>
/// Base for a Theme: layout chrome with a SINGLE <see cref="Body"/> hole — NO zones. CSS/script URL
/// lists map onto the fixed cascade (Global -> Theme tiers). e.g. <c>IdeaCms.Theme.Cyberspace.V4</c>.
/// </summary>
public abstract class ThemeBase : IdeaBase
{
    /// <summary>The page's free-form content renders here. The only hole; never a zone grid.</summary>
    [Parameter] public RenderFragment? Body { get; set; }

    /// <summary>Cascade tier "global" CSS (e.g. fonts), emitted before <see cref="ThemeCssUrls"/>.</summary>
    public virtual IReadOnlyList<string> GlobalCssUrls => Array.Empty<string>();

    /// <summary>Cascade tier "theme" CSS. Mirror the UiUx deps.json css[] at a pinned tag.</summary>
    public virtual IReadOnlyList<string> ThemeCssUrls => Array.Empty<string>();

    /// <summary>Theme scripts (e.g. Cyberspace effect loaders). Mirror deps.json scripts[].</summary>
    public virtual IReadOnlyList<string> ScriptUrls => Array.Empty<string>();

    /// <summary>Optional raw HTML injected at the top of the theme body (e.g. effect layers).</summary>
    public virtual string? BodyPreludeHtml => null;

    /// <summary>Padding of the theme's <c>.page</c> wrapper for this page (instance setting, M.IDEA-§4.5).</summary>
    [Parameter, Setting("Page padding", Group = "Layout", Order = 1, Description = "CSS padding shorthand of .page")]
    public string? PagePadding { get; set; } = ".75rem 1rem";

    /// <summary>Margin of the theme's <c>.page</c> wrapper for this page (instance setting, M.IDEA-§4.5).</summary>
    [Parameter, Setting("Page margin", Group = "Layout", Order = 2, Description = "CSS margin shorthand of .page")]
    public string? PageMargin { get; set; }

    /// <summary>The inline style a theme puts on its <c>.page</c> wrapper: <c>&lt;div class="page" style="@PageStyle"&gt;</c>.</summary>
    protected string? PageStyle
    {
        get
        {
            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(PagePadding)) parts.Add("padding:" + PagePadding);
            if (!string.IsNullOrWhiteSpace(PageMargin)) parts.Add("margin:" + PageMargin);
            return parts.Count == 0 ? null : string.Join(";", parts);
        }
    }
}

// ---- Plugin (site-wide .idea: activates a behavior/capability across the whole page) ----

/// <summary>
/// Base for a Plugin: a site-wide .idea that activates a behavior or capability across the entire
/// rendered page without occupying a specific token position. Plugins are selected per-page via the
/// Admin Page Properties Plugin checkbox list; they may also be dropped inline via
/// <c>&lt;Plugin.X /&gt;</c> for one-off pages. e.g. dropping
/// <c>IdeaCms.Plugin.Tooltip.V1</c> loads tooltip css/js so ANY element with
/// <c>data-tooltip</c>/<c>data-tt</c> shows a tooltip on hover.
///
/// By default renders no markup — emits <see cref="StylesheetUrls"/> as &lt;link&gt; and
/// <see cref="ScriptUrls"/> as &lt;script&gt;. Override <c>BuildRenderTree</c> to add markup.
/// Declare typed <c>[Parameter]</c> properties for configuration; unmatched attributes land in
/// <see cref="Attributes"/>.
/// </summary>
public abstract class PluginBase : IdeaBase
{
    /// <summary>
    /// Config attributes from the include tag that don't match a typed <c>[Parameter]</c> property.
    /// Never throws on an unknown attribute.
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? Attributes { get; set; }

    /// <summary>Stylesheets this plugin needs (jsDelivr or host-relative). Emitted once.</summary>
    public virtual IReadOnlyList<string> StylesheetUrls => Array.Empty<string>();

    /// <summary>Scripts this plugin needs (the behavior engine). Emitted once.</summary>
    public virtual IReadOnlyList<string> ScriptUrls => Array.Empty<string>();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var seq = 0;
        foreach (var css in StylesheetUrls)
        {
            builder.OpenElement(seq++, "link");
            builder.AddAttribute(seq++, "rel", "stylesheet");
            builder.AddAttribute(seq++, "href", css);
            builder.CloseElement();
        }
        foreach (var js in ScriptUrls)
        {
            builder.OpenElement(seq++, "script");
            builder.AddAttribute(seq++, "src", js);
            builder.CloseElement();
        }
    }
}

// ---- Component (inline-placed .idea: renders at the <Component.X /> tag position; can nest) ----

/// <summary>
/// Base for a Component: an inline-placed .idea that renders at the exact
/// <c>&lt;Component.X /&gt;</c> tag position in the page body. Components can nest other Components,
/// enabling composite UIs — e.g. <c>IdeaCms.Component.TabControl</c> nests
/// <c>Component.TabButtonContainer</c>, <c>Component.TabButton</c> instances,
/// <c>Component.TabPageContainer</c>, and <c>Component.TabPage</c> instances (each of which may
/// contain <c>Component.Textbox</c> or other children). Declare sub-component dependencies with
/// <c>[Uses]</c>/<c>uses[]</c>.
///
/// NOTE: <c>ComponentBase</c> here is <c>MindAttic.Ideas.Abstractions.ComponentBase</c>, NOT Blazor's
/// <c>Microsoft.AspNetCore.Components.ComponentBase</c> — the MindAttic kind wins the bare name.
/// Blazor's base is aliased as <c>BlazorComponentBase</c> in this file (see M.IDEA-LAW-1).
/// </summary>
public abstract class ComponentBase : IdeaBase
{
    /// <summary>
    /// Config attributes from the include tag that don't match a typed <c>[Parameter]</c> property.
    /// Never throws on an unknown attribute.
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? Attributes { get; set; }

    /// <summary>Stylesheets this component needs (jsDelivr or host-relative). Emitted once.</summary>
    public virtual IReadOnlyList<string> StylesheetUrls => Array.Empty<string>();

    /// <summary>Scripts this component needs. Emitted once.</summary>
    public virtual IReadOnlyList<string> ScriptUrls => Array.Empty<string>();

    /// <summary>
    /// Opt-in: true to isolate this component's rendered markup inside a real browser Shadow DOM shadow
    /// root (mode "open"), so Page/Theme CSS cannot reach in via ordinary selectors. Default false --
    /// every existing Component citizen is unaffected. A component that opts in must give its `@ref`'d
    /// root element structurally STABLE immediate children (no top-level `@if`/element-type-swap
    /// directly under the ref) -- see docs/AUTHORING.md "Shadow DOM isolation" for the attach recipe,
    /// the authoring rule, and why.
    /// </summary>
    protected virtual bool UseShadowDom => false;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var seq = 0;
        foreach (var css in StylesheetUrls)
        {
            builder.OpenElement(seq++, "link");
            builder.AddAttribute(seq++, "rel", "stylesheet");
            builder.AddAttribute(seq++, "href", css);
            builder.CloseElement();
        }
        foreach (var js in ScriptUrls)
        {
            builder.OpenElement(seq++, "script");
            builder.AddAttribute(seq++, "src", js);
            builder.CloseElement();
        }
    }
}

// ---- (ContentKind ordinal 3 is reserved and never reused — M.IDEA-LAW-2. Author atomic UI as a Component.) ----
