using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Rendering;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests;

/// <summary>
/// Cascade tier 3 (the page-level &lt;style&gt; block <see cref="FreeFormPage"/> emits) is a raw markup
/// emission like any other, so it is trust-keyed (M.IDEA-LAW-5). Regression: PageCss was written into the
/// style block VERBATIM regardless of trust, so an Untrusted page could close the block and follow it
/// with a &lt;script&gt; — bypassing the body sanitizer entirely. PageCss reaches an Untrusted page via a
/// <c>--untrusted</c> bundle import, a history restore by a non-raw-markup author, or an admin whose
/// AuthorRawMarkup claim is withheld pending MFA.
/// </summary>
[TestFixture]
public class FreeFormPageCssTests
{
    /// <summary>Wraps <see cref="FreeFormPage"/> in the IRenderContext cascade the host normally supplies.</summary>
    private sealed class CascadeHost : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Parameter] public IRenderContext Ctx { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<CascadingValue<IRenderContext>>(0);
            b.AddComponentParameter(1, "Value", Ctx);
            b.AddComponentParameter(2, "IsFixed", true);
            b.AddComponentParameter(3, "ChildContent", (RenderFragment)(cb =>
            {
                cb.OpenComponent<FreeFormPage>(0);
                cb.CloseComponent();
            }));
            b.CloseComponent();
        }
    }

    private static async Task<string> RenderAsync(string? css, string? html, bool trusted)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IContentCatalog>(new ContentCatalog(new DefaultTypeResolver()));
        services.AddSingleton<IRawContentGate, RawContentGate>();
        services.AddSingleton<IRenderAlertSink, NullRenderAlertSink>();
        await using var provider = services.BuildServiceProvider();

        var ctx = new CmsRenderContext
        {
            InstanceId = Guid.NewGuid(),
            Mode = ContentMode.View,
            RenderMode = CmsRenderMode.Static,
            Page = new CmsPageContext
            {
                PageId = Guid.NewGuid(), Slug = "styled", Title = "Styled",
                Inline = new CmsInlineMarkup { Css = css, Html = html, Trusted = trusted },
            },
            Site = new CmsSiteContext { SiteId = Guid.Empty, Key = "default", Host = "localhost", DefaultThemeKey = "" },
            Services = provider,
        };

        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<CascadeHost>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Ctx"] = ctx }));
            return output.ToHtmlString();
        });
    }

    [Test]
    public async Task UntrustedPageCss_CannotCloseTheStyleBlock()
    {
        // The breakout payload: close <style>, then run script. The body is empty, so any <script>
        // in the output could only have come out of the stylesheet.
        const string payload = "body{color:red}</style><script>alert(1)</script><style>";

        var html = await RenderAsync(payload, html: null, trusted: false);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("</style><script>"),
                "untrusted PageCss must not be able to close the style block");
            Assert.That(html, Does.Not.Contain("<script>alert(1)</script>"),
                "untrusted PageCss must not smuggle an executable script into the page");
            Assert.That(html, Does.Contain("body{color:red}"),
                "the legitimate CSS itself must still render");
        });
    }

    [Test]
    public async Task PageCss_IsWrappedInThePageCascadeLayer()
    {
        // Page CSS beats Global/Theme CSS by cascade-layer precedence, not by accidental document-order
        // + matching specificity — so !important is never needed (M.IDEA-§4.7). Component beats Page
        // (M.IDEA-§4.7); this test only pins the wrapper itself, not the cross-tier ordering.
        var html = await RenderAsync("body{color:red}", html: null, trusted: true);

        Assert.That(html, Does.Contain("@layer page {"));
    }

    [Test]
    public async Task AuthorPageCss_IsEmittedVerbatim()
    {
        // M.IDEA-LAW-5: Author trust is raw passthrough. An admin who deliberately writes a "</" sequence
        // (e.g. inside a content: "…" string) gets exactly what they wrote.
        const string css = """.q::after{content:"</end>"}""";

        var html = await RenderAsync(css, html: null, trusted: true);

        Assert.That(html, Does.Contain(css), "Author-trusted PageCss must not be rewritten");
    }
}
