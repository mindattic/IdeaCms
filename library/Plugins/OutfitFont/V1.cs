using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Plugin.OutfitFont;

/// <summary>
/// IdeaCms.Plugin.OutfitFont.V1 — the Outfit typeface as a self-contained Plugin (the .idea
/// target of the UiUx OutfitFont source). A code-only capability activator: it emits ONE stylesheet
/// (base64-embedded @font-face, so no external font files) bundled in this package's wwwroot/ and
/// served under <c>/_ideas/Plugin/outfitfont/1/</c>. Dropping it on a page makes the Outfit family
/// available page-wide. Instance settings (M.IDEA-§4.5) can also apply the face (body / headings / any
/// selector) and change the <c>--font-outfit</c> fallback stack; with none set it only registers the face.
/// </summary>
public sealed class V1 : PluginBase
{
    private const string Mount = "/_ideas/Plugin/outfitfont/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/outfit-font.css" };

    [Parameter, Setting("Apply to body text", Group = "Behavior", Order = 1, Description = "Set the page body in Outfit")]
    public bool ApplyToBody { get; set; }

    [Parameter, Setting("Apply to headings", Group = "Behavior", Order = 2, Description = "Set h1–h6 in Outfit")]
    public bool ApplyToHeadings { get; set; }

    [Parameter, Setting("Apply to selector", Group = "Behavior", Order = 3, Description = "Any CSS selector to set in Outfit, e.g. \".nav, .card\"")]
    public string? ApplyToSelector { get; set; }

    [Parameter, Setting("Font weight", Group = "Advanced", Order = 1, Description = "font-weight (100–900, variable) for the elements this plugin applies Outfit to")]
    public int? FontWeight { get; set; }

    [Parameter, Setting("Fallback font stack", Group = "Advanced", Order = 2, Description = "Fonts after 'Outfit' in --font-outfit (designed: system-ui, sans-serif)")]
    public string? FallbackStack { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);
        var rules = new List<string>();
        if (Css(FallbackStack) is { } fb) rules.Add("html:root{--font-outfit:'Outfit', " + fb + "}");
        var targets = new List<string>();
        if (ApplyToBody) targets.Add("body");
        if (ApplyToHeadings) targets.Add("h1,h2,h3,h4,h5,h6");
        if (Css(ApplyToSelector) is { } sel) targets.Add(sel);
        if (targets.Count > 0)
        {
            var w = FontWeight is { } fw ? ";font-weight:" + Math.Clamp(fw, 100, 900) : "";
            rules.Add(string.Join(",", targets) + "{font-family:var(--font-outfit)" + w + "}");
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
