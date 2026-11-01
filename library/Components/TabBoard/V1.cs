using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MindAttic.Ideas.Abstractions;
using ComponentBase = MindAttic.Ideas.Abstractions.ComponentBase;

namespace IdeaCms.Component.TabBoard;

/// <summary>
/// IdeaCms.Component.TabBoard.V1 — the mindattic.com project-board engine as an asset-only
/// activator, extracted VERBATIM from mindattic.com/index.htm (the `.tabButton`/`.tabPage` styles
/// and the board script). Drop the token once and authored board markup is wired — tiles in a
/// wrapping grid, single-active full-row panels, procedural tile art for placeholders, per-section
/// localStorage persistence:
/// <code>
///   &lt;div class="board-section"&gt;&lt;div class="board-grid"&gt;
///     &lt;button type="button" class="tabButton" data-target="x"&gt;
///       &lt;div class="tabButton-name"&gt;Name&lt;/div&gt;&lt;/button&gt;
///     &lt;div class="tabPage" id="x"&gt;…&lt;/div&gt;
///   &lt;/div&gt;&lt;/div&gt;
/// </code>
/// Page scripts can also BUILD boards from data via <c>window.TabBoard.build(items)</c> /
/// <c>TabBoard.art(name)</c> / <c>TabBoard.images</c> (how the frontpage tabifies its Portfolio
/// links and books grids).
///
/// PLACEMENT OPTIONS: <c>&lt;Component.TabBoard alwaysShowTabPage="true" /&gt;</c> keeps a
/// selected item always visible — re-clicking the open tab no longer collapses it, and a board with
/// no saved selection opens its first tab. The selection persists per section in localStorage
/// either way (the engine's verbatim behavior).
/// </summary>
public sealed class V1 : ComponentBase
{
    private const string Mount = "/_ideas/Component/tabboard/1";

    /// <summary>
    /// "true" =&gt; every board keeps one tab open (no fully-collapsed state). Declared as object so
    /// both token forms bind: <c>alwaysShowTabPage=true</c> (string) and a bare attribute (bool).
    /// </summary>
    [Parameter] public object? AlwaysShowTabPage { get; set; }

    // ── Behavior (handed to the engine through window.TabBoardConfig) ─────────────────────────────
    [Parameter, Setting("Remember open tab", Group = "Behavior", Order = 1, Description = "Persist each board's selection in localStorage")]
    public bool Persist { get; set; } = true;

    [Parameter, Setting("Generate placeholder art", Group = "Behavior", Order = 2, Description = "Procedural tile art for .tabPage-img--placeholder images")]
    public bool GenerateArt { get; set; } = true;

    [Parameter, Setting("Built-board link label", Group = "Behavior", Order = 3, Description = "Button label for TabBoard.build() items without a linkLabel")]
    public string LinkLabel { get; set; } = "Open";

    [Parameter, Setting("Built-board links in new tab", Group = "Behavior", Order = 4)]
    public bool LinkNewTab { get; set; } = true;

    [Parameter, Setting("Transitions", Group = "Behavior", Order = 5, Description = "Tile and button hover transitions")]
    public bool Animate { get; set; } = true;

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Tile gap", Group = "Layout", Order = 1, Description = "Default 12px")] public string? Gap { get; set; }
    [Parameter, Setting("Tile alignment", Group = "Layout", Order = 2, Description = "flex-start | center | flex-end | space-between")] public string? TileAlign { get; set; }
    [Parameter, Setting("Tile padding", Group = "Layout", Order = 3, Description = "Default 5px")] public string? TilePadding { get; set; }
    [Parameter, Setting("Tile corner radius", Group = "Layout", Order = 4, Description = "Default 8px")] public string? TileRadius { get; set; }
    [Parameter, Setting("Tile name font size", Group = "Layout", Order = 5, Description = "Default 1rem")] public string? NameSize { get; set; }
    [Parameter, Setting("Panel padding", Group = "Layout", Order = 6, Description = "Default 18px 24px")] public string? PanelPadding { get; set; }
    [Parameter, Setting("Panel corner radius", Group = "Layout", Order = 7, Description = "Default 10px")] public string? PanelRadius { get; set; }
    [Parameter, Setting("Panel font size", Group = "Layout", Order = 8, Description = "Default 0.9rem")] public string? PanelFontSize { get; set; }
    [Parameter, Setting("Panel image width", Group = "Layout", Order = 9, Description = "Default 150px")] public string? ImageWidth { get; set; }
    [Parameter, Setting("Panel image height", Group = "Layout", Order = 10, Description = "Default 225px")] public string? ImageHeight { get; set; }
    [Parameter, Setting("Font family", Group = "Layout", Order = 11, Description = "Default 'Outfit', system-ui, sans-serif")] public string? FontFamily { get; set; }

    // ── Colors ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Accent color", Group = "Colors", Order = 1, Description = "Hover/open tile border (default #dc3545)")] public string? AccentColor { get; set; }
    [Parameter, Setting("Accent glow", Group = "Colors", Order = 2, Description = "Open tile glow (default rgba(220,53,69,.25))")] public string? AccentGlow { get; set; }
    [Parameter, Setting("Tile background", Group = "Colors", Order = 3)] public string? TileBackground { get; set; }
    [Parameter, Setting("Tile hover background", Group = "Colors", Order = 4)] public string? TileHoverBackground { get; set; }
    [Parameter, Setting("Tile text color", Group = "Colors", Order = 5)] public string? TileColor { get; set; }
    [Parameter, Setting("Border color", Group = "Colors", Order = 6)] public string? BorderColor { get; set; }
    [Parameter, Setting("Section label color", Group = "Colors", Order = 7)] public string? MutedColor { get; set; }
    [Parameter, Setting("Divider color", Group = "Colors", Order = 8)] public string? DividerColor { get; set; }
    [Parameter, Setting("Panel background", Group = "Colors", Order = 9)] public string? PanelBackground { get; set; }
    [Parameter, Setting("Panel text color", Group = "Colors", Order = 10)] public string? PanelColor { get; set; }
    [Parameter, Setting("Panel link color", Group = "Colors", Order = 11)] public string? LinkColor { get; set; }
    [Parameter, Setting("Panel shadow", Group = "Colors", Order = 12, Description = "CSS box-shadow; 'none' to remove")] public string? PanelShadow { get; set; }
    [Parameter, Setting("Panel backdrop blur", Group = "Colors", Order = 13, Description = "Default 8px")] public string? PanelBlur { get; set; }

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/tabboard.css" };
    public override IReadOnlyList<string> ScriptUrls { get; } = new[] { Mount + "/tabboard.js" };

    // A value lands inside a <style> block, so anything that could close the rule or the element is refused.
    private static string? Css(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '<', '>', '{', '}', ';' }) >= 0
            ? null : $"{name}:{value.Trim()};";

    private string BuildVars() => string.Concat(new[]
    {
        Css("--ma-tb-gap", Gap), Css("--ma-tb-align", TileAlign), Css("--ma-tb-tile-padding", TilePadding),
        Css("--ma-tb-tile-radius", TileRadius), Css("--ma-tb-name-size", NameSize),
        Css("--ma-tb-panel-padding", PanelPadding), Css("--ma-tb-panel-radius", PanelRadius),
        Css("--ma-tb-panel-font-size", PanelFontSize), Css("--ma-tb-img-width", ImageWidth),
        Css("--ma-tb-img-height", ImageHeight), Css("--ma-tb-font", FontFamily),
        Css("--ma-tb-accent", AccentColor), Css("--ma-tb-accent-glow", AccentGlow),
        Css("--ma-tb-tile-bg", TileBackground), Css("--ma-tb-tile-hover-bg", TileHoverBackground),
        Css("--ma-tb-tile-color", TileColor), Css("--ma-tb-border", BorderColor),
        Css("--ma-tb-muted", MutedColor), Css("--ma-tb-divider", DividerColor),
        Css("--ma-tb-panel-bg", PanelBackground), Css("--ma-tb-panel-color", PanelColor),
        Css("--ma-tb-link", LinkColor), Css("--ma-tb-panel-shadow", PanelShadow),
        Css("--ma-tb-panel-blur", PanelBlur),
    }.Where(s => s is not null));

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);   // the asset-emitting render (link + script)

        // Visual settings: the engine styles authored markup page-wide, so they are page-wide custom
        // properties. Emitted only when something is set — no settings, no extra markup.
        var vars = BuildVars();
        if (vars.Length > 0 || !Animate)
        {
            builder.OpenElement(90, "style");
            builder.AddMarkupContent(91,
                (vars.Length > 0 ? ":root{" + vars + "}" : "")
                + (Animate ? "" : ".tabButton,.tabButton-btn{transition:none!important}"));
            builder.CloseElement();
        }

        // Behavior settings: only the ones that differ from the engine's verbatim behavior.
        var js = new List<string>();
        if (!Persist) js.Add("persist: false");
        if (!GenerateArt) js.Add("generateArt: false");
        if (!LinkNewTab) js.Add("linkNewTab: false");
        if (!string.IsNullOrEmpty(LinkLabel) && LinkLabel != "Open")
            js.Add("linkLabel: " + System.Text.Json.JsonSerializer.Serialize(LinkLabel));
        if (js.Count > 0)
        {
            builder.OpenElement(95, "script");
            builder.AddMarkupContent(96,
                "window.TabBoardConfig = Object.assign(window.TabBoardConfig || {}, { " + string.Join(", ", js) + " });");
            builder.CloseElement();
        }

        // Hand the placement option to the engine. The engine reads window.TabBoardConfig lazily
        // (at wire time), so the relative order of this inline script and the engine script never
        // matters. Emitted only when the author set the attribute.
        if (AlwaysShowTabPage is not null)
        {
            var on = string.Equals(AlwaysShowTabPage.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            builder.OpenElement(100, "script");
            builder.AddMarkupContent(101,
                "window.TabBoardConfig = Object.assign(window.TabBoardConfig || {}, { alwaysShowTabPage: "
                + (on ? "true" : "false") + " });");
            builder.CloseElement();
        }
    }
}
