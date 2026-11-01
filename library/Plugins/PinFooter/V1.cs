using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Plugin.PinFooter;

/// <summary>
/// IdeaCms.Plugin.PinFooter.V1 — the UiUx PINFOOTER bundle as an asset-only activator,
/// extracted VERBATIM from mindattic.com/index.htm. Any element with class
/// <c>pin-when-short</c> (canonically the site &lt;footer&gt;) pins to the bottom edge while the
/// document is shorter than the viewport and flows normally once content scrolls — re-evaluated on
/// resize, font load, and (CMS adapter) host DOM swaps. Distinct from the generic
/// <c>Plugin.Footer</c> baseline activator: this is the authentic mindattic.com implementation and
/// class contract. Instance settings (M.IDEA-§4.5): the on/off toggle reaches the script through
/// <c>data-ma-settings="plugin.pinfooter"</c>; the pinned look is emitted as a small style block.
/// </summary>
public sealed class V1 : PluginBase
{
    private const string Mount = "/_ideas/Plugin/pinfooter/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/pinfooter.css" };
    public override IReadOnlyList<string> ScriptUrls { get; } = new[] { Mount + "/pinfooter.js" };

    [Parameter, Setting("Pin when short", Group = "Behavior", Order = 1, Description = "Pin .pin-when-short elements to the bottom edge while the page is shorter than the viewport")]
    public bool Enabled { get; set; } = true;

    [Parameter, Setting("Bottom offset", Group = "Layout", Order = 1, Description = "Gap between the pinned element and the viewport bottom (designed: 0)")]
    public string? BottomOffset { get; set; }

    [Parameter, Setting("Pinned stacking order (z-index)", Group = "Layout", Order = 2, Description = "z-index while pinned (designed: auto)")]
    public int? ZIndex { get; set; }

    [Parameter, Setting("Pinned background", Group = "Appearance", Order = 1, Description = "CSS background while pinned (designed: unchanged)")]
    public string? PinnedBackground { get; set; }

    [Parameter, Setting("Pinned shadow", Group = "Appearance", Order = 2, Description = "CSS box-shadow while pinned (designed: none)")]
    public string? PinnedShadow { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);

        var css = Css(BottomOffset) is { } b ? "html:root{--ma-pinfooter-bottom:" + b + "}" : "";
        var own = new List<string>();
        if (ZIndex is { } z) own.Add("z-index:" + z.ToString(CultureInfo.InvariantCulture));
        if (Css(PinnedBackground) is { } bg) own.Add("background:" + bg);
        if (Css(PinnedShadow) is { } sh) own.Add("box-shadow:" + sh);
        if (own.Count > 0) css += ".pin-when-short.pinned{" + string.Join(";", own) + "}";
        if (css.Length > 0) builder.AddMarkupContent(1000, "<style>" + css + "</style>");

        AddSettingsData(builder, 1001, "plugin.pinfooter");
    }

    // Raw CSS text inside <style>: strip anything that could close the rule or the element.
    private static string? Css(string? v)
    {
        if (v is null) return null;
        var s = new string(v.Where(c => c is not ('{' or '}' or '<' or ';')).ToArray()).Trim();
        return s.Length == 0 ? null : s;
    }
}
