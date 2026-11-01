using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using IdeaCms.Abstractions;

namespace IdeaCms.Plugin.AtticFont;

/// <summary>
/// IdeaCms.Plugin.AtticFont.V1 — the Attic display typeface as a self-contained Plugin.
/// Emits one stylesheet (base64-embedded @font-face, no external font files) bundled in this package's
/// wwwroot/ and served under <c>/_ideas/Plugin/atticfont/1/</c>. Registers the family and exposes the
/// <c>--font-attic</c> token; a Theme/page applies it (e.g. <c>font-family: var(--font-attic)</c>).
/// Instance settings (M.IDEA-§4.5) can also apply the face directly (headings / any selector) and change the
/// token's fallback stack; with none set it only registers the face, exactly as before.
/// </summary>
public sealed class V1 : PluginBase
{
    private const string Mount = "/_ideas/Plugin/atticfont/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/attic-font.css" };

    [Parameter, Setting("Apply to headings", Group = "Behavior", Order = 1, Description = "Set h1–h6 in Attic on this page")]
    public bool ApplyToHeadings { get; set; }

    [Parameter, Setting("Apply to selector", Group = "Behavior", Order = 2, Description = "Any CSS selector to set in Attic, e.g. \".brand, .hero-title\"")]
    public string? ApplyToSelector { get; set; }

    [Parameter, Setting("Fallback font stack", Group = "Advanced", Order = 1, Description = "Fonts after 'Attic' in --font-attic (designed: serif)")]
    public string? FallbackStack { get; set; }

    [Parameter, Setting("Letter spacing", Group = "Advanced", Order = 2, Description = "letter-spacing for the elements this plugin applies Attic to")]
    public string? LetterSpacing { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);
        var rules = new List<string>();
        if (Css(FallbackStack) is { } fb) rules.Add("html:root{--font-attic:'Attic', " + fb + "}");
        var targets = new List<string>();
        if (ApplyToHeadings) targets.Add("h1,h2,h3,h4,h5,h6");
        if (Css(ApplyToSelector) is { } sel) targets.Add(sel);
        if (targets.Count > 0)
        {
            var ls = Css(LetterSpacing) is { } l ? ";letter-spacing:" + l : "";
            rules.Add(string.Join(",", targets) + "{font-family:var(--font-attic)" + ls + "}");
        }
        if (rules.Count > 0) builder.AddMarkupContent(1000, "<style>" + string.Concat(rules) + "</style>");
    }

    // Raw CSS text inside <style>: strip anything that could close the rule or the element.
    private static string? Css(string? v)
    {
        if (v is null) return null;
        var s = new string(v.Where(c => c is not ('{' or '}' or '<' or ';')).ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }
}
