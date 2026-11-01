using System.Text;
using Microsoft.AspNetCore.Components.Rendering;
using MindAttic.Ideas.Abstractions;
using ParameterAttribute = Microsoft.AspNetCore.Components.ParameterAttribute;  // not the namespace: its ComponentBase would clash (M.IDEA-LAW-1)

namespace IdeaCms.Component.Carousel;

/// <summary>
/// IdeaCms.Component.Carousel.V1 — the slideshow capability as an asset-only activator. Drop the
/// token once and ANY <c>&lt;div class="ma-carousel"&gt;</c> of <c>.ma-slide</c> children becomes a
/// slider with arrows, dots, and keyboard support. A slide is free-form markup; an image slide is a
/// slide carrying a base64 background-image CSS class (the page convention):
/// <code>
///   &lt;div class="ma-carousel" data-autoplay="6000"&gt;
///     &lt;div class="ma-slide img-sunset"&gt;&lt;/div&gt;
///     &lt;div class="ma-slide"&gt;&lt;h3&gt;Any markup&lt;/h3&gt;&lt;p&gt;works as a slide.&lt;/p&gt;&lt;/div&gt;
///   &lt;/div&gt;
/// </code>
/// <c>data-autoplay</c> (ms) is optional; autoplay pauses on hover/focus.
/// Instance settings (M.IDEA-§4.5) are page-wide defaults for every carousel on the page: visual ones are
/// emitted as a scoped custom-property block (only when set), behavioral ones reach carousel.js through
/// <c>data-ma-settings="component.carousel"</c>. Per-carousel <c>data-*</c> attributes still win.
/// </summary>
public sealed class V1 : ComponentBase
{
    private const string Mount = "/_ideas/Component/carousel/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/carousel.css" };
    public override IReadOnlyList<string> ScriptUrls { get; } = new[] { Mount + "/carousel.js" };

    // ── Behavior ────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Autoplay interval (ms)", Group = "Behavior", Order = 1, Description = "0 = no autoplay. A carousel's own data-autoplay wins")]
    public int Autoplay { get; set; }

    [Parameter, Setting("Pause autoplay on hover/focus", Group = "Behavior", Order = 2)] public bool PauseOnHover { get; set; } = true;
    [Parameter, Setting("Loop (wrap around)", Group = "Behavior", Order = 3)] public bool Loop { get; set; } = true;
    [Parameter, Setting("Show arrows", Group = "Behavior", Order = 4)] public bool ShowArrows { get; set; } = true;
    [Parameter, Setting("Show dots", Group = "Behavior", Order = 5)] public bool ShowDots { get; set; } = true;
    [Parameter, Setting("Keyboard navigation", Group = "Behavior", Order = 6, Description = "Left/Right arrow keys")] public bool Keyboard { get; set; } = true;
    [Parameter, Setting("Start slide", Group = "Behavior", Order = 7, Description = "1-based index of the first slide shown")] public int StartSlide { get; set; } = 1;
    [Parameter, Setting("Animate arrow hover", Group = "Behavior", Order = 8)] public bool Animations { get; set; } = true;

    // ── Content ─────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Previous arrow glyph", Group = "Content", Order = 1, Copyable = false)] public string PrevText { get; set; } = "‹";
    [Parameter, Setting("Next arrow glyph", Group = "Content", Order = 2, Copyable = false)] public string NextText { get; set; } = "›";

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Slide height", Group = "Layout", Order = 1, Description = "Minimum slide height (default 20rem)")] public string? Height { get; set; }
    [Parameter, Setting("Slide padding", Group = "Layout", Order = 2, Description = "Default 1.5rem")] public string? SlidePadding { get; set; }
    [Parameter, Setting("Corner radius", Group = "Layout", Order = 3, Description = "Default .5rem")] public string? Radius { get; set; }
    [Parameter, Setting("Background size", Group = "Layout", Order = 4, Description = "Image slides: cover (default), contain, …")] public string? BackgroundSize { get; set; }
    [Parameter, Setting("Arrow size", Group = "Layout", Order = 5, Description = "Default 2.5rem")] public string? ArrowSize { get; set; }
    [Parameter, Setting("Dot size", Group = "Layout", Order = 6, Description = "Default .6rem")] public string? DotSize { get; set; }

    // ── Colors ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Background", Group = "Colors", Order = 1)] public string? Background { get; set; }
    [Parameter, Setting("Arrow background", Group = "Colors", Order = 2, Description = "Default rgba(0,0,0,.35)")] public string? ArrowBackground { get; set; }
    [Parameter, Setting("Arrow color", Group = "Colors", Order = 3, Description = "Default #fff")] public string? ArrowColor { get; set; }
    [Parameter, Setting("Dot color", Group = "Colors", Order = 4, Description = "Default rgba(255,255,255,.55)")] public string? DotColor { get; set; }
    [Parameter, Setting("Active dot color", Group = "Colors", Order = 5, Description = "Default #fff")] public string? DotActiveColor { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);
        var css = BuildCss();
        if (css is not null) builder.AddMarkupContent(1000, css);
        AddSettingsData(builder, 1001, "component.carousel");
    }

    private string? BuildCss()
    {
        const string S = ":root .ma-carousel";
        var sb = new StringBuilder();
        var vars = Vars(
            ("--ma-carousel-height", Height), ("--ma-carousel-slide-padding", SlidePadding),
            ("--ma-carousel-radius", Radius), ("--ma-carousel-bg-size", BackgroundSize),
            ("--ma-carousel-arrow-size", ArrowSize), ("--ma-carousel-dot-size", DotSize),
            ("--ma-carousel-arrow-bg", ArrowBackground),
            ("--ma-carousel-arrow-color", ArrowColor), ("--ma-carousel-dot", DotColor),
            ("--ma-carousel-dot-active", DotActiveColor));
        vars += Vars(("background", Background));  // no as-designed rule: a real property, only when set
        if (vars.Length > 0) sb.Append(S).Append('{').Append(vars).Append('}');
        if (!Animations) sb.Append(S).Append(" .ma-carousel-arrow{transition:none}");
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
