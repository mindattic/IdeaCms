using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MindAttic.Ideas.Abstractions;
using BlazorComponentBase = Microsoft.AspNetCore.Components.ComponentBase;

namespace IdeaCms.Core.Rendering;

/// <summary>Visible placeholder for an unresolved/stale include (never a crash). A plain Blazor
/// component — MindAttic's content bases are the four <see cref="IdeaBase"/> kinds, not this.
/// RFC 0001 "clickable upload-to-fix": the placeholder is a LINK to the admin upload surface with
/// the missing reference carried in the query, so an admin fixes the page by uploading the
/// .idea it names — no hunting required.</summary>
public sealed class MissingContent : BlazorComponentBase
{
    [Parameter] public string Key { get; set; } = "";

    /// <summary>The admin upload deep link this placeholder targets for the given missing key.</summary>
    public static string UploadToFixHref(string key) => "/admin/upload?missing=" + Uri.EscapeDataString(key);

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        // A visible placeholder box for an unresolved <Kind.Key /> tag (missing/disabled .idea). Styled inline
        // so it shows even before any theme/app CSS loads; .ma-missing-content lets a theme restyle it.
        b.OpenElement(0, "a");
        b.AddAttribute(1, "class", "ma-missing-content");
        b.AddAttribute(2, "data-ma-missing-key", Key);
        b.AddAttribute(3, "title", $"Not found: {Key} — click to upload the .idea that provides it");
        b.AddAttribute(4, "href", UploadToFixHref(Key));
        b.AddAttribute(5, "style",
            "display:inline-block;padding:.15rem .5rem;border:1px dashed #c0392b;border-radius:6px;" +
            "background:rgba(192,57,43,.08);color:#c0392b;font:600 .8rem/1.4 ui-monospace,monospace;" +
            "text-decoration:none;cursor:pointer;");
        b.AddContent(6, $"⚠ {Key} not found — upload to fix");
        b.CloseElement();
    }
}

/// <summary>Rendered when a Code page's type can't be resolved (stale/renamed). Never a crash.</summary>
public sealed class MissingPageHost : PageBase
{
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddAttribute(1, "class", "ma-missing-page");
        b.AddContent(2, "This page's component is not available.");
        b.CloseElement();
    }
}
