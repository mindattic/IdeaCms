using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace IdeaCms.Abstractions;

// ============================================================================================
//  CmsInclude — the SDK include primitive. The MindAttic analog of Orchard's @Display / <zone>:
//  a COMPILED Page/Theme renders ANOTHER citizen (Plugin/Component/Theme) BY STRING ID at runtime,
//  with ZERO compile-time reference to that citizen's package. The string is resolved through the
//  host's global catalog exactly like a data page's <IdeaCms.…/> include tag, so a missing or
//  disabled target degrades to a visible placeholder + Admin-Inbox alert — never a crash.
//
//  Authoring (in a .razor that compiles against ONLY this SDK):
//      <CmsInclude Ref="IdeaCms.Plugin.Tooltip.V1" />
//      <CmsInclude Ref="IdeaCms.Component.Textbox.V1" placeholder="Name" />
//      <CmsInclude Ref="IdeaCms.Component.Accordion" />   @* no version = float to latest *@
//
//  Themes are NOT placed with CmsInclude — a page selects its theme by ThemeKey metadata and the host
//  wraps the body in it. Declare what a compiled page uses with [Uses(...)] so its assets hoist.
// ============================================================================================

/// <summary>
/// Renders another citizen by its string id at runtime (no compile-time package reference). Delegates to
/// the host's <see cref="IIncludeRenderer"/> feature, resolved from the cascaded <see cref="IRenderContext"/>;
/// if no host feature is present (e.g. design time) it renders nothing.
/// </summary>
public sealed class CmsInclude : Microsoft.AspNetCore.Components.ComponentBase
{
    [CascadingParameter] private IRenderContext? Context { get; set; }

    /// <summary>
    /// The include reference in the locked tag form, e.g. <c>"IdeaCms.Plugin.Tooltip.V1"</c>.
    /// Omit the trailing <c>.V{n}</c> (or use <c>.Latest</c>) to float to the latest enabled version.
    /// </summary>
    [Parameter, EditorRequired] public string Ref { get; set; } = "";

    /// <summary>Inner content, passed to the resolved citizen as <c>ChildContent</c> when it declares one.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Unmatched attributes flow straight through to the resolved citizen (e.g. a Control's input).</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? Attributes { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(Ref)) return;
        if (Context is null || !Context.TryGetFeature<IIncludeRenderer>(out var renderer) || renderer is null)
            return; // no host (design time / preview without a renderer) -> render nothing, never throw

        IReadOnlyDictionary<string, object?>? attrs = null;
        if (Attributes is { Count: > 0 })
        {
            var copy = new Dictionary<string, object?>(Attributes.Count);
            foreach (var kv in Attributes) copy[kv.Key] = kv.Value;
            attrs = copy;
        }

        builder.AddContent(0, renderer.Render(Context, Ref, attrs, ChildContent));
    }
}
