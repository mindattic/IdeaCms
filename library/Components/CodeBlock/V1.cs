using System.Text;
using Microsoft.AspNetCore.Components.Rendering;
using MindAttic.Ideas.Abstractions;
using ParameterAttribute = Microsoft.AspNetCore.Components.ParameterAttribute;  // not the namespace: its ComponentBase would clash (M.IDEA-LAW-1)

namespace IdeaCms.Component.CodeBlock;

/// <summary>
/// IdeaCms.Component.CodeBlock.V1 — presentable code blocks as an asset-only activator. Drop
/// the token once and EVERY <c>&lt;pre&gt;&lt;code&gt;</c> on the page gets the code chrome, a
/// one-click copy button, and an optional language badge from <c>data-lang</c>:
/// <code>&lt;pre data-lang="csharp"&gt;&lt;code&gt;var x = 1;&lt;/code&gt;&lt;/pre&gt;</code>
/// Styling + copy only — syntax highlighting is a V2 candidate (kept out so V1 ships zero parsing
/// risk and zero external dependencies).
/// Instance settings (M.IDEA-§4.5) are page-wide: visual ones are emitted as a scoped custom-property
/// block (only when set), behavioral ones reach codeblock.js through
/// <c>data-ma-settings="component.codeblock"</c>.
/// </summary>
public sealed class V1 : ComponentBase
{
    private const string Mount = "/_ideas/Component/codeblock/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/codeblock.css" };
    public override IReadOnlyList<string> ScriptUrls { get; } = new[] { Mount + "/codeblock.js" };

    // ── Behavior ────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Copy button", Group = "Behavior", Order = 1)] public bool ShowCopy { get; set; } = true;
    [Parameter, Setting("Always show copy button", Group = "Behavior", Order = 2, Description = "Off = appears on hover/focus only")] public bool AlwaysShowCopy { get; set; }
    [Parameter, Setting("Language badge", Group = "Behavior", Order = 3, Description = "Show the data-lang badge")] public bool ShowLanguage { get; set; } = true;
    [Parameter, Setting("Uppercase language badge", Group = "Behavior", Order = 4)] public bool UppercaseLanguage { get; set; } = true;
    [Parameter, Setting("\"Copied\" duration (ms)", Group = "Behavior", Order = 5)] public int CopiedDuration { get; set; } = 1600;
    [Parameter, Setting("Wrap long lines", Group = "Behavior", Order = 6, Description = "Off = scroll horizontally")] public bool WrapLines { get; set; }
    [Parameter, Setting("Animate copy button", Group = "Behavior", Order = 7)] public bool Animations { get; set; } = true;

    // ── Content ─────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Copy label", Group = "Content", Order = 1, Copyable = false)] public string CopyText { get; set; } = "Copy";
    [Parameter, Setting("Copied label", Group = "Content", Order = 2, Copyable = false)] public string CopiedText { get; set; } = "Copied";

    // ── Colors ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Background", Group = "Colors", Order = 1, Description = "Default #14181f")] public string? Background { get; set; }
    [Parameter, Setting("Text color", Group = "Colors", Order = 2, Description = "Default #d6e2f0")] public string? TextColor { get; set; }
    [Parameter, Setting("Border", Group = "Colors", Order = 3, Description = "CSS border shorthand (default none)")] public string? Border { get; set; }
    [Parameter, Setting("Copied accent", Group = "Colors", Order = 4, Description = "Default #2e9e5b")] public string? CopiedColor { get; set; }

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Padding", Group = "Layout", Order = 1, Description = "Default 1rem 1.1rem")] public string? Padding { get; set; }
    [Parameter, Setting("Corner radius", Group = "Layout", Order = 2, Description = "Default .5rem")] public string? Radius { get; set; }
    [Parameter, Setting("Font size", Group = "Layout", Order = 3, Description = "Default .9rem")] public string? FontSize { get; set; }
    [Parameter, Setting("Line height", Group = "Layout", Order = 4, Description = "Default 1.55")] public string? LineHeight { get; set; }
    [Parameter, Setting("Font family", Group = "Layout", Order = 5, Description = "Default ui-monospace, Consolas, \"Cascadia Mono\", monospace")] public string? FontFamily { get; set; }
    [Parameter, Setting("Max height", Group = "Layout", Order = 6, Description = "Scroll vertically beyond this height (default none)")] public string? MaxHeight { get; set; }
    [Parameter, Setting("Tab size", Group = "Layout", Order = 7)] public string? TabSize { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);
        var css = BuildCss();
        if (css is not null) builder.AddMarkupContent(1000, css);
        AddSettingsData(builder, 1001, "component.codeblock");
    }

    private string? BuildCss()
    {
        const string S = ":root .ma-code";
        var sb = new StringBuilder();
        var vars = Vars(
            ("--ma-code-bg", Background), ("--ma-code-color", TextColor),
            ("--ma-code-copied", CopiedColor), ("--ma-code-padding", Padding), ("--ma-code-radius", Radius),
            ("--ma-code-font-size", FontSize), ("--ma-code-line-height", LineHeight),
            ("--ma-code-font", FontFamily));
        // Border / max-height / tab-size have no rule in codeblock.css (themes may style <pre>), so they
        // are emitted as real properties only when set rather than as var() fallbacks.
        vars += Vars(("border", Border), ("max-height", MaxHeight), ("tab-size", TabSize));
        if (vars.Length > 0) sb.Append(S).Append('{').Append(vars).Append('}');
        if (AlwaysShowCopy) sb.Append(S).Append(" .ma-code-copy{opacity:1}");
        if (!UppercaseLanguage) sb.Append(S).Append(" .ma-code-lang{text-transform:none}");
        if (WrapLines) sb.Append(S).Append("{white-space:pre-wrap;overflow-wrap:anywhere}");
        if (!Animations) sb.Append(S).Append(" .ma-code-copy{transition:none}");
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
