using System.IO.Compression;
using System.Text.Json;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Portability;
using IdeaCms.Core.Rendering;
using IdeaCms.Core.Services;
using IdeaCms.Packaging;

namespace IdeaCms.Tests;

/// <summary>M.IDEA-§4.6: page bodies are validated at save, citizens at pack, and both again in this suite.</summary>
[TestFixture]
public class PageMarkupValidatorTests
{
    private static readonly CitizenSchemaLookup Lookup = (kind, key, _) =>
        kind == ContentKind.Component && key == "card"
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Radius"] = "string", ["Columns"] = "integer", ["Animate"] = "bool" }
            : null;

    private static IReadOnlyList<PageIssue> Validate(string body, ContentTrust trust = ContentTrust.Author) =>
        PageMarkupValidator.Validate(body, trust, Lookup, new RawContentGate());

    [Test]
    public void CleanPage_HasNoIssues() =>
        Assert.That(Validate("""<p>Hi</p><Component.Card radius="8px" columns="3" animate class="x" data-foo="1" />"""), Is.Empty);

    [Test]
    public void UnknownCitizen_IsAnError() =>
        Assert.That(Validate("<Component.Nope />").Single().Severity, Is.EqualTo(PageIssueSeverity.Error));

    [Test]
    public void UndeclaredAttribute_Warns_AndBadTypedValue_Errors()
    {
        var issues = Validate("""<Component.Card colour="red" columns="three" />""");
        Assert.Multiple(() =>
        {
            Assert.That(issues.Any(i => i.Severity == PageIssueSeverity.Warning && i.Message.Contains("'colour'")), Is.True);
            Assert.That(issues.Any(i => i.Severity == PageIssueSeverity.Error && i.Message.Contains("columns=")), Is.True);
        });
    }

    [Test]
    public void WhatTheSanitizerStrips_IsReported()
    {
        var issues = Validate("""<p onclick="x()">a</p><script>alert(1)</script>""");
        Assert.Multiple(() =>
        {
            Assert.That(issues.Any(i => i.Message.Contains("onclick")), Is.True);
            Assert.That(issues.Any(i => i.Message.Contains("<script>")), Is.True);
        });
    }

    [Test]
    public void UntrustedPageWithComponents_WarnsTheyWontRender() =>
        Assert.That(Validate("<Component.Card />", ContentTrust.Untrusted).Any(i => i.Message.Contains("Untrusted")), Is.True);
}

[TestFixture]
public class CitizenValidatorTests
{
    [TestCase("x.js", "var y = eval(code);")]
    [TestCase("x.js", "var f = new Function('a', 'return a');")]
    [TestCase("x.js", "document.write('<b>')")]
    [TestCase("x.js", "setTimeout(\"go()\", 10)")]
    [TestCase("x.css", ".a{width:expression(alert(1))}")]
    [TestCase("x.css", ".a{background:url(javascript:alert(1))}")]
    [TestCase("x.css", ".a{behavior:url(x.htc)}")]
    public void UnsafeAssetPatterns_AreRejected(string path, string content) =>
        Assert.That(CitizenValidator.ValidateAssets([(path, content)]), Is.Not.Empty);

    [TestCase("x.css", "html{scroll-behavior:smooth} .m{overscroll-behavior:contain}")]
    [TestCase("x.js", "// don't eval(anything) here\nsetTimeout(run, 10); el.evaluate();")]
    [TestCase("x.css", "/* never use expression( */ .a{color:red}")]
    public void LookalikesAndComments_AreAllowed(string path, string content) =>
        Assert.That(CitizenValidator.ValidateAssets([(path, content)]), Is.Empty);

    [Test]
    public void SettingsDifferingOnlyByCase_OrReservedNames_AreRejected()
    {
        var problems = CitizenValidator.ValidateSettings(
        [
            new IdeaManifestSetting { Name = "Radius" }, new IdeaManifestSetting { Name = "radius" },
            new IdeaManifestSetting { Name = "Kind" },
        ]);
        Assert.That(problems, Has.Count.EqualTo(2));
    }
}

/// <summary>
/// The CI gate: every package the host ships (src/IdeaCms.Blazor/library) passes the pack-time
/// citizen checks, and every page of the seed site validates against those packages' declared settings
/// with nothing silently removed by the sanitizer.
/// </summary>
[TestFixture]
public class ShippedContentValidationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IdeaCms.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static IEnumerable<string> ShippedPackages() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "IdeaCms.Blazor", "library"), "*.idea")
            .OrderBy(p => p, StringComparer.Ordinal);

    private static IdeaManifest ReadManifest(ZipArchive zip)
    {
        using var s = zip.GetEntry("idea.json")!.Open();
        using var r = new StreamReader(s);
        return ManifestReader.Read(r.ReadToEnd());
    }

    [Test]
    public void EveryShippedPackage_PassesCitizenValidation()
    {
        var failures = new List<string>();
        var count = 0;
        foreach (var path in ShippedPackages())
        {
            count++;
            using var zip = ZipFile.OpenRead(path);
            var manifest = ReadManifest(zip);
            var assets = zip.Entries
                .Where(e => e.FullName.StartsWith("wwwroot/", StringComparison.Ordinal)
                            && (e.Name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".css", StringComparison.OrdinalIgnoreCase)))
                .Select(e => { using var r = new StreamReader(e.Open()); return (e.FullName, r.ReadToEnd()); })
                .ToList();
            failures.AddRange(CitizenValidator.ValidateSettings(manifest.Settings).Select(p => $"{Path.GetFileName(path)}: {p}"));
            failures.AddRange(CitizenValidator.ValidateAssets(assets).Select(p => $"{Path.GetFileName(path)}: {p}"));
        }
        Assert.That(count, Is.GreaterThan(0), "no shipped packages found");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    // An absolute asset URL a package hard-codes: /_ideas/{Category}/{key}/{version}/{path}.
    private static readonly System.Text.RegularExpressions.Regex OwnAssetUrl = new(
        @"/_ideas/(?<cat>[A-Za-z]+)/(?<key>[A-Za-z0-9_.-]+)/(?<ver>\d+)(?<path>/[^'""\s)?#`]*)?",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// A package that hard-codes its own asset URLs (e.g. Cyberspace's circuitboard textures in
    /// circuitboard-srcs.js) must use the mount the host actually serves it under,
    /// <c>/_ideas/{manifest category}/{key}/{version}</c>, and every file it names must be in its wwwroot.
    /// A stale category (Plugin vs. Widget) or version 404s silently in the browser.
    /// </summary>
    [Test]
    public void EveryShippedPackage_SelfReferencingAssetUrls_MatchItsMountAndExist()
    {
        var failures = new List<string>();
        var checkedUrls = 0;
        foreach (var path in ShippedPackages())
        {
            using var zip = ZipFile.OpenRead(path);
            var manifest = ReadManifest(zip);
            var entries = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
            foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("wwwroot/", StringComparison.Ordinal)
                         && (e.Name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".css", StringComparison.OrdinalIgnoreCase))))
            {
                string text;
                using (var r = new StreamReader(e.Open())) text = r.ReadToEnd();
                failures.AddRange(CheckOwnAssetUrls(text, manifest.Category, manifest.Key, manifest.Version,
                    rel => entries.Contains("wwwroot/" + rel), $"{Path.GetFileName(path)}:{e.FullName}", ref checkedUrls));
            }
        }
        Assert.That(checkedUrls, Is.GreaterThan(0), "no self-referencing asset URLs found (the Cyberspace textures should be)");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// The same contract on the maintained library SOURCE (library/{Plugins,Components,Themes}/X/assets),
    /// so a wrong mount is caught before anything is packed.
    /// </summary>
    [Test]
    public void EveryLibrarySource_SelfReferencingAssetUrls_MatchItsMountAndExist()
    {
        var failures = new List<string>();
        var checkedUrls = 0;
        var library = Path.Combine(RepoRoot(), "library");
        foreach (var (folder, category) in new[] { ("Plugins", "Plugin"), ("Components", "Component"), ("Themes", "Theme") })
        {
            var root = Path.Combine(library, folder);
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var assets = Path.Combine(dir, "assets");
                if (!Directory.Exists(assets)) continue;
                var key = Path.GetFileName(dir).ToLowerInvariant();
                foreach (var file in Directory.EnumerateFiles(assets, "*.*", SearchOption.AllDirectories)
                             .Where(f => f.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".css", StringComparison.OrdinalIgnoreCase)))
                {
                    failures.AddRange(CheckOwnAssetUrls(File.ReadAllText(file), category, key, version: null,
                        rel => File.Exists(Path.Combine(assets, rel.Replace('/', Path.DirectorySeparatorChar))),
                        Path.GetRelativePath(library, file), ref checkedUrls));
                }
            }
        }
        Assert.That(checkedUrls, Is.GreaterThan(0), "no self-referencing asset URLs found (the Cyberspace textures should be)");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    [Test]
    public void SelfReferencingAssetUrlCheck_FlagsAWrongCategory()
    {
        // The archived Ideas.Library shape: Plugin textures addressed under a different category.
        var count = 0;
        var problems = CheckOwnAssetUrls("x = ['/_ideas/Widget/cyberspace/1/assets/circuitboard.00.png'];",
            "Plugin", "cyberspace", 1, _ => true, "circuitboard-srcs.js", ref count);
        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain("/_ideas/Plugin/cyberspace/1"));

        problems = CheckOwnAssetUrls("x = '/_ideas/Plugin/cyberspace/1/assets/missing.png';",
            "Plugin", "cyberspace", 1, _ => false, "circuitboard-srcs.js", ref count);
        Assert.That(problems, Has.Count.EqualTo(1));
        Assert.That(problems[0], Does.Contain("missing.png"));
    }

    private static List<string> CheckOwnAssetUrls(string text, string category, string key, int? version,
        Func<string, bool> assetExists, string where, ref int checkedUrls)
    {
        var problems = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in OwnAssetUrl.Matches(text))
        {
            if (!string.Equals(m.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase)) continue;   // another package's URL
            var cat = m.Groups["cat"].Value;
            // Same key under ANOTHER live category is a different package (Theme.Cyberspace composing
            // Plugin.Cyberspace). A category the host doesn't serve (e.g. the retired "Widget") is a bad mount.
            if (!string.Equals(cat, category, StringComparison.Ordinal) && Enum.TryParse<ContentKind>(cat, ignoreCase: false, out _)) continue;
            checkedUrls++;
            var url = m.Value;
            var expectedMount = version is { } v ? $"/_ideas/{category}/{key}/{v}" : $"/_ideas/{category}/{key}/{m.Groups["ver"].Value}";
            if (!string.Equals(m.Groups["cat"].Value, category, StringComparison.Ordinal)
                || (version is { } ver && m.Groups["ver"].Value != ver.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                problems.Add($"{where}: {url} is not under this package's mount {expectedMount}");
                continue;
            }
            var rel = m.Groups["path"].Value.TrimStart('/').TrimEnd('.', ',', ';', ':');   // URLs quoted in prose comments
            if (rel.Length > 0 && !rel.EndsWith('/') && !assetExists(rel))
                problems.Add($"{where}: {url} names {rel}, which is not in the package's assets");
        }
        return problems;
    }

    [Test]
    public void EverySeedPage_ValidatesAgainstTheShippedPackages()
    {
        var schemas = new Dictionary<(ContentKind, string), IReadOnlyDictionary<string, string>>();
        foreach (var path in ShippedPackages())
        {
            using var zip = ZipFile.OpenRead(path);
            var m = ReadManifest(zip);
            if (!Enum.TryParse<ContentKind>(m.Category, out var kind)) continue;
            schemas[(kind, m.Key.ToLowerInvariant())] = m.Settings.ToDictionary(s => s.Name, s => s.Type, StringComparer.OrdinalIgnoreCase);
        }
        CitizenSchemaLookup lookup = (kind, key, _) => schemas.GetValueOrDefault((kind, key.ToLowerInvariant()));

        using var seed = ZipFile.OpenRead(Path.Combine(RepoRoot(), "seed", "mindattic-site.idealist"));
        IdeaList list;
        using (var s = seed.GetEntry(IdeaList.ManifestEntryName)!.Open())
            list = JsonSerializer.Deserialize<IdeaList>(s, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var gate = new RawContentGate();
        var problems = new List<string>();
        foreach (var page in list.Pages)
        {
            var trust = string.Equals(page.BodyTrust, "Author", StringComparison.OrdinalIgnoreCase) ? ContentTrust.Author : ContentTrust.Untrusted;
            foreach (var issue in PageMarkupValidator.Validate(page.BodyHtml, trust, lookup, gate))
                if (issue.Severity == PageIssueSeverity.Error || issue.Message.StartsWith("Removed by the XSS sanitizer", StringComparison.Ordinal))
                    problems.Add($"/{page.Slug}: {issue.Message}");
        }
        Assert.That(list.Pages, Is.Not.Empty);
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }
}
