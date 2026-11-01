using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using IdeaCms.Rendering;

namespace IdeaCms.Tests;

/// <summary>
/// CmsHead emits an explicit "@layer global, theme, page, component;" statement first, then attributes
/// every tier's CSS to its named layer, so the fixed M.IDEA-LAW-4 order wins by layer precedence rather
/// than by accidental document-order + matching specificity (M.IDEA-§4.7). Component comes last -- and so
/// wins -- by design (M.IDEA-§4.7).
/// </summary>
[TestFixture]
public class CmsHeadCssLayerTests
{
    private static async Task<string> RenderAsync(Dictionary<string, object?> parameters)
    {
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<CmsHead>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }

    [Test]
    public async Task LayerOrderStatement_IsEmittedOnceAndFirst_EvenWhenAllTiersAreEmpty()
    {
        var html = await RenderAsync(new Dictionary<string, object?>());

        const string statement = "@layer global, theme, page, component;";
        Assert.That(html, Does.Contain(statement));
        Assert.That(html.IndexOf(statement, StringComparison.Ordinal),
            Is.EqualTo(html.LastIndexOf(statement, StringComparison.Ordinal)),
            "the layer-order statement must be emitted exactly once");
    }

    [Test]
    public async Task LayerOrderStatement_PrecedesEveryOtherEmittedTag()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            ["GlobalCss"] = "body{color:red}",
            ["ThemeGlobalCss"] = new List<string> { "a.css" },
            ["ThemeCss"] = new List<string> { "b.css" },
            ["ComponentCss"] = new List<string> { "c.css" },
            ["ThemeScripts"] = new List<string> { "t.js" },
            ["ComponentScripts"] = new List<string> { "w.js" },
        });

        var statementIndex = html.IndexOf("@layer global, theme, page, component;", StringComparison.Ordinal);
        Assert.That(statementIndex, Is.GreaterThanOrEqualTo(0));

        foreach (var needle in new[] { "@layer global {", "@import", "<script" })
        {
            var idx = html.IndexOf(needle, StringComparison.Ordinal);
            Assert.That(idx, Is.GreaterThan(statementIndex), $"'{needle}' must come after the layer-order statement");
        }
    }

    [Test]
    public async Task ComponentLayer_IsDeclaredAfterPageLayer_SoComponentWinsPrecedence()
    {
        var html = await RenderAsync(new Dictionary<string, object?>());

        const string statement = "@layer global, theme, page, component;";
        Assert.That(html, Does.Contain(statement));
        Assert.That(statement.IndexOf("page", StringComparison.Ordinal),
            Is.LessThan(statement.IndexOf("component", StringComparison.Ordinal)),
            "page must be declared before component so component wins (M.IDEA-§4.7)");
        Assert.That(html, Does.Not.Contain("@layer global, theme, component, page;"),
            "the page-after-component order must not resurface");
    }

    [Test]
    public async Task GlobalCss_IsWrappedInItsLayer_WithBreakoutEscapingIntact()
    {
        const string payload = "body{color:red}</style><script>alert(1)</script><style>";

        var html = await RenderAsync(new Dictionary<string, object?> { ["GlobalCss"] = payload });

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("@layer global {"));
            Assert.That(html, Does.Not.Contain("</style><script>"));
            Assert.That(html, Does.Not.Contain("<script>alert(1)</script>"));
            Assert.That(html, Does.Contain("body{color:red}"));
        });
    }

    [Test]
    public async Task ThemeAndComponentUrls_AreImportedIntoTheirNamedLayers_NotAsPlainLinkTags()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            ["ThemeGlobalCss"] = new List<string> { "a.css" },
            ["ThemeCss"] = new List<string> { "b.css" },
            ["ComponentCss"] = new List<string> { "c.css" },
        });

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("@import url(\"a.css\") layer(global);"));
            Assert.That(html, Does.Contain("@import url(\"b.css\") layer(theme);"));
            Assert.That(html, Does.Contain("@import url(\"c.css\") layer(component);"));
            Assert.That(html, Does.Not.Contain("<link rel=\"stylesheet\" href=\"a.css\""));
            Assert.That(html, Does.Not.Contain("<link rel=\"stylesheet\" href=\"b.css\""));
            Assert.That(html, Does.Not.Contain("<link rel=\"stylesheet\" href=\"c.css\""));
        });
    }

    [Test]
    public async Task ComponentCssUrl_WithHostileCharacters_IsEscapedInsideTheImportStringLiteral()
    {
        const string hostileUrl = "evil.css\") layer(page);</style><script>alert(1)</script><style>@import url(\"x";

        var html = await RenderAsync(new Dictionary<string, object?>
        {
            ["ComponentCss"] = new List<string> { hostileUrl },
        });

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("<script>alert(1)</script>"));
            Assert.That(html, Does.Not.Contain("</style><script>"));
            Assert.That(html, Does.Contain("@import url(\""));
        });
    }

    [Test]
    public async Task Scripts_AreEmittedUnchanged_AsPlainScriptTags()
    {
        var html = await RenderAsync(new Dictionary<string, object?>
        {
            ["ThemeScripts"] = new List<string> { "t.js" },
            ["ComponentScripts"] = new List<string> { "w.js" },
        });

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<script src=\"t.js\"></script>"));
            Assert.That(html, Does.Contain("<script src=\"w.js\"></script>"));
        });
    }
}
