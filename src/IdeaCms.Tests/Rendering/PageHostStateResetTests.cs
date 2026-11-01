using System.Text.RegularExpressions;

namespace IdeaCms.Tests.Rendering;

/// <summary>
/// PageHost is a long-lived route component and its &lt;HeadContent&gt; renders unconditionally, OUTSIDE
/// the _status switch — so any per-page field left set on an early return is still on the wire for the
/// next render. This pins every early return in OnParametersSetAsync to the one reset helper, in the same
/// source-inspection style as the interactive-circuit trap in SiteResolutionTests: there is no way to
/// assert it from a unit test of the resolver, and the failure ("the 404 still wears the last page's
/// theme", "a Forbidden page's head names the components you may not see") is invisible in a build.
/// </summary>
[TestFixture]
public class PageHostStateResetTests
{
    private static string PageHostSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IdeaCms.slnx")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "Could not locate the repo root (IdeaCms.slnx).");

        var pageHost = Path.Combine(dir!.FullName, "src", "IdeaCms.Rendering", "PageHost.razor");
        Assert.That(File.Exists(pageHost), Is.True, pageHost);
        return File.ReadAllText(pageHost);
    }

    [Test]
    public void ClearPageState_ResetsEveryFieldHeadContentReads()
    {
        var source = PageHostSource();
        var body = Regex.Match(source, @"private void ClearPageState\(\)\s*\{(.*?)\n    \}", RegexOptions.Singleline);
        Assert.That(body.Success, Is.True, "PageHost must declare a ClearPageState() reset helper.");

        // Exactly the fields <HeadContent> and the render body read off the previous page.
        foreach (var field in new[]
                 {
                     "_context", "_themeType", "_themeParams", "_bodyType",
                     "_pluginTypesBeforeBody", "_pluginTypesAfterBody",
                     "_themeGlobalCss", "_themeCss", "_themeScripts",
                     "_componentCss", "_componentScripts",
                 })
            Assert.That(body.Groups[1].Value, Does.Contain(field + " ="),
                $"ClearPageState must reset {field} — <HeadContent> renders it regardless of _status");
    }

    [TestCase("Status.NotFound")]
    [TestCase("Status.Forbidden")]
    public void EveryEarlyReturn_ClearsPageStateFirst(string statusAssignment)
    {
        var source = PageHostSource();
        var lines = source.Split('\n');

        var found = false;
        for (var i = 0; i < lines.Length; i++)
        {
            // The @if block at the top of the file reads _status; only the assignments in @code count.
            if (!lines[i].Contains("_status = " + statusAssignment, StringComparison.Ordinal)) continue;
            found = true;
            var preceding = string.Join('\n', lines[Math.Max(0, i - 4)..i]);
            Assert.That(preceding, Does.Contain("ClearPageState()"),
                $"the '{statusAssignment}' path must drop the previous page's state before it renders");
        }

        Assert.That(found, Is.True, $"PageHost no longer assigns {statusAssignment} — update this pin.");
    }
}
