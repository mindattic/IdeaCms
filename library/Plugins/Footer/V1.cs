using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using IdeaCms.Abstractions;

namespace IdeaCms.Plugin.Footer;

/// <summary>
/// IdeaCms.Plugin.Footer.V1 — the site-footer capability as an asset-only activator. Drop the
/// token once and ANY <c>&lt;footer class="ma-footer"&gt;…&lt;/footer&gt;</c> the author writes gets the
/// footer chrome plus the mindattic.com <em>pin-when-short</em> behavior: when the page content is
/// shorter than the viewport the footer pins to the bottom edge; when content is taller it flows
/// normally after the content (never overlaps). The author keeps full control of the footer's HTML.
/// Instance settings (M.IDEA-§4.5): chrome is emitted as <c>--ma-footer-*</c> custom properties; the pin
/// toggle reaches footer.js through <c>data-ma-settings="plugin.footer"</c>.
/// </summary>
public sealed class V1 : PluginBase
{
    private const string Mount = "/_ideas/Plugin/footer/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/footer.css" };
    public override IReadOnlyList<string> ScriptUrls { get; } = new[] { Mount + "/footer.js" };

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Padding", Group = "Layout", Order = 1, Description = "designed: 1.25rem 1rem")]
    public string? Padding { get; set; }

    [Parameter, Setting("Space above", Group = "Layout", Order = 2, Description = "margin-top (designed: 2rem)")]
    public string? MarginTop { get; set; }

    [Parameter, Setting("Alignment", Group = "Layout", Order = 3, Description = "text-align (designed: center)")]
    public string? Align { get; set; }

    // ── Appearance ──────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Font size", Group = "Appearance", Order = 1, Description = "designed: .85rem")]
    public string? FontSize { get; set; }

    [Parameter, Setting("Opacity", Group = "Appearance", Order = 2, Description = "0–1 (designed: 0.75)")]
    public double? Opacity { get; set; }

    [Parameter, Setting("Top border", Group = "Appearance", Order = 3, Description = "CSS border shorthand (designed: 1px solid rgba(128,128,128,.25)); 'none' to remove")]
    public string? BorderTop { get; set; }

    [Parameter, Setting("Text color", Group = "Appearance", Order = 4, Description = "designed: inherits the page text color")]
    public string? TextColor { get; set; }

    [Parameter, Setting("Link color", Group = "Appearance", Order = 5, Description = "designed: inherits the footer text color")]
    public string? LinkColor { get; set; }

    [Parameter, Setting("Background", Group = "Appearance", Order = 6, Description = "CSS background (designed: none)")]
    public string? Background { get; set; }

    // ── Behavior ────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Pin when short", Group = "Behavior", Order = 1, Description = "Fix the footer to the bottom edge while the page is shorter than the viewport")]
    public bool PinWhenShort { get; set; } = true;

    [Parameter, Setting("Pinned stacking order (z-index)", Group = "Behavior", Order = 2, Description = "z-index while pinned (designed: auto)")]
    public int? PinnedZIndex { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);

        var decls = new List<string>();
        void Add(string prop, string? value) { if (Css(value) is { } v) decls.Add(prop + ":" + v); }
        Add("--ma-footer-padding", Padding);
        Add("--ma-footer-margin-top", MarginTop);
        Add("--ma-footer-align", Align);
        Add("--ma-footer-font-size", FontSize);
        Add("--ma-footer-opacity", Opacity is { } o ? Math.Clamp(o, 0, 1).ToString(CultureInfo.InvariantCulture) : null);
        Add("--ma-footer-border", BorderTop);
        Add("--ma-footer-link", LinkColor);

        var css = decls.Count > 0 ? "html:root{" + string.Join(";", decls) + "}" : "";
        var own = new List<string>();
        if (Css(TextColor) is { } tc) own.Add("color:" + tc);
        if (Css(Background) is { } bg) own.Add("background:" + bg);
        if (own.Count > 0) css += ".ma-footer{" + string.Join(";", own) + "}";
        if (PinnedZIndex is { } z) css += ".ma-footer-pinned{z-index:" + z.ToString(CultureInfo.InvariantCulture) + "}";
        if (css.Length > 0) builder.AddMarkupContent(1000, "<style>" + css + "</style>");

        AddSettingsData(builder, 1001, "plugin.footer");
    }

    // Raw CSS text inside <style>: strip anything that could close the rule or the element.
    private static string? Css(string? v)
    {
        if (v is null) return null;
        var s = new string(v.Where(c => c is not ('{' or '}' or '<' or ';')).ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }
}
