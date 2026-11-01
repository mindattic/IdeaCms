using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using IdeaCms.Abstractions;
using ComponentBase = IdeaCms.Abstractions.ComponentBase;

namespace IdeaCms.Component.WebSnapshot;

/// <summary>
/// IdeaCms.Component.WebSnapshot.V1 — the UiUx WEBSNAPSHOT bundle as an asset-only activator,
/// extracted VERBATIM from mindattic.com/index.htm. Any <c>&lt;div class="web-snapshot"&gt;</c>
/// container becomes a framed site-screenshot viewer: fetch mode (<c>data-src</c> pointing at a
/// .b64 capture) or inline mode (set the inner <c>&lt;img src&gt;</c> yourself, e.g. a base64 data
/// URI per the page convention). Exposes <c>window.WebSnapshot.autoInit()</c> for containers built
/// after load (the frontpage's tabified Portfolio tiles).
/// Instance settings (M.IDEA-§4.5) are page-wide — the activator serves every .web-snapshot on the page —
/// and are emitted only when set: CSS custom properties in a &lt;style&gt;, behavior on
/// window.WebSnapshotConfig.
/// </summary>
public sealed class V1 : ComponentBase
{
    private const string Mount = "/_ideas/Component/websnapshot/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/websnapshot.css" };
    public override IReadOnlyList<string> ScriptUrls { get; } = new[] { Mount + "/websnapshot.js" };

    // ── Behavior ────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Bypass cache", Group = "Behavior", Order = 1, Description = "Fetch-mode captures are re-downloaded every load (cache-busting query + no-store)")]
    public bool CacheBust { get; set; } = true;

    [Parameter, Setting("Auto-refresh interval (seconds)", Group = "Behavior", Order = 2, Description = "Re-fetch fetch-mode captures periodically; 0 = never")]
    public int RefreshInterval { get; set; }

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Image fit", Group = "Layout", Order = 1, Description = "object-fit: cover (default) | contain | fill")] public string? Fit { get; set; }
    [Parameter, Setting("Image position", Group = "Layout", Order = 2, Description = "object-position, e.g. top (default center)")] public string? Position { get; set; }
    [Parameter, Setting("Corner radius", Group = "Layout", Order = 3)] public string? Radius { get; set; }

    // ── Colors ──────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Background", Group = "Colors", Order = 1, Description = "Shown behind a contained or loading capture")] public string? Background { get; set; }
    [Parameter, Setting("Border", Group = "Colors", Order = 2, Description = "CSS border shorthand, e.g. 1px solid #30363d")] public string? Border { get; set; }
    [Parameter, Setting("Shadow", Group = "Colors", Order = 3, Description = "CSS box-shadow")] public string? Shadow { get; set; }

    // A value lands inside a <style> block, so anything that could close the rule or the element is refused.
    private static string? Css(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '<', '>', '{', '}', ';' }) >= 0
            ? null : $"{name}:{value.Trim()};";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);   // the asset-emitting render (link + script)

        // Emitted as real declarations (not var() hooks in the sheet) so an unset setting never
        // disturbs a host container's own radius/border/background — e.g. the TabBoard .tabPage-img.
        // ":root" lifts specificity above websnapshot.css regardless of load order.
        var img = string.Concat(new[] { Css("object-fit", Fit), Css("object-position", Position) }
            .Where(s => s is not null));
        var box = string.Concat(new[]
        {
            Css("border-radius", Radius), Css("background", Background), Css("border", Border), Css("box-shadow", Shadow),
        }.Where(s => s is not null));
        if (img.Length > 0 || box.Length > 0)
        {
            builder.OpenElement(10, "style");
            builder.AddMarkupContent(11,
                (box.Length > 0 ? ":root .web-snapshot{" + box + "}" : "")
                + (img.Length > 0 ? ":root .web-snapshot > img{" + img + "}" : ""));
            builder.CloseElement();
        }

        var js = new List<string>();
        if (!CacheBust) js.Add("cacheBust: false");
        if (RefreshInterval > 0) js.Add("refreshInterval: " + RefreshInterval);
        if (js.Count > 0)
        {
            builder.OpenElement(20, "script");
            builder.AddMarkupContent(21,
                "window.WebSnapshotConfig = Object.assign(window.WebSnapshotConfig || {}, { " + string.Join(", ", js) + " });");
            builder.CloseElement();
        }
    }
}
