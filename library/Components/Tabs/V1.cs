using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using IdeaCms.Abstractions;
using ComponentBase = IdeaCms.Abstractions.ComponentBase;

namespace IdeaCms.Component.Tabs;

/// <summary>
/// IdeaCms.Component.Tabs.V1 — the tabbed-content capability as an asset-only activator (the
/// Tooltip model). Drop the token once and ANY <c>&lt;div class="ma-tabs"&gt;</c> whose child sections
/// carry <c>data-title</c> becomes a wired, accessible tab control — the script builds the tablist
/// from the titles, so the page author writes only content:
/// <code>
///   &lt;div class="ma-tabs"&gt;
///     &lt;section data-title="One"&gt;…&lt;/section&gt;
///     &lt;section data-title="Two"&gt;…&lt;/section&gt;
///   &lt;/div&gt;
/// </code>
/// Add <c>ma-tabs-board</c> for the mindattic.com tab-board look: a wrapping grid of fixed-size tiles
/// with the open panel claiming the full row below.
/// Instance settings (M.IDEA-§4.5) are page-wide — the activator styles every .ma-tabs on the page — and
/// are emitted only when set: CSS custom properties in a &lt;style&gt;, behavior on window.MaTabsConfig.
/// </summary>
public sealed class V1 : ComponentBase
{
    private const string Mount = "/_ideas/Component/tabs/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/tabs.css" };
    public override IReadOnlyList<string> ScriptUrls { get; } = new[] { Mount + "/tabs.js" };

    // ── Behavior ────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Start with all tabs closed", Group = "Behavior", Order = 1, Description = "As if every .ma-tabs had data-closed; clicking the open tab closes it")]
    public bool StartClosed { get; set; }

    [Parameter, Setting("Keyboard navigation", Group = "Behavior", Order = 2, Description = "Left/Right/Home/End move between tabs")]
    public bool Keyboard { get; set; } = true;

    [Parameter, Setting("Transitions", Group = "Behavior", Order = 3, Description = "Board tile hover lift and transitions")]
    public bool Animate { get; set; } = true;

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Tab padding", Group = "Layout", Order = 1, Description = "Default .55rem .9rem")] public string? TabPadding { get; set; }
    [Parameter, Setting("Tab gap", Group = "Layout", Order = 2, Description = "Default .25rem (.6rem on boards)")] public string? Gap { get; set; }
    [Parameter, Setting("Tab list alignment", Group = "Layout", Order = 3, Description = "flex-start | center | flex-end")] public string? ListAlign { get; set; }
    [Parameter, Setting("Space below tab list", Group = "Layout", Order = 4, Description = "Default 1rem")] public string? ListSpacing { get; set; }
    [Parameter, Setting("Board tile min width", Group = "Layout", Order = 5, Description = "Default 9.5rem")] public string? TileMinWidth { get; set; }
    [Parameter, Setting("Board tile min height", Group = "Layout", Order = 6, Description = "Default 4.25rem")] public string? TileMinHeight { get; set; }
    [Parameter, Setting("Board corner radius", Group = "Layout", Order = 7, Description = "Default .5rem")] public string? Radius { get; set; }
    [Parameter, Setting("Board panel padding", Group = "Layout", Order = 8, Description = "Default 1rem")] public string? PanelPadding { get; set; }
    [Parameter, Setting("Inactive tab opacity", Group = "Layout", Order = 9, Description = "Default .7")] public string? InactiveOpacity { get; set; }

    // ── Colors ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Accent color", Group = "Colors", Order = 1, Description = "Selected tab (default #4a6cf7)")] public string? AccentColor { get; set; }
    [Parameter, Setting("Border color", Group = "Colors", Order = 2, Description = "Default rgba(128,128,128,.35)")] public string? BorderColor { get; set; }
    [Parameter, Setting("Board tile background", Group = "Colors", Order = 3, Description = "Default rgba(128,128,128,.06)")] public string? TileBackground { get; set; }

    // A value lands inside a <style> block, so anything that could close the rule or the element is refused.
    private static string? Css(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '<', '>', '{', '}', ';' }) >= 0
            ? null : $"{name}:{value.Trim()};";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);   // the asset-emitting render (link + script)

        var vars = string.Concat(new[]
        {
            Css("--ma-tabs-accent", AccentColor), Css("--ma-tabs-border", BorderColor),
            Css("--ma-tabs-tab-padding", TabPadding), Css("--ma-tabs-gap", Gap),
            Css("--ma-tabs-align", ListAlign), Css("--ma-tabs-list-spacing", ListSpacing),
            Css("--ma-tabs-tile-min", TileMinWidth), Css("--ma-tabs-tile-height", TileMinHeight),
            Css("--ma-tabs-radius", Radius), Css("--ma-tabs-panel-padding", PanelPadding),
            Css("--ma-tabs-inactive-opacity", InactiveOpacity), Css("--ma-tabs-tile-bg", TileBackground),
        }.Where(s => s is not null));

        if (vars.Length > 0 || !Animate)
        {
            builder.OpenElement(10, "style");
            // :root .ma-tabs outranks the sheet's own .ma-tabs defaults regardless of load order.
            builder.AddMarkupContent(11,
                (vars.Length > 0 ? ":root .ma-tabs{" + vars + "}" : "")
                + (Animate ? "" : ".ma-tabs .ma-tabs-tab{transition:none!important;transform:none!important}"));
            builder.CloseElement();
        }

        var js = new List<string>();
        if (StartClosed) js.Add("startClosed: true");
        if (!Keyboard) js.Add("keyboard: false");
        if (js.Count > 0)
        {
            builder.OpenElement(20, "script");
            builder.AddMarkupContent(21,
                "window.MaTabsConfig = Object.assign(window.MaTabsConfig || {}, { " + string.Join(", ", js) + " });");
            builder.CloseElement();
        }
    }
}
