using System.Text;
using Microsoft.AspNetCore.Components.Rendering;
using IdeaCms.Abstractions;
using ParameterAttribute = Microsoft.AspNetCore.Components.ParameterAttribute;  // not the namespace: its ComponentBase would clash (M.IDEA-LAW-1)

namespace IdeaCms.Component.Callout;

/// <summary>
/// IdeaCms.Component.Callout.V1 — notice/admonition boxes as a CSS-only activator. Drop the
/// token once and ANY <c>&lt;div class="ma-callout"&gt;</c> gets the callout chrome; add a variant
/// class for the accent + icon: <c>ma-callout-info</c> (default), <c>ma-callout-success</c>,
/// <c>ma-callout-warn</c>, <c>ma-callout-error</c>. Pure CSS — icons are drawn glyphs, no images,
/// no script:
/// <code>&lt;div class="ma-callout ma-callout-warn"&gt;&lt;strong&gt;Heads up.&lt;/strong&gt; Versions never mutate.&lt;/div&gt;</code>
/// Instance settings (M.IDEA-§4.5) are page-wide defaults for every callout on the page, emitted as a
/// scoped custom-property block only when something is set.
/// </summary>
public sealed class V1 : ComponentBase
{
    private const string Mount = "/_ideas/Component/callout/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/callout.css" };

    // ── Colors ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Info accent", Group = "Colors", Order = 1, Description = "Default #3b82d4")] public string? InfoAccent { get; set; }
    [Parameter, Setting("Success accent", Group = "Colors", Order = 2, Description = "Default #2e9e5b")] public string? SuccessAccent { get; set; }
    [Parameter, Setting("Warning accent", Group = "Colors", Order = 3, Description = "Default #d4a52e")] public string? WarnAccent { get; set; }
    [Parameter, Setting("Error accent", Group = "Colors", Order = 4, Description = "Default #d44a3b")] public string? ErrorAccent { get; set; }

    [Parameter, Setting("Background tint", Group = "Colors", Order = 5, Description = "How much accent mixes into the background, e.g. 9% (default)")]
    public string? Tint { get; set; }

    [Parameter, Setting("Text color", Group = "Colors", Order = 6)] public string? TextColor { get; set; }

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Padding", Group = "Layout", Order = 1, Description = "Default .9rem 1rem .9rem 2.9rem (room for the icon)")] public string? Padding { get; set; }
    [Parameter, Setting("Margin", Group = "Layout", Order = 2, Description = "Default 1rem 0")] public string? Margin { get; set; }
    [Parameter, Setting("Corner radius", Group = "Layout", Order = 3, Description = "Default .35rem")] public string? Radius { get; set; }
    [Parameter, Setting("Accent bar width", Group = "Layout", Order = 4, Description = "Default 4px")] public string? BorderWidth { get; set; }
    [Parameter, Setting("Line height", Group = "Layout", Order = 5, Description = "Default 1.5")] public string? LineHeight { get; set; }
    [Parameter, Setting("Font size", Group = "Layout", Order = 6)] public string? FontSize { get; set; }

    // ── Behavior ────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Show icon", Group = "Behavior", Order = 1)] public bool ShowIcon { get; set; } = true;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);
        var css = BuildCss();
        if (css is not null) builder.AddMarkupContent(1000, css);
    }

    private string? BuildCss()
    {
        const string S = ":root .ma-callout";
        var sb = new StringBuilder();
        var vars = Vars(
            ("--ma-callout-tint", Tint),
            ("--ma-callout-padding", Padding ?? (ShowIcon ? null : ".9rem 1rem")),
            ("--ma-callout-margin", Margin), ("--ma-callout-radius", Radius),
            ("--ma-callout-border-width", BorderWidth), ("--ma-callout-line-height", LineHeight));
        // No as-designed rule for these (theme styles apply), so they are real properties, emitted only when set.
        vars += Vars(("color", TextColor), ("font-size", FontSize));
        if (vars.Length > 0) sb.Append(S).Append('{').Append(vars).Append('}');
        // Accents are declared per variant class, so each override targets its own variant.
        Rule(sb, S + ":not(.ma-callout-success):not(.ma-callout-warn):not(.ma-callout-error)", InfoAccent);
        Rule(sb, ":root .ma-callout-success", SuccessAccent);
        Rule(sb, ":root .ma-callout-warn", WarnAccent);
        Rule(sb, ":root .ma-callout-error", ErrorAccent);
        if (!ShowIcon) sb.Append(S).Append("::before{display:none}");
        return sb.Length == 0 ? null : "<style>" + sb + "</style>";
    }

    private static void Rule(StringBuilder sb, string selector, string? accent)
    {
        var v = Vars(("--ma-callout-accent", accent));
        if (v.Length > 0) sb.Append(selector).Append('{').Append(v).Append('}');
    }

    // Values land in a raw <style> block: strip anything that could close the block or a rule.
    private static string Vars(params (string Name, string? Value)[] pairs)
    {
        var sb = new StringBuilder();
        foreach (var (name, value) in pairs)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var clean = new string(value.Where(c => c is not ('<' or '>' or '{' or '}' or ';' or '\\')).ToArray()).Trim();
            if (clean.Length > 0) sb.Append(name).Append(':').Append(clean).Append(';');
        }
        return sb.ToString();
    }
}
