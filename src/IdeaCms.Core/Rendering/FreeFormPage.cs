using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using IdeaCms.Abstractions;

namespace IdeaCms.Core.Rendering;

/// <summary>
/// The built-in renderer for a Data page (RenderStrategy.RawMarkup): emits the page's free-form
/// author content — page-level CSS (cascade tier 3), the expanded HTML body (with &lt;ma-component&gt;
/// includes turned into live components), and the author's inline JS (only when Author-trusted).
/// </summary>
public sealed class FreeFormPage : PageBase
{
    [Inject] public IContentCatalog Catalog { get; set; } = default!;
    [Inject] public IRawContentGate Gate { get; set; } = default!;
    [Inject] public IRenderAlertSink Alerts { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var inline = Context.Page.Inline;
        var trust = inline.Trusted ? ContentTrust.Author : ContentTrust.Untrusted;
        var seq = 0;

        // Cascade tier 3: page-level stylesheet. Trust-keyed like every other raw emission (M.IDEA-LAW-5):
        // an Author's CSS goes in verbatim, but Untrusted CSS has its "</" sequences escaped so it cannot
        // close the <style> block and smuggle a <script> past the body sanitizer — the same breakout guard
        // CmsHead applies to the global stylesheet setting. PageCss reaches an Untrusted page through a
        // bundle import (--untrusted), a history restore by a non-raw-markup author, or an admin whose
        // AuthorRawMarkup claim is withheld pending MFA.
        //
        // The trust-gated text is wrapped in "@layer page { }" so it beats Global/Theme CSS by
        // cascade-layer precedence regardless of selector specificity, with no !important needed — but
        // Component beats Page (M.IDEA-§4.7: a Component citizen's own
        // stylesheet outranks whatever a Page author writes, since a Component is meant to guarantee its
        // own presentation). The wrapper is renderer-owned literal text added AFTER the trust decision
        // above, so it changes neither the verbatim-vs-escaped choice nor the escaped text itself.
        if (!string.IsNullOrWhiteSpace(inline.Css))
        {
            var css = trust == ContentTrust.Author
                ? inline.Css
                : inline.Css!.Replace("</", "<\\/", StringComparison.OrdinalIgnoreCase);
            // One markup frame for the WHOLE element, never a markup child of an OpenElement("style"):
            // interactive Blazor inserts a markup child via a <template> innerHTML parse outside raw-text
            // context, so any "<main>" in a CSS comment becomes a real element and every rule after it
            // drops out of the sheet. Parsing "<style>…</style>" as a unit keeps its body raw text.
            builder.AddMarkupContent(seq++, $"<style>@layer page {{\n{css}\n}}</style>");
        }

        // Free-form body with <IdeaCms.{Kind}.{Name}.V{n}> includes. A missing/disabled
        // reference degrades to a placeholder AND raises an Admin Inbox alert (a page must never be invalid).
        IncludeExpander.Expand(builder, ref seq, inline.Html, Catalog, Gate, trust,
            Alerts, Context.Page.PageId, Context.Page.Slug);

        // Intentional author JS — emitted only for Author-trusted pages.
        if (trust == ContentTrust.Author && !string.IsNullOrWhiteSpace(inline.Js))
        {
            builder.AddMarkupContent(seq++, $"<script>{inline.Js}</script>");
        }
    }
}
