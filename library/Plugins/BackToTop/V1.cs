using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Plugin.BackToTop;

/// <summary>
/// IdeaCms.Plugin.BackToTop.V1 — the scroll-to-top capability as an asset-only activator.
/// Drop the token once and a floating button appears in the bottom-right corner after the visitor
/// scrolls one screen down; clicking it smooth-scrolls back to the top. Zero author markup — the
/// script creates the button itself (the arrow is a CSS glyph; no images, no requests).
/// Instance settings (M.IDEA-§4.5): look is emitted as <c>--ma-btt-*</c> custom properties; behavior is
/// published to the script through <c>data-ma-settings="plugin.backtotop"</c>.
/// </summary>
public sealed class V1 : PluginBase
{
    private const string Mount = "/_ideas/Plugin/backtotop/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/backtotop.css" };
    public override IReadOnlyList<string> ScriptUrls { get; } = new[] { Mount + "/backtotop.js" };

    public enum Sides { Right, Left }

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Side", Group = "Layout", Order = 1, Description = "Bottom corner the button sits in")]
    public Sides Side { get; set; } = Sides.Right;

    [Parameter, Setting("Side offset", Group = "Layout", Order = 2, Description = "Distance from the side edge (designed: 1.25rem)")]
    public string? OffsetX { get; set; }

    [Parameter, Setting("Bottom offset", Group = "Layout", Order = 3, Description = "Distance from the bottom edge (designed: 1.25rem)")]
    public string? OffsetY { get; set; }

    [Parameter, Setting("Size", Group = "Layout", Order = 4, Description = "Width and height (designed: 2.9rem)")]
    public string? Size { get; set; }

    [Parameter, Setting("Stacking order (z-index)", Group = "Layout", Order = 5, Description = "designed: 9000")]
    public int? ZIndex { get; set; }

    // ── Appearance ──────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Background", Group = "Appearance", Order = 1, Description = "designed: rgba(0,0,0,.55)")]
    public string? Background { get; set; }

    [Parameter, Setting("Arrow color", Group = "Appearance", Order = 2, Description = "designed: #fff")]
    public string? Color { get; set; }

    [Parameter, Setting("Corner radius", Group = "Appearance", Order = 3, Description = "designed: 50% (a circle)")]
    public string? Radius { get; set; }

    [Parameter, Setting("Opacity", Group = "Appearance", Order = 4, Description = "0–1 while shown (designed: 0.85; 1 on hover)")]
    public double? Opacity { get; set; }

    [Parameter, Setting("Shadow", Group = "Appearance", Order = 5, Description = "CSS box-shadow (designed: none)")]
    public string? Shadow { get; set; }

    // ── Behavior ────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Show after (screens)", Group = "Behavior", Order = 1, Description = "Scroll distance, in viewport heights, before the button appears")]
    public double ShowAfterScreens { get; set; } = 1;

    [Parameter, Setting("Smooth scroll", Group = "Behavior", Order = 2, Description = "Animate the scroll back to the top")]
    public bool SmoothScroll { get; set; } = true;

    [Parameter, Setting("Animations", Group = "Behavior", Order = 3, Description = "Fade / slide the button in and out")]
    public bool Animations { get; set; } = true;

    [Parameter, Setting("Label", Group = "Content", Order = 1, Copyable = false, Description = "Accessible label / tooltip (designed: \"Back to top\")")]
    public string? Label { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);

        var decls = new List<string>();
        void Add(string prop, string? value) { if (Css(value) is { } v) decls.Add(prop + ":" + v); }
        var x = Css(OffsetX);
        if (Side == Sides.Left) { Add("--ma-btt-left", x ?? "1.25rem"); Add("--ma-btt-right", "auto"); }
        else Add("--ma-btt-right", x);
        Add("--ma-btt-bottom", OffsetY);
        Add("--ma-btt-size", Size);
        Add("--ma-btt-z", ZIndex?.ToString(CultureInfo.InvariantCulture));
        Add("--ma-btt-bg", Background);
        Add("--ma-btt-color", Color);
        Add("--ma-btt-radius", Radius);
        Add("--ma-btt-opacity", Opacity is { } o ? Math.Clamp(o, 0, 1).ToString(CultureInfo.InvariantCulture) : null);

        var css = decls.Count > 0 ? "html:root{" + string.Join(";", decls) + "}" : "";
        if (Css(Shadow) is { } sh) css += ".ma-backtotop{box-shadow:" + sh + "}";
        if (css.Length > 0) builder.AddMarkupContent(1000, "<style>" + css + "</style>");

        AddSettingsData(builder, 1001, "plugin.backtotop");
    }

    // Raw CSS text inside <style>: strip anything that could close the rule or the element.
    private static string? Css(string? v)
    {
        if (v is null) return null;
        var s = new string(v.Where(c => c is not ('{' or '}' or '<' or ';')).ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }
}
