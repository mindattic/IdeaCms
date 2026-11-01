using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using IdeaCms.Abstractions;

namespace IdeaCms.Plugin.Tooltip;

/// <summary>
/// IdeaCms.Plugin.Tooltip.V1 — the hover-tooltip capability as a self-contained Plugin (the
/// .idea target of the UiUx Tooltip source). Dropping its tag loads the bundled tooltip css/js (served
/// from /_ideas/Plugin/tooltip/1/), so thereafter ANY element with <c>data-tooltip</c>/<c>data-tt</c>
/// shows a tooltip on hover. Instance settings (M.IDEA-§4.5): look overrides the stylesheet's
/// <c>--ma-tooltip-*</c> tokens; behavior reaches tooltip.js through <c>data-ma-settings="plugin.tooltip"</c>.
/// Per-trigger attributes (data-tooltip-pos / -delay) still win over these page-wide defaults.
/// </summary>
public sealed class V1 : PluginBase
{
    private const string Mount = "/_ideas/Plugin/tooltip/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/tooltip.css" };
    public override IReadOnlyList<string> ScriptUrls { get; } = new[] { Mount + "/tooltip.js" };

    public enum Positions { Top, Bottom, Left, Right }

    // ── Colors ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Background", Group = "Colors", Order = 1, Description = "--ma-tooltip-bg (designed: #161b22)")]
    public string? Background { get; set; }

    [Parameter, Setting("Text color", Group = "Colors", Order = 2, Description = "--ma-tooltip-fg (designed: #e6edf3)")]
    public string? TextColor { get; set; }

    [Parameter, Setting("Border color", Group = "Colors", Order = 3, Description = "--ma-tooltip-border (designed: #dc3545)")]
    public string? BorderColor { get; set; }

    // ── Appearance ──────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Corner radius", Group = "Appearance", Order = 1, Description = "designed: 7px")]
    public string? Radius { get; set; }

    [Parameter, Setting("Shadow", Group = "Appearance", Order = 2, Description = "CSS box-shadow (designed: a soft double drop shadow); 'none' to remove")]
    public string? Shadow { get; set; }

    [Parameter, Setting("Max width", Group = "Appearance", Order = 3, Description = "designed: 280px")]
    public string? MaxWidth { get; set; }

    [Parameter, Setting("Padding", Group = "Appearance", Order = 4, Description = "designed: 7px 11px")]
    public string? Padding { get; set; }

    [Parameter, Setting("Font family", Group = "Appearance", Order = 5, Description = "designed: 'Outfit', system-ui, sans-serif")]
    public string? FontFamily { get; set; }

    [Parameter, Setting("Font size", Group = "Appearance", Order = 6, Description = "designed: 0.82rem")]
    public string? FontSize { get; set; }

    [Parameter, Setting("Font weight", Group = "Appearance", Order = 7, Description = "designed: 500")]
    public int? FontWeight { get; set; }

    [Parameter, Setting("Show arrow", Group = "Appearance", Order = 8, Description = "The small pointer toward the trigger")]
    public bool ShowArrow { get; set; } = true;

    // ── Behavior ────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Default position", Group = "Behavior", Order = 1, Description = "Preferred side when a trigger has no data-tooltip-pos (auto-flips to stay on screen)")]
    public Positions DefaultPosition { get; set; } = Positions.Top;

    [Parameter, Setting("Show delay (ms)", Group = "Behavior", Order = 2, Description = "Delay when a trigger has no data-tooltip-delay")]
    public int Delay { get; set; }

    [Parameter, Setting("Gap from trigger (px)", Group = "Behavior", Order = 3)]
    public int Gap { get; set; } = 10;

    [Parameter, Setting("Viewport edge margin (px)", Group = "Behavior", Order = 4)]
    public int Edge { get; set; } = 6;

    [Parameter, Setting("Animations", Group = "Behavior", Order = 5, Description = "Fade / scale the tooltip in and out")]
    public bool Animations { get; set; } = true;

    [Parameter, Setting("Show on keyboard focus", Group = "Behavior", Order = 6)]
    public bool ShowOnFocus { get; set; } = true;

    [Parameter, Setting("Hide on Escape", Group = "Behavior", Order = 7)]
    public bool HideOnEscape { get; set; } = true;

    [Parameter, Setting("Allow HTML tooltips", Group = "Advanced", Order = 1, Description = "Honor data-tooltip-html (trusted HTML); off renders every tooltip as plain text")]
    public bool AllowHtml { get; set; } = true;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);

        var decls = new List<string>();
        void Add(string prop, string? value) { if (Css(value) is { } v) decls.Add(prop + ":" + v); }
        Add("--ma-tooltip-bg", Background);
        Add("--ma-tooltip-fg", TextColor);
        Add("--ma-tooltip-border", BorderColor);
        Add("--ma-tooltip-radius", Radius);
        Add("--ma-tooltip-shadow", Shadow);
        Add("--ma-tooltip-maxw", MaxWidth);
        Add("--ma-tooltip-pad", Padding);
        Add("--ma-tooltip-font", FontFamily);
        Add("--ma-tooltip-fontsz", FontSize);
        Add("--ma-tooltip-weight", FontWeight is { } w ? Math.Clamp(w, 100, 900).ToString(CultureInfo.InvariantCulture) : null);

        var css = decls.Count > 0 ? "html:root{" + string.Join(";", decls) + "}" : "";
        if (!ShowArrow) css += ".ma-tooltip__arrow{display:none}";
        if (!Animations) css += ".ma-tooltip{transition:none;transform:none}";
        if (css.Length > 0) builder.AddMarkupContent(1000, "<style>" + css + "</style>");

        AddSettingsData(builder, 1001, "plugin.tooltip");
    }

    // Raw CSS text inside <style>: strip anything that could close the rule or the element.
    private static string? Css(string? v)
    {
        if (v is null) return null;
        var s = new string(v.Where(c => c is not ('{' or '}' or '<' or ';')).ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }
}
