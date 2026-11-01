using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Plugin.BackHomeM;

/// <summary>
/// IdeaCms.Plugin.BackHomeM.V1 — the "back to mindattic.com" corner glyph as a self-contained
/// Plugin. Emits one stylesheet (base64-embedded icon @font-face, no external files) bundled in this
/// package's wwwroot/ and served under <c>/_ideas/Plugin/backhomem/1/</c>, styling the
/// <c>.back-home-m</c> anchor a Theme/page places. Instance settings (M.IDEA-§4.5) are emitted as
/// <c>--ma-bhm-*</c> custom properties the stylesheet reads with the designed values as fallbacks.
/// </summary>
public sealed class V1 : PluginBase
{
    private const string Mount = "/_ideas/Plugin/backhomem/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/back-home-m.css" };

    public enum Corners { TopLeft, TopRight, BottomLeft, BottomRight }

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Corner", Group = "Layout", Order = 1, Description = "Viewport corner the glyph is pinned to")]
    public Corners Corner { get; set; } = Corners.TopLeft;

    [Parameter, Setting("Horizontal offset", Group = "Layout", Order = 2, Description = "Distance from the side edge (designed: 5px)")]
    public string? OffsetX { get; set; }

    [Parameter, Setting("Vertical offset", Group = "Layout", Order = 3, Description = "Distance from the top/bottom edge (designed: 5px)")]
    public string? OffsetY { get; set; }

    [Parameter, Setting("Stacking order (z-index)", Group = "Layout", Order = 4, Description = "designed: 9999")]
    public int? ZIndex { get; set; }

    // ── Appearance ──────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Size", Group = "Appearance", Order = 1, Description = "font-size of the glyph (designed: 1.25rem)")]
    public string? Size { get; set; }

    [Parameter, Setting("Color", Group = "Appearance", Order = 2, Description = "designed: inherits the surrounding text color")]
    public string? Color { get; set; }

    [Parameter, Setting("Opacity", Group = "Appearance", Order = 3, Description = "0–1 at rest (designed: 0.7)")]
    public double? Opacity { get; set; }

    [Parameter, Setting("Font family", Group = "Appearance", Order = 4, Description = "designed: 'Attic', serif")]
    public string? FontFamily { get; set; }

    [Parameter, Setting("Glyph text", Group = "Content", Order = 1, Copyable = false, Description = "Text drawn by the anchor (designed: \"❮ M\")")]
    public string? Glyph { get; set; }

    // ── Behavior ────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Hover effect", Group = "Behavior", Order = 1, Description = "Brighten and grow on hover / focus (animated)")]
    public bool HoverEffect { get; set; } = true;

    [Parameter, Setting("Hover opacity", Group = "Behavior", Order = 2, Description = "0–1 (designed: 1)")]
    public double? HoverOpacity { get; set; }

    [Parameter, Setting("Hover scale", Group = "Behavior", Order = 3, Description = "Scale factor on hover (designed: 1.1)")]
    public double? HoverScale { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);

        var vars = new List<(string, string?)>();
        if (Corner != Corners.TopLeft || OffsetX is not null || OffsetY is not null)
        {
            var x = Css(OffsetX) ?? "5px";
            var y = Css(OffsetY) ?? "5px";
            var right = Corner is Corners.TopRight or Corners.BottomRight;
            var bottom = Corner is Corners.BottomLeft or Corners.BottomRight;
            vars.Add(("--ma-bhm-left", right ? "auto" : x));
            vars.Add(("--ma-bhm-right", right ? x : "auto"));
            vars.Add(("--ma-bhm-top", bottom ? "auto" : y));
            vars.Add(("--ma-bhm-bottom", bottom ? y : "auto"));
        }
        vars.Add(("--ma-bhm-z", ZIndex?.ToString(CultureInfo.InvariantCulture)));
        vars.Add(("--ma-bhm-size", Size));
        vars.Add(("--ma-bhm-color", Color));
        vars.Add(("--ma-bhm-opacity", Num(Opacity)));
        vars.Add(("--ma-bhm-font", FontFamily));
        vars.Add(("--ma-bhm-glyph", string.IsNullOrWhiteSpace(Glyph) ? null : CssString(Glyph)));
        vars.Add(("--ma-bhm-hover-opacity", HoverEffect ? Num(HoverOpacity) : "var(--ma-bhm-opacity, 0.7)"));
        vars.Add(("--ma-bhm-hover-scale", HoverEffect ? Num(HoverScale) : "1"));

        var decls = vars.Select(v => (v.Item1, Value: v.Item1 == "--ma-bhm-glyph" ? v.Item2 : Css(v.Item2)))
                        .Where(v => v.Value is not null).Select(v => v.Item1 + ":" + v.Value).ToList();
        var css = decls.Count > 0 ? "html:root{" + string.Join(";", decls) + "}" : "";
        if (!HoverEffect) css += ".back-home-m{transition:none}";
        if (css.Length > 0) builder.AddMarkupContent(1000, "<style>" + css + "</style>");
    }

    private static string? Num(double? d) => d?.ToString(CultureInfo.InvariantCulture);

    // Raw CSS text inside <style>: strip anything that could close the rule or the element.
    private static string? Css(string? v)
    {
        if (v is null) return null;
        var s = new string(v.Where(c => c is not ('{' or '}' or '<' or ';')).ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }

    // A quoted CSS string literal (for content:), with every character that could break out escaped.
    private static string CssString(string v) =>
        "\"" + string.Concat(v.Select(c => c is '"' or '\\' or '<' or '{' or '}' or ';' or '\n' or '\r'
            ? "\\" + ((int)c).ToString("X", CultureInfo.InvariantCulture) + " " : c.ToString())) + "\"";
}
