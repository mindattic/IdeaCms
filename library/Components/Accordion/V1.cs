using System.Text;
using Microsoft.AspNetCore.Components.Rendering;
using ParameterAttribute = Microsoft.AspNetCore.Components.ParameterAttribute;  // not the namespace: its ComponentBase would clash (M.IDEA-LAW-1)
using IdeaCms.Abstractions;

namespace IdeaCms.Component.Accordion;

/// <summary>
/// IdeaCms.Component.Accordion.V1 — the collapsible-sections capability as an asset-only
/// activator. Drop the token once and ANY <c>&lt;div class="ma-accordion"&gt;</c> of native
/// <c>&lt;details&gt;/&lt;summary&gt;</c> children gets the accordion chrome (no JS required for
/// open/close — the platform does it). Add <c>data-exclusive</c> on the container and the script
/// keeps at most one item open (the classic FAQ behavior):
/// <code>
///   &lt;div class="ma-accordion" data-exclusive&gt;
///     &lt;details&gt;&lt;summary&gt;Question&lt;/summary&gt;&lt;p&gt;Answer.&lt;/p&gt;&lt;/details&gt;
///   &lt;/div&gt;
/// </code>
/// Instance settings (M.IDEA-§4.5) are page-wide defaults for every accordion on the page: visual ones
/// are emitted as a scoped custom-property block (only when set), behavioral ones reach the script
/// through <c>data-ma-settings="component.accordion"</c>. Per-element attributes/styles still win.
/// </summary>
public sealed class V1 : ComponentBase
{
    private const string Mount = "/_ideas/Component/accordion/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/accordion.css" };
    public override IReadOnlyList<string> ScriptUrls { get; } = new[] { Mount + "/accordion.js" };

    // ── Colors ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Border color", Group = "Colors", Order = 1, Description = "Outer border and item dividers (default rgba(128,128,128,.35))")]
    public string? BorderColor { get; set; }

    [Parameter, Setting("Background", Group = "Colors", Order = 2, Description = "Accordion background (default transparent)")]
    public string? Background { get; set; }

    [Parameter, Setting("Header hover background", Group = "Colors", Order = 3, Description = "Default rgba(128,128,128,.08)")]
    public string? HoverBackground { get; set; }

    [Parameter, Setting("Header text color", Group = "Colors", Order = 4)]
    public string? HeaderColor { get; set; }

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Corner radius", Group = "Layout", Order = 1, Description = "Default .5rem")]
    public string? Radius { get; set; }

    [Parameter, Setting("Border width", Group = "Layout", Order = 2, Description = "Default 1px")]
    public string? BorderWidth { get; set; }

    [Parameter, Setting("Header padding", Group = "Layout", Order = 3, Description = "Default .8rem 1rem")]
    public string? HeaderPadding { get; set; }

    [Parameter, Setting("Content padding", Group = "Layout", Order = 4, Description = "Default 0 1rem 1rem")]
    public string? ContentPadding { get; set; }

    [Parameter, Setting("Header font weight", Group = "Layout", Order = 5, Description = "Default 600")]
    public string? HeaderFontWeight { get; set; }

    [Parameter, Setting("Header font size", Group = "Layout", Order = 6)]
    public string? HeaderFontSize { get; set; }

    [Parameter, Setting("Max width", Group = "Layout", Order = 7)]
    public string? MaxWidth { get; set; }

    // ── Behavior ────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Show chevron", Group = "Behavior", Order = 1)]
    public bool ShowChevron { get; set; } = true;

    [Parameter, Setting("Animate chevron", Group = "Behavior", Order = 2)]
    public bool Animations { get; set; } = true;

    [Parameter, Setting("Hover highlight", Group = "Behavior", Order = 3)]
    public bool HoverEffect { get; set; } = true;

    [Parameter, Setting("Exclusive by default", Group = "Behavior", Order = 4, Description = "Every accordion keeps at most one item open, not only those with data-exclusive")]
    public bool ExclusiveByDefault { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);
        var css = BuildCss();
        if (css is not null) builder.AddMarkupContent(1000, css);
        if (ExclusiveByDefault) AddSettingsData(builder, 1001, "component.accordion");
    }

    private string? BuildCss()
    {
        const string S = ":root .ma-accordion";
        var sb = new StringBuilder();
        var vars = Vars(
            ("--ma-accordion-border", BorderColor), ("--ma-accordion-hover", HoverBackground),
            ("--ma-accordion-radius", Radius), ("--ma-accordion-border-width", BorderWidth),
            ("--ma-accordion-header-padding", HeaderPadding), ("--ma-accordion-content-padding", ContentPadding),
            ("--ma-accordion-header-weight", HeaderFontWeight));
        // No as-designed rule for these (theme styles apply), so they are real properties, emitted only when set.
        vars += Vars(("background", Background), ("max-width", MaxWidth));
        if (vars.Length > 0) sb.Append(S).Append('{').Append(vars).Append('}');
        var summary = Vars(("color", HeaderColor), ("font-size", HeaderFontSize));
        if (summary.Length > 0) sb.Append(S).Append(" summary{").Append(summary).Append('}');
        if (!ShowChevron) sb.Append(S).Append(" summary::after{display:none}");
        if (!Animations) sb.Append(S).Append(" summary::after{transition:none}");
        if (!HoverEffect) sb.Append(S).Append(" summary:hover{background:none}");
        return sb.Length == 0 ? null : "<style>" + sb + "</style>";
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
