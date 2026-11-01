using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using IdeaCms.Abstractions;

namespace IdeaCms.Component.Gallery;

/// <summary>
/// IdeaCms.Component.Gallery.V1 — the image-grid capability as an asset-only activator. Drop the
/// token once and ANY <c>&lt;div class="ma-gallery"&gt;</c> becomes a responsive grid of its tiles.
/// Tiles follow the page-authoring convention that images live as inline base64 CSS classes:
/// <code>
///   &lt;div class="ma-gallery"&gt;
///     &lt;div class="ma-gallery-tile img-photo1"&gt;&lt;/div&gt;            (class tile → lightbox)
///     &lt;a class="ma-gallery-tile img-book1" href="https://…"&gt;&lt;/a&gt;  (linked tile → navigates)
///     &lt;img src="data:…" alt="…"&gt;                                  (&lt;img&gt; also works)
///   &lt;/div&gt;
/// </code>
/// Linked tiles make it the mindattic.com books grid (covers → store pages); unlinked tiles get a
/// keyboard-navigable lightbox (Esc / arrows). Tile size tunes via <c>--ma-gallery-min</c>, tile
/// shape via <c>--ma-gallery-ratio</c>.
/// Instance settings (M.IDEA-§4.5) apply page-wide (the activator has no element of its own): visual ones
/// are emitted as a small &lt;style&gt; overriding the gallery custom properties only for values that
/// are set; behavior ones reach gallery.js through a <c>data-ma-settings="component.gallery"</c> element.
/// </summary>
public sealed class V1 : IdeaCms.Abstractions.ComponentBase
{
    private const string Mount = "/_ideas/Component/gallery/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/gallery.css" };
    public override IReadOnlyList<string> ScriptUrls { get; } = new[] { Mount + "/gallery.js" };

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Minimum tile width", Group = "Layout", Order = 1, Description = "Default 9rem")]
    public string? TileMinWidth { get; set; }

    [Parameter, Setting("Gap", Group = "Layout", Order = 2, Description = "Default 1rem")]
    public string? Gap { get; set; }

    [Parameter, Setting("Tile aspect ratio", Group = "Layout", Order = 3, Description = "Default 2 / 3 (book cover)")]
    public string? Ratio { get; set; }

    [Parameter, Setting("Vertical alignment", Group = "Layout", Order = 4, Description = "start, center, end or stretch (default start)")]
    public string? AlignItems { get; set; }

    // ── Appearance ──────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Tile corner radius", Group = "Appearance", Order = 1, Description = "Default .4rem")]
    public string? Radius { get; set; }

    [Parameter, Setting("Tile hover shadow", Group = "Appearance", Order = 2, Description = "Default 0 .5rem 1.25rem rgba(0,0,0,.25)")]
    public string? HoverShadow { get; set; }

    [Parameter, Setting("Tile hover transform", Group = "Appearance", Order = 3, Description = "Default translateY(-3px) scale(1.02)")]
    public string? HoverTransform { get; set; }

    [Parameter, Setting("Lightbox backdrop", Group = "Appearance", Order = 4, Description = "Default rgba(0,0,0,.85)")]
    public string? LightboxBackdrop { get; set; }

    // ── Behavior ────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Hover effect", Group = "Behavior", Order = 1, Description = "Lift and shadow tiles on hover")]
    public bool HoverEffect { get; set; } = true;

    [Parameter, Setting("Lightbox", Group = "Behavior", Order = 2, Description = "Open unlinked tiles in a full-screen viewer")]
    public bool Lightbox { get; set; } = true;

    [Parameter, Setting("Prev/next arrows", Group = "Behavior", Order = 3)]
    public bool ShowArrows { get; set; } = true;

    [Parameter, Setting("Keyboard navigation", Group = "Behavior", Order = 4, Description = "Esc closes, arrow keys step")]
    public bool Keyboard { get; set; } = true;

    [Parameter, Setting("Wrap around", Group = "Behavior", Order = 5, Description = "Stepping past the last tile returns to the first")]
    public bool Loop { get; set; } = true;

    [Parameter, Setting("Close on backdrop click", Group = "Behavior", Order = 6)]
    public bool CloseOnBackdrop { get; set; } = true;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);

        var css = BuildCss();
        if (css.Length > 0)
        {
            builder.OpenElement(900, "style");
            builder.AddContent(901, css);
            builder.CloseElement();
        }
        AddSettingsData(builder, 1000, "component.gallery");
    }

    // Strip characters that could break out of a declaration/rule; values are admin-authored CSS.
    private static string? Clean(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var s = new string(v.Where(c => c is not ('{' or '}' or ';' or '<' or '>')).ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }

    private string BuildCss()
    {
        var sb = new System.Text.StringBuilder();
        var root = new List<string>();
        void Root(string prop, string? v) { if (Clean(v) is { } c) root.Add($"{prop}:{c}"); }
        Root("--ma-gallery-min", TileMinWidth);
        Root("--ma-gallery-gap", Gap);
        Root("--ma-gallery-ratio", Ratio);
        Root("--ma-gallery-radius", Radius);
        Root("--ma-gallery-hover-shadow", HoverShadow);
        Root("--ma-gallery-hover-transform", HoverTransform);
        Root("align-items", AlignItems);
        if (root.Count > 0) sb.Append(".ma-gallery{").Append(string.Join(";", root)).Append('}');
        if (Clean(LightboxBackdrop) is { } bd) sb.Append(".ma-gallery-lightbox{background:").Append(bd).Append('}');
        if (!HoverEffect)
            sb.Append(".ma-gallery-tile,.ma-gallery img{transition:none}")
              .Append(".ma-gallery-tile:hover,.ma-gallery-tile:focus-visible,.ma-gallery img:hover{transform:none;box-shadow:none}");
        if (!Lightbox) sb.Append(".ma-gallery div.ma-gallery-tile,.ma-gallery img:not(a img){cursor:default}");
        return sb.ToString();
    }
}
