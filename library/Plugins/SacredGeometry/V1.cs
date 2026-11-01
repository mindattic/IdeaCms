using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using IdeaCms.Abstractions;

namespace IdeaCms.Plugin.SacredGeometry;

/// <summary>
/// IdeaCms.Plugin.SacredGeometry.V1 — the 1024-shape SacredGeometry catalog + renderer as a
/// self-contained Plugin (the .idea target of the canonical UiUx <c>Components/SacredGeometry</c>
/// source). A code-only capability activator: it emits the renderer (<c>window.SacredGeometry</c>) plus
/// a tiny auto-init driver, both bundled in this package's wwwroot/ and served under
/// <c>/_ideas/Plugin/sacredgeometry/1/</c>. Dropping it on a page makes every
/// <c>&lt;canvas data-sacred-shape="N"&gt;</c> animate shape N live (IntersectionObserver-gated,
/// MutationObserver-aware) — no per-page JS. The driver loads AFTER the renderer.
/// Instance settings (M.IDEA-§4.5) reach the driver through <c>data-ma-settings="plugin.sacredgeometry"</c>
/// and are page-wide defaults; a canvas's own data-sacred-spin / data-sacred-bg still win.
/// </summary>
public sealed class V1 : PluginBase
{
    private const string Mount = "/_ideas/Plugin/sacredgeometry/1";

    public override IReadOnlyList<string> ScriptUrls { get; } = new[]
    {
        Mount + "/sacred-geometry.js",        // window.SacredGeometry (catalog + draw)
        Mount + "/sacred-geometry-init.js",   // the canvas[data-sacred-shape] animator
    };

    [Parameter, Setting("Animate", Group = "Behavior", Order = 1, Description = "Off draws every shape once, still")]
    public bool Animate { get; set; } = true;

    [Parameter, Setting("Speed ×", Group = "Behavior", Order = 2, Description = "Multiplier on every canvas's rotation speed (1 = as designed)")]
    public double Speed { get; set; } = 1;

    [Parameter, Setting("Default spin", Group = "Behavior", Order = 3, Description = "Phase step per frame for canvases without data-sacred-spin")]
    public double DefaultSpin { get; set; } = 0.01;

    [Parameter, Setting("Respect reduced motion", Group = "Behavior", Order = 4, Description = "Hold shapes still for visitors who prefer reduced motion")]
    public bool RespectReducedMotion { get; set; }

    [Parameter, Setting("Default backdrop", Group = "Appearance", Order = 1, Description = "Canvas fill for canvases without data-sacred-bg (designed: transparent)")]
    public string? DefaultBackground { get; set; }

    [Parameter, Setting("Canvas opacity", Group = "Appearance", Order = 2, Description = "0–1 applied to every sacred-geometry canvas (designed: 1)")]
    public double? Opacity { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);
        if (Opacity is { } o)
            builder.AddMarkupContent(1000, "<style>canvas[data-sacred-shape]{opacity:"
                + Math.Clamp(o, 0, 1).ToString(CultureInfo.InvariantCulture) + "}</style>");
        AddSettingsData(builder, 1001, "plugin.sacredgeometry");
    }
}
