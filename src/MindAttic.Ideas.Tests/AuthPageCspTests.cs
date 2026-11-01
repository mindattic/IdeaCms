using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MindAttic.Authentication.Options;
using MindAttic.Authentication.Web;

namespace MindAttic.Ideas.Tests;

/// <summary>
/// M.IDEA-T36 (same bug as IdiotProof's IP-T350): /login, /mfa and /account render through Ideas'
/// App.razor but carry MindAttic.Authentication's strict CSP (default-src 'self', script/style 'self'
/// + a per-request nonce). App.razor had an inline theme-mode script, and its &lt;ImportMap /&gt; renders
/// an inline import map; the CSP blocked both on the sign-in page. These read the real header the
/// library sends and check every stylesheet and script the shell loads against it, that the shell
/// has no literal inline script, that the import map carries the nonce, and that the CSP still
/// forbids inline scripts and styles.
/// </summary>
[TestFixture]
public sealed class AuthPageCspTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MindAttic.Ideas.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (MindAttic.Ideas.slnx) not found.");
    }

    private static string BlazorRoot => Path.Combine(RepoRoot(), "src", "MindAttic.Ideas.Blazor");
    private static string WwwRoot => Path.Combine(BlazorRoot, "wwwroot");
    /// <summary>App.razor's markup with Razor (@* *@) and HTML comments removed, so prose about tags isn't read as tags.</summary>
    private static string AppShell() =>
        Regex.Replace(File.ReadAllText(Path.Combine(BlazorRoot, "Components", "App.razor")), @"@\*.*?\*@|<!--.*?-->", "", RegexOptions.Singleline);

    /// <summary>Hosts just the library's middleware (as Program.cs wires it) and reads the auth surface's CSP and nonce.</summary>
    private static async Task<(string Csp, string? Nonce)> AuthSurfaceCspAsync(string path = "/login")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddAuthorization();
        builder.Services.Configure<MfaOptions>(_ => { });
        await using var app = builder.Build();
        app.UseMindAtticAuthentication();
        // App.razor reads the nonce from this HttpContext item; echo it back to prove it's there.
        app.MapGet(path, (Microsoft.AspNetCore.Http.HttpContext ctx) => ctx.Items[MindAtticAuthAppExtensions.CspNonceItem] as string ?? "");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            using var response = await http.GetAsync(path);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.TryGetValues("Content-Security-Policy", out var values), Is.True,
                "MindAttic.Authentication no longer sets a CSP on the auth surface; revisit this test.");
            var nonce = await response.Content.ReadAsStringAsync();
            return (string.Join(";", values!), string.IsNullOrEmpty(nonce) ? null : nonce);
        }
        finally { await app.StopAsync(); }
    }

    private static Dictionary<string, string[]> Directives(string csp) =>
        csp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Select(d => d.Split(' ', StringSplitOptions.RemoveEmptyEntries))
           .GroupBy(p => p[0].ToLowerInvariant())
           .ToDictionary(g => g.Key, g => g.First().Skip(1).ToArray());

    /// <summary>The CSP3 fallback chain: script-src-elem → script-src → default-src, etc.</summary>
    private static string[] Effective(Dictionary<string, string[]> d, params string[] chain)
    {
        foreach (var name in chain)
            if (d.TryGetValue(name, out var sources)) return sources;
        return [];
    }

    /// <summary>Whether a non-inline URL from the page is allowed by a source list (no nonce on it).</summary>
    private static bool Allows(string[] sources, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var abs) || abs.Scheme == "file")
            return sources.Contains("'self'");                         // same-origin relative URL
        return sources.Any(s => s == "*" || s.Equals($"{abs.Scheme}:", StringComparison.OrdinalIgnoreCase)
            || s.TrimEnd('/').Equals($"{abs.Scheme}://{abs.Host}", StringComparison.OrdinalIgnoreCase)
            || s.TrimEnd('/').Equals(abs.Host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>An attribute value from matching tags; @Assets["x"] is reduced to x.</summary>
    private static IEnumerable<string> Attr(string html, string tag, string attr, string? mustContain = null) =>
        Regex.Matches(html, $@"<{tag}\b[^>]*>", RegexOptions.IgnoreCase)
             .Select(m => m.Value)
             .Where(t => mustContain is null || t.Contains(mustContain, StringComparison.OrdinalIgnoreCase))
             .Select(t => Regex.Match(t, $@"\b{attr}\s*=\s*""(?:@Assets\[""([^""]*)""\]|([^""]*))""", RegexOptions.IgnoreCase))
             .Where(m => m.Success)
             .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

    /// <summary>
    /// Whether a shell URL exists: a wwwroot file, the build's scoped-CSS bundle, or a framework
    /// file (_framework/ is served by ASP.NET Core, not wwwroot).
    /// </summary>
    private static bool ExistsOnDisk(string url)
    {
        if (url.StartsWith("_framework/", StringComparison.Ordinal)) return true;
        if (url == "MindAttic.Ideas.Blazor.styles.css") return true; // generated from the *.razor.css files at build
        return File.Exists(Path.Combine(WwwRoot, url.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Test]
    public async Task AuthSurfaceCsp_StillForbidsInlineScriptsAndStyles()
    {
        var (csp, nonce) = await AuthSurfaceCspAsync();
        var d = Directives(csp);
        var scripts = Effective(d, "script-src-elem", "script-src", "default-src");
        var styles = Effective(d, "style-src-elem", "style-src", "default-src");
        Assert.Multiple(() =>
        {
            Assert.That(scripts, Does.Not.Contain("'unsafe-inline'"));
            Assert.That(scripts, Does.Not.Contain("'unsafe-eval'"));
            Assert.That(styles, Does.Not.Contain("'unsafe-inline'"));
            Assert.That(nonce, Is.Not.Null, "the library no longer exposes the per-request nonce App.razor gives the import map");
            Assert.That(scripts, Does.Contain($"'nonce-{nonce}'"), "the nonce App.razor reads is not the one the CSP allows");
        });
    }

    [Test]
    public async Task AppShell_EveryStylesheetAndScript_IsAllowedByTheAuthSurfaceCspAndExists()
    {
        var d = Directives((await AuthSurfaceCspAsync()).Csp);
        var scripts = Effective(d, "script-src-elem", "script-src", "default-src");
        var styles = Effective(d, "style-src-elem", "style-src", "default-src");
        var fonts = Effective(d, "font-src", "default-src");
        var shell = AppShell();

        var stylesheets = Attr(shell, "link", "href", "stylesheet").ToList();
        var scriptSrcs = Attr(shell, "script", "src").ToList();
        Assert.That(stylesheets, Is.Not.Empty);
        Assert.That(scriptSrcs, Is.Not.Empty);

        // Fonts and other url(...) references in the shell's own stylesheets.
        var fontUrls = new List<string>();
        foreach (var css in stylesheets.Where(h => !h.Contains("://") && File.Exists(Path.Combine(WwwRoot, h))))
            foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(WwwRoot, css)), @"url\(\s*[""']?([^""')]+?)[""']?\s*\)"))
            {
                var u = m.Groups[1].Value;
                if (Regex.IsMatch(u, @"\.(woff2?|ttf|otf)([?#]|$)") || u.Contains("fonts.gstatic.com")) fontUrls.Add(u);
            }
        var imports = stylesheets.Where(h => !h.Contains("://") && File.Exists(Path.Combine(WwwRoot, h)))
            .SelectMany(css => Regex.Matches(File.ReadAllText(Path.Combine(WwwRoot, css)), @"@import\s+(?:url\()?\s*[""']?([^""');]+)")
                                    .Select(m => m.Groups[1].Value))
            .ToList();

        Assert.Multiple(() =>
        {
            foreach (var href in stylesheets)
            {
                Assert.That(Allows(styles, href), Is.True, $"CSP style-src blocks stylesheet {href}");
                Assert.That(ExistsOnDisk(href), Is.True, $"stylesheet {href} is not in wwwroot");
            }
            foreach (var src in scriptSrcs)
            {
                Assert.That(Allows(scripts, src), Is.True, $"CSP script-src blocks script {src}");
                Assert.That(ExistsOnDisk(src), Is.True, $"script {src} is not in wwwroot");
            }
            foreach (var f in fontUrls) Assert.That(Allows(fonts, f), Is.True, $"CSP font-src blocks font {f}");
            foreach (var i in imports) Assert.That(Allows(styles, i), Is.True, $"CSP style-src blocks @import {i}");
        });
    }

    [Test]
    public void AppShell_HasNoInlineScripts_AndTheImportMapCarriesTheNonce()
    {
        var shell = AppShell();
        var inline = Regex.Matches(shell, @"<script\b(?![^>]*\bsrc\s*=)[^>]*>", RegexOptions.IgnoreCase)
                          .Select(m => m.Value).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(inline, Is.Empty, "an inline <script> in App.razor is blocked by the auth pages' CSP; move it to wwwroot/js");
            // <ImportMap /> renders an inline <script type="importmap">; without the nonce the CSP blocks it.
            foreach (Match m in Regex.Matches(shell, @"<ImportMap\b[^>]*>"))
                Assert.That(m.Value, Does.Match(@"\bnonce\s*=\s*""@_cspNonce"""), "<ImportMap /> must pass the auth surface's CSP nonce");
            Assert.That(shell, Does.Contain("MindAtticAuthAppExtensions.CspNonceItem"), "App.razor no longer reads the library's CSP nonce");
        });
    }
}
